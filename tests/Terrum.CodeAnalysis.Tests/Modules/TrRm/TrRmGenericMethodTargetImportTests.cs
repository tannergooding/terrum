// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmGenericMethodTargetImportTests
{
    private const string SchemaIdentity
        = "trrm/1-draft-generic-method-calls-parameter-labels-1";
    private const string ConsumerIdentity = "generic.relay";
    private const string ProviderIdentity = "generic.transport";
    private const string ProviderKey = "transport.acquire/v4";
    private const string ExpectedSource = """
        T Expected<T, U = T>(T expectedValue) { return expectedValue; }
        public T Forward<T>(T callerValue) {
            return Expected<T>(callerValue);
        }
        """;
    private const string NamedArgumentExpectedSource = """
        T Expected<T, U = T>(U otherValue, T expectedValue) {
            return expectedValue;
        }
        public T Forward<T>(T callerValue, T otherValue) {
            return Expected<T>(expectedValue: callerValue,
                otherValue: otherValue);
        }
        """;
    private const string ProviderSource = """
        int32 Padding<Marker>(int32 value) { return value; }
        public V Actual<V, W = V>(V providerValue) {
            return providerValue;
        }
        """;
    private const string NamedArgumentProviderSource = """
        int32 Padding<Marker>(int32 value) { return value; }
        public V Actual<V, W = V>(
            W providerValue, V providerExpected) {
            return providerExpected;
        }
        """;

    [TestCase(false)]
    [TestCase(true)]
    public void ReconstructsExactExpectedImportAndProviderSnapshot(
        bool directExpectedReference)
    {
        var consumer = CreateImportArtifact(NamedArgumentExpectedSource,
            directExpectedReference: directExpectedReference);
        var provider = EmitProvider(NamedArgumentProviderSource);
        var consumerDigest = SHA256.HashData(consumer);
        var providerDigest = SHA256.HashData(provider);
        var reference = CreateReference(consumer, provider);
        var forwarded = reference.GenericMethodImports.Single();
        var call = (BoundCall)((BoundReturn)forwarded.ProofFunction.Body
            .Statements.Single()).Value!;
        var target = call.ImportedGenericMethodCall!;
        var binding = target.Binding!;

        Assert.That(call.Arguments, Has.Length.EqualTo(2));
        Assert.That(((BoundRead)call.Arguments[0]).Variable, Is.EqualTo(1));
        Assert.That(((BoundRead)call.Arguments[1]).Variable, Is.Zero);
        Assert.That(binding.ImportRow, Is.EqualTo(1));
        Assert.That(binding.ModuleRefRow, Is.EqualTo(1));
        Assert.That(binding.ProviderIdentity, Is.EqualTo(ProviderIdentity));
        Assert.That(binding.ExportKey, Is.EqualTo(ProviderKey));
        Assert.That(binding.Expected.Origin.ProviderIdentity,
            Is.EqualTo(ConsumerIdentity));
        Assert.That(binding.Expected.Origin.MethodDefId,
            Is.EqualTo(0x1600_0001));
        Assert.That(binding.Expected.Origin.ProviderArtifactDigest,
            Is.EqualTo(consumerDigest));
        Assert.That(binding.Expected.ProofFunction.IsExternal, Is.True);
        Assert.That(binding.Expected.ProofFunction.Variables[0].Name,
            Is.EqualTo("otherValue"));
        Assert.That(binding.Expected.ProofFunction.Variables[1].Name,
            Is.EqualTo("expectedValue"));
        Assert.That(binding.Expected.Formals.Select(static formal =>
            formal.Name), Is.EqualTo(new[] { "T", "U" }));
        Assert.That(binding.Provider.Origin.ProviderIdentity,
            Is.EqualTo(ProviderIdentity));
        Assert.That(binding.Provider.Origin.ExportKey,
            Is.EqualTo(ProviderKey));
        Assert.That(binding.Provider.Origin.MethodDefId,
            Is.EqualTo(0x1600_0002));
        Assert.That(binding.Provider.Origin.ProviderArtifactDigest,
            Is.EqualTo(providerDigest));
        Assert.That(binding.Provider.Formals.Select(static formal =>
            formal.Name), Is.EqualTo(new[] { "V", "W" }));
        Assert.That(binding.Provider.ProofFunction.Variables[0].Name,
            Is.EqualTo("providerValue"));
        Assert.That(target.Signature, Is.SameAs(binding.Expected.Signature));
        Assert.That(target.Formals[1].TypeDefault!.Term.FormalOrdinal,
            Is.Zero);
        Assert.That(target.ProofFunction.Body,
            Is.SameAs(binding.Provider.ProofFunction.Body));
        Assert.That(target.ProofFunction.Variables[0].Name,
            Is.EqualTo("otherValue"));
        Assert.That(target.ProofFunction.Variables[1].Name,
            Is.EqualTo("expectedValue"));
        Assert.That(forwarded.ProofFunction.Variables[0].Name,
            Is.EqualTo("callerValue"));
        Assert.That(call.ImportedGenericMethodTypeArguments,
            Is.EqualTo(new[] {
                forwarded.Formals[0].PlaceholderType,
                forwarded.Formals[0].PlaceholderType,
            }));

        Array.Clear(consumer);
        Array.Clear(provider);
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Forward<int32>(42, 43); }"), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(compilation.Program!);
        Assert.That(binding.Provider.Origin.ProviderArtifactDigest,
            Is.EqualTo(providerDigest));
    }

    [TestCase("Span<Span<V>>", true)]
    [TestCase("Span<V>", false)]
    public void BindsNestedSpanTypeDefaultsStructurally(
        string providerDefault, bool expectedCompatible)
    {
        var consumer = CreateImportArtifact("""
            alias NestedSpan<T> = Span<Span<T>>;
            T Expected<T, U = NestedSpan<T>>(T expectedValue) {
                return expectedValue;
            }
            public T Forward<T>(T callerValue) {
                return Expected<T>(callerValue);
            }
            """);
        var provider = EmitProvider(
            "public V Actual<V, W = " + providerDefault
            + ">(V providerValue) { return providerValue; }");
        if (!expectedCompatible)
        {
            _ = Assert.Throws<InvalidDataException>(() =>
                CreateReference(consumer, provider));
            return;
        }

        var reference = CreateReference(consumer, provider);
        var forwarded = reference.GenericMethodImports.Single();
        var call = (BoundCall)((BoundReturn)forwarded.ProofFunction.Body
            .Statements.Single()).Value!;
        var binding = call.ImportedGenericMethodCall!.Binding!;
        var expectedDefault = binding.Expected.Formals[1].TypeDefault!;
        var providerDefaultTerm = binding.Provider.Formals[1]
            .TypeDefault!.Term;

        Assert.That(expectedDefault.Term.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));
        Assert.That(expectedDefault.Term.TypeArguments[0].Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));
        Assert.That(providerDefaultTerm.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));
        Assert.That(providerDefaultTerm.TypeArguments[0].Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));

        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Forward<int32>(42); }"), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(compilation.Program!);
    }

    [TestCase("Span<write V>", true)]
    [TestCase("Span<V>", false)]
    public void BindsInnerWritableSpanAliasDefaultsStructurally(
        string providerDefault, bool expectedCompatible)
    {
        var consumer = CreateImportArtifact("""
            alias ElementWritableSpan<T> = Span<write T>;
            T Expected<T, U = ElementWritableSpan<T>>(T expectedValue)
                where T : write {
                return expectedValue;
            }
            public T Forward<T>(T callerValue) where T : write {
                return Expected<T>(callerValue);
            }
            """);
        var provider = EmitProvider(
            "public V Actual<V, W = " + providerDefault
            + ">(V providerValue) where V : write { return providerValue; }");
        if (!expectedCompatible)
        {
            _ = Assert.Throws<InvalidDataException>(() =>
                CreateReference(consumer, provider));
            return;
        }

        var reference = CreateReference(consumer, provider);
        var forwarded = reference.GenericMethodImports.Single();
        BoundGenericMethodImport? target = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(
            forwarded.ProofFunction.Body, expression =>
            {
                if (expression is BoundCall {
                        ImportedGenericMethodCall: {
                            Binding: not null,
                        } importedTarget,
                    })
                {
                    target = importedTarget;
                }
            }), Is.True);
        Assert.That(target, Is.Not.Null);
        var expectedDefault = target!.Binding!.Expected.Formals[1]
            .TypeDefault!.Term;
        var providerDefaultTerm = target!.Binding!.Provider.Formals[1]
            .TypeDefault!.Term;
        Assert.That(expectedDefault.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));
        Assert.That(providerDefaultTerm.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));
        Assert.That(expectedDefault.IsElementWritable, Is.True);
        Assert.That(providerDefaultTerm.IsElementWritable, Is.True);
        Assert.That(expectedDefault.TypeArguments.Single().Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Formal));
        Assert.That(providerDefaultTerm.TypeArguments.Single().Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Formal));
        Assert.That(expectedDefault.TypeArguments.Single().FormalOrdinal,
            Is.Zero);
        Assert.That(providerDefaultTerm.TypeArguments.Single().FormalOrdinal,
            Is.Zero);
    }

    [Test]
    public void MergesNestedSpanDefaultsAcrossBoundGenericProviders()
    {
        static CompilationReference CreateBoundReference(string suffix)
        {
            var consumerIdentity = "generic.relay" + suffix;
            var providerIdentity = "generic.transport" + suffix;
            var providerKey = "transport.acquire/" + suffix;
            var consumerSource = $$"""
                alias NestedSpan<T> = Span<Span<T>>;
                T Expected<T, U = NestedSpan<T>>(T expectedValue) {
                    return expectedValue;
                }
                public T Forward{{suffix}}<T>(T callerValue) {
                    return Expected<T>(callerValue);
                }
                """;
            var consumer = CreateImportArtifact(consumerSource,
                moduleIdentity: consumerIdentity,
                providerIdentity: providerIdentity,
                providerKey: providerKey);
            var provider = EmitProvider("""
                alias NestedSpan<V> = Span<Span<V>>;
                public V Actual<V, W = NestedSpan<V>>(V providerValue) {
                    return providerValue;
                }
                """, moduleIdentity: providerIdentity,
                exportKey: providerKey);
            return TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity, consumer),
                consumerIdentity, [
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        provider),
                ]);
        }

        var first = CreateBoundReference("A");
        var second = CreateBoundReference("B");
        var reference = ModuleCompiler.CreateReferenceSetFromReferences(
            [first, second]);
        var firstImport = reference.GenericMethodImports.Single(import =>
            import.Origin.ExportKey == "ForwardA");
        var secondImport = reference.GenericMethodImports.Single(import =>
            import.Origin.ExportKey == "ForwardB");
        var firstTarget = GetExpectedTarget(firstImport);
        var secondTarget = GetExpectedTarget(secondImport);
        var firstBinding = firstTarget.Binding!;
        var secondBinding = secondTarget.Binding!;

        Assert.That(firstImport.FormalTypeOwnerOrdinal,
            Is.Not.EqualTo(secondImport.FormalTypeOwnerOrdinal));
        Assert.That(firstBinding.Expected.FormalTypeOwnerOrdinal,
            Is.Not.EqualTo(secondBinding.Expected.FormalTypeOwnerOrdinal));
        Assert.That(firstBinding.Provider.FormalTypeOwnerOrdinal,
            Is.Not.EqualTo(secondBinding.Provider.FormalTypeOwnerOrdinal));
        Assert.That(firstBinding.Expected.Formals[1].TypeDefault!.Term.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));
        Assert.That(secondBinding.Expected.Formals[1].TypeDefault!.Term.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));
        Assert.That(firstTarget.Signature,
            Is.SameAs(firstBinding.Expected.Signature));
        Assert.That(secondTarget.Signature,
            Is.SameAs(secondBinding.Expected.Signature));
        Assert.That(firstTarget.ProofFunction.Body,
            Is.SameAs(firstBinding.Provider.ProofFunction.Body));
        Assert.That(secondTarget.ProofFunction.Body,
            Is.SameAs(secondBinding.Provider.ProofFunction.Body));
        Assert.That(BoundSpanTypeContract.TryCreateTypeIdentityIndex(
            reference.SpanTypes, out _), Is.True);

        var compilation = Compilation.Create(SyntaxTree.Parse("""
            alias WritableSpan<T> = Span<write T>;
            int32 Main() {
                return ConsumerForwardA<int32>(40)
                    + ConsumerForwardB<int32>(2);
            }
            public int32 ConsumerForwardA<T>(int32 value) {
                if (typeof(Span<T>) == typeof(Span<T>)) {
                    return ForwardA<T>(value);
                }
                return value;
            }
            public int32 ConsumerForwardB<T>(int32 value) {
                if (typeof(Span<T>) == typeof(Span<T>)) {
                    return ForwardB<T>(value);
                }
                return value;
            }
            public int32 ConsumerForwardWrite<T>(int32 value)
                where T : write {
                return ForwardA<WritableSpan<T>>(value);
            }
            """), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(compilation.Program!);
        AssertForwardedWritableSpan(compilation.Program!);

        var graphReference = ModuleCompiler.MergeGraphReferences(
            [first, second]);
        var graphFirstImport = graphReference.GenericMethodImports.Single(
            import => import.Origin.ExportKey == "ForwardA");
        var graphSecondImport = graphReference.GenericMethodImports.Single(
            import => import.Origin.ExportKey == "ForwardB");
        Assert.That(GetExpectedTarget(graphFirstImport).Binding, Is.Not.Null);
        Assert.That(GetExpectedTarget(graphSecondImport).Binding,
            Is.Not.Null);
        Assert.That(BoundSpanTypeContract.TryCreateTypeIdentityIndex(
            graphReference.SpanTypes, out _), Is.True);
        var graphCompilation = Compilation.Create(SyntaxTree.Parse("""
            alias WritableSpan<T> = Span<write T>;
            int32 Main() {
                return ConsumerForwardA<int32>(40)
                    + ConsumerForwardB<int32>(2);
            }
            public int32 ConsumerForwardA<T>(int32 value) {
                if (typeof(Span<T>) == typeof(Span<T>)) {
                    return ForwardA<T>(value);
                }
                return value;
            }
            public int32 ConsumerForwardB<T>(int32 value) {
                if (typeof(Span<T>) == typeof(Span<T>)) {
                    return ForwardB<T>(value);
                }
                return value;
            }
            public int32 ConsumerForwardWrite<T>(int32 value)
                where T : write {
                return ForwardA<WritableSpan<T>>(value);
            }
            """), graphReference);
        Assert.That(graphCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        SemanticVerifier.Verify(graphCompilation.Program!);
        AssertForwardedWritableSpan(graphCompilation.Program!);

        static void AssertForwardedWritableSpan(BoundProgram program)
        {
            var definition = program.GenericMethodDefinitions.Single(
                static candidate =>
                    candidate.Name == "ConsumerForwardWrite");
            var function = definition.ProofProgram.Functions[
                definition.FunctionIndex];
            BoundCall? call = null;
            Assert.That(BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall {
                            ImportedGenericMethodCall: not null,
                        } importedCall)
                    {
                        call = importedCall;
                    }
                }), Is.True);
            Assert.That(call, Is.Not.Null);
            var typeArgument
                = call!.ImportedGenericMethodTypeArguments[0];
            var span = definition.ProofProgram.SpanTypes[
                BoundSpanTypeTypes.Index(typeArgument)];
            Assert.That(span.ElementFormalDefinitionOrdinal,
                Is.EqualTo(definition.DefinitionOrdinal));
            Assert.That(span.ElementFormalOrdinal, Is.Zero);
            Assert.That(span.IsElementWritable, Is.True);
        }

        static BoundGenericMethodImport GetExpectedTarget(
            BoundGenericMethodImport import)
        {
            BoundGenericMethodImport? target = null;
            Assert.That(BoundTreeTraversal.VisitExpressions(
                import.ProofFunction.Body, expression =>
                {
                    if (expression is BoundCall {
                            ImportedGenericMethodCall:
                                BoundGenericMethodImport {
                                    Binding: not null,
                                } importedTarget,
                        })
                    {
                        target = importedTarget;
                    }
                }), Is.True);
            return target!;
        }
    }

    [Test]
    public void RejectsProviderWithDifferentInterfaceIdentity()
    {
        const string consumerSource = """
            namespace Example {
                public interface I<T> {
                    public T Echo(T value);
                }
                T Expected<T>(T expectedValue) where T : I<T> {
                    return expectedValue;
                }
                public T Forward<T>(T callerValue) where T : I<T> {
                    return Expected<T>(callerValue);
                }
            }
            """;
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(consumerSource), CreateOptions());
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program!;
        var consumerInterface = consumerProgram.Interfaces.Single();
        var consumerMember = consumerInterface.Members.Single();
        var interfaceExport = new TrRmSourceTypeExport("Example", "I",
            "relay.interface/I") {
            GenericArity = consumerInterface.Formals.Length,
            InterfaceMethodExports = [
                new TrRmSourceInterfaceMethodExport(
                    consumerMember.SourceInputOrdinal,
                    consumerMember.SourceDeclarationSpan,
                    "relay.interface/I.Echo"),
            ],
        };
        var consumer = CreateImportArtifact(consumerSource,
            typeExports: [interfaceExport]);

        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                namespace Example {
                    public interface I<T> {
                        public T Echo(T value);
                    }
                    public T Actual<T>(T providerValue) where T : I<T> {
                        return providerValue.via<I<T>>.Echo(providerValue);
                    }
                }
                """), CreateOptions());
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program!;
        var providerInterface = providerProgram.Interfaces.Single();
        var providerMember = providerInterface.Members.Single();
        var providerInterfaceExport = new TrRmSourceTypeExport(
            "Example", "I", "transport.interface/I") {
            GenericArity = providerInterface.Formals.Length,
            InterfaceMethodExports = [
                new TrRmSourceInterfaceMethodExport(
                    providerMember.SourceInputOrdinal,
                    providerMember.SourceDeclarationSpan,
                    "transport.interface/I.Echo"),
            ],
        };
        var providerMethod = providerProgram.GenericMethodDefinitions
            .Single(static definition => definition.IsPublic);
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, ProviderIdentity, SchemaIdentity, 0,
            [providerInterfaceExport], [
                new TrRmSourceGenericMethodExport(providerProgram,
                    providerMethod.SourceInputOrdinal, providerMethod.Span,
                    ProviderKey),
            ]);

        var exception = Assert.Throws<InvalidDataException>(() =>
            CreateReference(consumer, providerArtifact));
        Assert.That(exception!.Message,
            Does.Contain("interface constraint identity"));
    }

    [Test]
    public void SelectsTheSecondImportSharingAnExpectedMethodDef()
    {
        const string firstKey = "transport.first/v4";
        var consumer = CreateImportArtifact(ExpectedSource,
            providerKey: firstKey, secondImportKey: ProviderKey);
        var provider = EmitProvider(ProviderSource,
            exportKey: firstKey, alternateExportKey: ProviderKey);
        var reference = CreateReference(consumer, provider);
        var call = (BoundCall)((BoundReturn)reference.GenericMethodImports
            .Single().ProofFunction.Body.Statements.Single()).Value!;
        var binding = call.ImportedGenericMethodCall!.Binding!;
        Assert.That(binding.ImportRow, Is.EqualTo(2));
        Assert.That(binding.ModuleRefRow, Is.EqualTo(2));
        Assert.That(binding.ExportKey, Is.EqualTo(ProviderKey));
        Assert.That(binding.Provider.Origin.ExportKey,
            Is.EqualTo(ProviderKey));
        Assert.That(binding.Expected.Origin.MethodDefId,
            Is.EqualTo(0x1600_0001));
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Forward<int32>(42); }"), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(compilation.Program!);
    }

    [Test]
    public void RejectsConflictingProvidersForOneExpectedMethodDef()
    {
        const string firstIdentity = "generic.first-transport";
        var consumer = CreateImportArtifact(ExpectedSource,
            providerIdentity: firstIdentity,
            secondImportKey: ProviderKey,
            secondProviderIdentity: ProviderIdentity);
        var firstProvider = EmitProvider(ProviderSource,
            moduleIdentity: firstIdentity);
        var secondProvider = EmitProvider(ProviderSource);
        var exception = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity, consumer),
                ConsumerIdentity, [
                    new TrRmStandardArtifactInput(
                        SchemaIdentity, firstProvider),
                    new TrRmStandardArtifactInput(
                        SchemaIdentity, secondProvider),
                ]));
        Assert.That(exception!.Message,
            Does.Contain("conflicting provider definitions"));
    }

    [Test]
    public void RejectsDuplicateImportTriplesInG1()
    {
        var consumer = CreateImportArtifact(ExpectedSource,
            secondImportKey: ProviderKey);
        var imports = GetSection(consumer, 0x001C);
        var bytes = imports.Bytes.ToArray();
        bytes.AsSpan(0, 12).CopyTo(bytes.AsSpan(12));
        consumer = ReplaceSections(consumer, imports with { Bytes = bytes });
        _ = Assert.Throws<InvalidDataException>(() =>
            CreateReference(consumer, EmitProvider(ProviderSource)));
    }

    [Test]
    public void DirectExpectedStubDoesNotChooseAmongMultipleImportRows()
    {
        const string firstKey = "transport.first/v4";
        var consumer = CreateImportArtifact(ExpectedSource,
            directExpectedReference: true, providerKey: firstKey,
            secondImportKey: ProviderKey);
        var provider = EmitProvider(ProviderSource,
            exportKey: firstKey, alternateExportKey: ProviderKey);
        var exception = Assert.Throws<NotSupportedException>(() =>
            CreateReference(consumer, provider));
        Assert.That(exception!.Message,
            Does.Contain("exact MethodMap Import token"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void IgnoresUnconsumedAssignedTypeAndFieldImports(bool includeField)
    {
        var consumer = AddUnrelatedLayoutImports(
            CreateImportArtifact(ExpectedSource), includeField);
        var reference = CreateReference(consumer,
            EmitProvider(ProviderSource));
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Forward<int32>(42); }"), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(compilation.Program!);
    }

    [Test]
    public void RetainsOriginalObligationsWhenProviderRelaxesThem()
    {
        var consumer = CreateImportArtifact("""
            T Expected<T, U = T>(T value) where T : [Blittable] {
                return value;
            }
            public T Forward<T>(T value) where T : [Blittable] {
                return Expected<T>(value);
            }
            """);
        var reference = CreateReference(consumer,
            EmitProvider(ProviderSource));
        var forwarded = reference.GenericMethodImports.Single();
        var call = (BoundCall)((BoundReturn)forwarded.ProofFunction.Body
            .Statements.Single()).Value!;
        var target = call.ImportedGenericMethodCall!;
        Assert.That(target.Formals[0].RequiresBlittable, Is.True);
        Assert.That(target.Binding!.Expected.Formals[0].RequiresBlittable,
            Is.True);
        Assert.That(target.Binding.Provider.Formals[0].RequiresBlittable,
            Is.False);
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Forward<int32>(42); }"), reference,
            CreateOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(compilation.Program!);
    }

    [Test]
    public void RejectsProviderThatStrengthensInterfaceConstraints()
    {
        var consumer = CreateImportArtifact("""
            T Expected<T>(T value) { return value; }
            public T Forward<T>(T value) { return Expected<T>(value); }
            """);
        var provider = EmitProviderWithInterfaceConstraint();

        var exception = Assert.Throws<InvalidDataException>(() =>
            CreateReference(consumer, provider));
        Assert.That(exception!.Message,
            Does.Contain("strengthens an interface obligation"));
    }

    [Test]
    public void ReconstructsOwnershipTransferThroughAnExpectedImport()
    {
        var consumer = CreateImportArtifact("""
            T Expected<T>(T value) where T : unique {
                return move value;
            }
            public T Forward<T>(T value) where T : unique {
                return Expected<T>(move value);
            }
            """);
        var provider = EmitProvider("""
            public V Actual<V>(V value) where V : unique {
                return move value;
            }
            """);
        var reference = CreateReference(consumer, provider);
        var call = (BoundCall)((BoundReturn)reference.GenericMethodImports
            .Single().ProofFunction.Body.Statements.Single()).Value!;
        Assert.That(call.Arguments.Single(), Is.TypeOf<BoundMove>());
        Assert.That(call.ImportedGenericMethodCall!.Binding!.Expected
            .Formals[0].OwnershipDomain, Is.EqualTo(2));
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public int32 Value;
                public Owner(int32 value) { Value = value; }
            }
            int32 Main() {
                Owner source = new Owner(42);
                Owner result = Forward<Owner>(move source);
                return result.Value;
            }
            """), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(compilation.Program!);
    }

    [Test]
    public void RetainsExpectedCallerUnsafetyWhenProviderIsSafe()
    {
        var consumer = AddCallerUnsafeEffects(
            CreateImportArtifact(ExpectedSource));
        var reference = CreateReference(consumer,
            EmitProvider(ProviderSource));
        var returned = ((BoundReturn)reference.GenericMethodImports
            .Single().ProofFunction.Body.Statements.Single()).Value!;
        Assert.That(returned, Is.TypeOf<BoundUnsafeExpression>());
        var call = (BoundCall)((BoundUnsafeExpression)returned).Expression;
        var target = call.ImportedGenericMethodCall!;
        Assert.That(call.IsUnsafeAuthorized, Is.True);
        Assert.That(target.ProofFunction.RequiresUnsafeCaller, Is.True);
        Assert.That(target.Binding!.Provider.ProofFunction
            .RequiresUnsafeCaller, Is.False);
        var safeCall = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Forward<int32>(42); }"), reference);
        Assert.That(safeCall.HasErrors, Is.True);
        var unsafeCall = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return unsafe(Forward<int32>(42)); }"),
            reference);
        Assert.That(unsafeCall.HasErrors, Is.False,
            string.Join(Environment.NewLine, unsafeCall.Diagnostics));
        SemanticVerifier.Verify(unsafeCall.Program!);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PreservesTheExpectedNoReturnPromise(bool providerDoesNotReturn)
    {
        var consumer = CreateImportArtifact("""
            [DoesNotReturn] T Expected<T>(T value) {
                return Forward<T>(value);
            }
            [DoesNotReturn] public T Forward<T>(T value) {
                return Expected<T>(value);
            }
            """);
        var provider = EmitProvider(providerDoesNotReturn ? """
            [DoesNotReturn] public V Actual<V>(V value) {
                return Loop<V>(value);
            }
            [DoesNotReturn] X Loop<X>(X value) {
                return Actual<X>(value);
            }
            """ : "public V Actual<V>(V value) { return value; }");
        if (!providerDoesNotReturn)
        {
            _ = Assert.Throws<InvalidDataException>(() =>
                CreateReference(consumer, provider));
            return;
        }

        var reference = CreateReference(consumer, provider);
        var call = (BoundCall)((BoundReturn)reference.GenericMethodImports
            .Single().ProofFunction.Body.Statements.Single()).Value!;
        Assert.That(call.ImportedGenericMethodCall!.ProofFunction
            .DoesNotReturn, Is.True);
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Forward<int32>(42); }"), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(compilation.Program!);
    }

    [TestCase("arity")]
    [TestCase("default")]
    [TestCase("parameter")]
    [TestCase("ownership")]
    [TestCase("constraint")]
    [TestCase("writable")]
    [TestCase("unsafe")]
    public void RejectsIncompatibleSelectedProviderContracts(string change)
    {
        var consumer = CreateImportArtifact(change == "ownership" ? """
            T Expected<T>(T value) { return value; }
            public T Forward<T>(T value) { return Expected<T>(value); }
            """ : ExpectedSource);
        var providerSource = change switch {
            "arity" => "public V Actual<V>(V value) { return value; }",
            "default" => """
                public V Actual<V, W = int32>(V value) { return value; }
                """,
            "ownership" => """
                public V Actual<V>(V value) where V : unique {
                    return move value;
                }
                """,
            "constraint" => """
                public V Actual<V, W = V>(V value)
                    where V : [Blittable] { return value; }
                """,
            "writable" => """
                public V Actual<V, W = V>(V value) where V : write {
                    return value;
                }
                """,
            "unsafe" => """
                public unsafe V Actual<V, W = V>(V value) {
                    return value;
                }
                """,
            _ => ProviderSource,
        };
        var provider = EmitProvider(providerSource);
        if (change == "parameter")
        {
            var tables = ReadTables(provider);
            var methodRow = 0;
            for (var row = 1; row <= tables.Methods.Count; row++)
            {
                if (tables.Methods.GetName(row) == "Actual")
                {
                    methodRow = row;
                }
            }
            Assert.That(methodRow, Is.GreaterThan(0));
            var signature = tables.Methods.GetSignature(methodRow);
            var (formalFirst, _) = tables.Signatures.GetGenericRange(
                signature);
            var (parameterFirst, _) = tables.Signatures.GetParamRange(
                signature);
            var secondFormalType = 0;
            for (var row = 1; row <= tables.TypeSpecs.Count; row++)
            {
                var type = tables.TypeSpecs.GetRow(row);
                if (type[0] == 0x04
                    && ReadUInt32(type, 2) == (uint)(formalFirst + 1))
                {
                    secondFormalType = row;
                }
            }
            Assert.That(secondFormalType, Is.GreaterThan(0));
            var parameters = GetSection(provider, 0x0018);
            var bytes = parameters.Bytes.ToArray();
            Write(bytes, ((parameterFirst - 1) * 16) + 8,
                checked((uint)secondFormalType));
            provider = ReplaceSections(provider,
                parameters with { Bytes = bytes });
        }

        _ = Assert.Throws<InvalidDataException>(() =>
            CreateReference(consumer, provider));
    }

    [Test]
    public void RejectsProviderNarrowingTheRetainedNullableDomain()
    {
        var consumer = CreateImportArtifact("""
            T Expected<T, U = T>(T value)
                where T : null ? where U : null ? {
                return value;
            }
            public T Forward<T>(T value) where T : null ? {
                return Expected<T>(value);
            }
            """);
        _ = Assert.Throws<InvalidDataException>(() =>
            CreateReference(consumer, EmitProvider(ProviderSource)));
    }

    [TestCase("missing")]
    [TestCase("identity")]
    [TestCase("key")]
    [TestCase("target")]
    [TestCase("runtime")]
    [TestCase("duplicate")]
    public void RejectsNonExactProviderSelections(string change)
    {
        var consumer = CreateImportArtifact(ExpectedSource);
        var provider = EmitProvider(ProviderSource,
            change == "identity" ? "another.transport" : ProviderIdentity,
            change == "key" ? "another.acquire/v4" : ProviderKey,
            change == "target" ? 1u : 0u);
        if (change == "runtime")
        {
            var module = GetSection(provider, 0x0001);
            var bytes = module.Bytes.ToArray();
            Write(bytes, 12, 1);
            provider = ReplaceSections(provider, module with { Bytes = bytes });
        }

        var providerInput = new TrRmStandardArtifactInput(
            SchemaIdentity, provider);
        var providers = change switch {
            "missing" => Array.Empty<TrRmStandardArtifactInput>(),
            "duplicate" => new[] { providerInput, providerInput },
            _ => new[] { providerInput },
        };
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity, consumer),
                ConsumerIdentity, providers));
    }

    [TestCase("trrm/1-draft-layout-queries-1")]
    [TestCase("trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1")]
    public void RejectsOtherSchemaSelectionsWithoutFallback(string schema)
    {
        var consumer = CreateImportArtifact(ExpectedSource);
        var provider = EmitProvider(ProviderSource);
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity, consumer),
                ConsumerIdentity,
                [new TrRmStandardArtifactInput(schema, provider)]));
    }

    [Test]
    public void RequiresAnExecutableProviderBody()
    {
        var consumer = CreateImportArtifact(ExpectedSource);
        var provider = EmitProvider(ProviderSource);
        provider = RemoveMethodBody(provider, "Actual");
        _ = Assert.Throws<NotSupportedException>(() =>
            CreateReference(consumer, provider));
    }

    [Test]
    public void DirectExpectedStubCannotBypassItsImport()
    {
        var consumer = CreateImportArtifact(ExpectedSource,
            directExpectedReference: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateGenericMethodCallsDraftReference(
                consumer, ConsumerIdentity, "Forward"));
    }

    [Test]
    public void BindsTheCompleteSourceFreeProviderClosure()
    {
        var leaf = EmitProvider(
            "public V Actual<V>(V value) { return value; }",
            "generic.leaf", "leaf.acquire");
        const string forwardingSource = """
            T Expected<T>(T value) { return value; }
            public T Forward<T>(T value) { return Expected<T>(value); }
            """;
        var middle = CreateImportArtifact(forwardingSource,
            moduleIdentity: ProviderIdentity,
            providerIdentity: "generic.leaf",
            providerKey: "leaf.acquire");
        var consumer = CreateImportArtifact(forwardingSource,
            providerKey: "Forward");
        _ = Assert.Throws<InvalidDataException>(() =>
            CreateReference(consumer, middle));
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(SchemaIdentity, consumer),
            ConsumerIdentity, [
                new TrRmStandardArtifactInput(SchemaIdentity, middle),
                new TrRmStandardArtifactInput(SchemaIdentity, leaf),
            ]);
        var rootCall = (BoundCall)((BoundReturn)reference
            .GenericMethodImports.Single().ProofFunction.Body
            .Statements.Single()).Value!;
        var middleTarget = rootCall.ImportedGenericMethodCall!.Binding!
            .Provider;
        var middleCall = (BoundCall)((BoundReturn)middleTarget
            .ProofFunction.Body.Statements.Single()).Value!;
        Assert.That(middleCall.ImportedGenericMethodCall!.Binding!
            .Provider.Origin.ProviderIdentity, Is.EqualTo("generic.leaf"));
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Forward<int32>(42); }"), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(compilation.Program!);
    }

    [Test]
    public void ReemitsExpectedLabelsAcrossProviderReplacement()
    {
        var consumer = CreateImportArtifact(NamedArgumentExpectedSource);
        var firstProvider = EmitProvider(NamedArgumentProviderSource);
        var reference = CreateReference(consumer, firstProvider);
        var relayCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public T Relay<T>(T relayFirst, T relaySecond) {
                    return Forward<T>(callerValue: relayFirst,
                        otherValue: relaySecond);
                }
                """), reference, CreateOptions());
        Assert.That(relayCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, relayCompilation.Diagnostics));

        var relayProgram = relayCompilation.Program!;
        var relay = relayProgram.GenericMethodDefinitions.Single();
        var relayArtifact = TrRmExecutableCompiler.EmitStandard(relayProgram,
            "generic.reemitted", SchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new TrRmSourceGenericMethodExport(relayProgram,
                    relay.SourceInputOrdinal, relay.Span, "Relay"),
            ]);
        var replacementProvider = EmitProvider("""
            int32 Padding<Marker>(int32 value) { return value; }
            public V Actual<V, W = V>(
                W replacementOther, V replacementExpected) {
                return replacementExpected;
            }
            """);
        var rebound = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(SchemaIdentity, relayArtifact),
            "generic.reemitted", [
                new TrRmStandardArtifactInput(SchemaIdentity, consumer),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    replacementProvider),
            ]);

        var relayImport = rebound.GenericMethodImports.Single();
        var relayCall = (BoundCall)((BoundReturn)relayImport.ProofFunction.Body
            .Statements.Single()).Value!;
        var forwardBinding = relayCall.ImportedGenericMethodCall!.Binding!;
        var expectedCall = (BoundCall)((BoundReturn)forwardBinding.Provider
            .ProofFunction.Body.Statements.Single()).Value!;
        var expectedBinding = expectedCall.ImportedGenericMethodCall!.Binding!;

        Assert.That(expectedBinding.Expected.ProofFunction.Variables
                .Take(2).Select(static variable => variable.Name),
            Is.EqualTo(new[] { "otherValue", "expectedValue" }));
        Assert.That(expectedCall.ImportedGenericMethodCall!.ProofFunction
                .Variables.Take(2).Select(static variable => variable.Name),
            Is.EqualTo(new[] { "otherValue", "expectedValue" }));
        Assert.That(expectedBinding.Provider.ProofFunction.Variables
                .Take(2).Select(static variable => variable.Name),
            Is.EqualTo(new[] {
                "replacementOther", "replacementExpected",
            }));

        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                return Relay<int32>(relaySecond: 43, relayFirst: 42);
            }
            """), rebound, CreateOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var expectedSpecialization = program.Functions.Single(
            static function => function.ImportedGenericMethod is {
                Origin.IsMethodDefTarget: true,
            });
        Assert.That(expectedSpecialization.Variables.Take(2)
                .Select(static variable => variable.Name),
            Is.EqualTo(new[] { "otherValue", "expectedValue" }));
        SemanticVerifier.Verify(program);
    }

    [Test]
    public void ReemitsExpectedWriteRequirementAcrossProviderReplacement()
    {
        var consumer = CreateImportArtifact("""
            T Expected<T>(T expectedValue) where T : write {
                return move expectedValue;
            }
            public T Forward<T>(T callerValue) where T : write {
                return Expected<T>(move callerValue);
            }
            """);
        var initialProvider = EmitProvider("""
            public V Actual<V>(V providerValue) where V : write {
                return move providerValue;
            }
            """);
        var reference = CreateReference(consumer, initialProvider);
        var relayCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public T Relay<T>(T relayValue) where T : write {
                    return Forward<T>(move relayValue);
                }
                """), reference, CreateOptions());
        Assert.That(relayCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                relayCompilation.Diagnostics));
        var relayProgram = relayCompilation.Program!;
        SemanticVerifier.VerifyLibrary(relayProgram);
        var relayDefinition
            = relayProgram.GenericMethodDefinitions.Single();
        var relayArtifact = TrRmExecutableCompiler.EmitStandard(
            relayProgram, "generic.write.reemitted", SchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(relayProgram, relayDefinition.SourceInputOrdinal,
                    relayDefinition.Span, "generic.write/Relay"),
            ]);
        var relayTables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                new TrRmArtifactIndex(relayArtifact,
                    TrRmArtifactSchema
                        .GenericMethodCallsParameterLabelsDraft));
        Assert.That(relayTables.Features.TryGetFeature(27, out _), Is.True);

        var relaxedProvider = EmitProvider("""
            public V Actual<V>(V providerValue) {
                return move providerValue;
            }
            """);
        var rebound = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(SchemaIdentity, relayArtifact),
            "generic.write.reemitted", [
                new TrRmStandardArtifactInput(SchemaIdentity, consumer),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    relaxedProvider),
            ]);
        var relayImport = rebound.GenericMethodImports.Single();
        Assert.That(relayImport.Formals[0].RequiresWritable, Is.True);
        var relayCall = (BoundCall)((BoundReturn)relayImport.ProofFunction
            .Body.Statements.Single()).Value!;
        var forwardBinding = relayCall.ImportedGenericMethodCall!
            .Binding!;
        var expectedCall = (BoundCall)((BoundReturn)forwardBinding.Provider
            .ProofFunction.Body.Statements.Single()).Value!;
        var expectedBinding = expectedCall.ImportedGenericMethodCall!
            .Binding!;
        Assert.That(expectedBinding.Expected.Formals[0].RequiresWritable,
            Is.True);
        Assert.That(expectedBinding.Provider.Formals[0].RequiresWritable,
            Is.False);
    }

    [Test]
    public void RejectsProviderThatStrengthensAnExpectedGenericMethodWithWrite()
    {
        var consumer = CreateImportArtifact("""
            T Expected<T>(T expectedValue) {
                return move expectedValue;
            }
            public T Forward<T>(T callerValue) {
                return Expected<T>(move callerValue);
            }
            """);
        var writeProvider = EmitProvider("""
            public V Actual<V>(V providerValue) where V : write {
                return move providerValue;
            }
            """);

        _ = Assert.Throws<InvalidDataException>(() =>
            CreateReference(consumer, writeProvider));
    }

    [Test]
    public void RejectsProviderReplacementWithMalformedParameterLabels()
    {
        var consumer = CreateImportArtifact(NamedArgumentExpectedSource);
        var provider = EmitProvider(NamedArgumentProviderSource);
        var labels = GetSection(provider, 0x0066);
        var labelBytes = labels.Bytes.ToArray();
        Write(labelBytes, sizeof(uint), 1);
        provider = ReplaceSections(provider,
            labels with { Bytes = labelBytes });

        _ = Assert.Throws<InvalidDataException>(() =>
            CreateReference(consumer, provider));
    }

    [Test]
    public void ChecksRetainedImportsEvenWhenSelectedExportDoesNotUseThem()
    {
        var consumer = CreateImportArtifact(ExpectedSource + """

            public T Unrelated<T>(T value) { return value; }
            """);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateGenericMethodCallsDraftReference(
                consumer, ConsumerIdentity, "Unrelated"));
    }

    private static CompilationOptions CreateOptions()
        => new() {
            LayoutCoreRegistration = Terrum.CodeAnalysis.Tests.Semantics
                .LayoutCoreRegistrationTestData.Create(),
        };

    private static CompilationReference CreateReference(
        byte[] consumer, byte[] provider)
        => TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(SchemaIdentity, consumer),
            ConsumerIdentity,
            [new TrRmStandardArtifactInput(SchemaIdentity, provider)]);

    private static byte[] EmitProvider(string source,
        string moduleIdentity = ProviderIdentity,
        string exportKey = ProviderKey, uint targetRequirement = 0,
        string? alternateExportKey = null)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            CreateOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var method = program.GenericMethodDefinitions.Single(
            static definition => definition.IsPublic);
        var exports = new List<TrRmSourceGenericMethodExport> {
            new(program, method.SourceInputOrdinal, method.Span, exportKey),
        };
        if (alternateExportKey is not null)
        {
            exports.Add(new(program, method.SourceInputOrdinal,
                method.Span, alternateExportKey));
        }
        return TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, SchemaIdentity, targetRequirement,
            Array.Empty<TrRmSourceTypeExport>(), exports);
    }

    private static byte[] EmitProviderWithInterfaceConstraint()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace Example {
                public interface I<T> {
                    public T Echo(T value);
                }
                public T Actual<T>(T value) where T : I<T> {
                    return value;
                }
            }
            """), CreateOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var interfaceDefinition = program.Interfaces.Single();
        var interfaceMember = interfaceDefinition.Members.Single();
        var interfaceExport = new TrRmSourceTypeExport("Example", "I",
            "transport.interface/I") {
            GenericArity = interfaceDefinition.Formals.Length,
            InterfaceMethodExports = [
                new TrRmSourceInterfaceMethodExport(
                    interfaceMember.SourceInputOrdinal,
                    interfaceMember.SourceDeclarationSpan,
                    "transport.interface/I.Echo"),
            ],
        };
        var method = program.GenericMethodDefinitions.Single(
            static definition => definition.IsPublic);
        var methodExport = new TrRmSourceGenericMethodExport(program,
            method.SourceInputOrdinal, method.Span, ProviderKey);
        return TrRmExecutableCompiler.EmitStandard(program,
            ProviderIdentity, SchemaIdentity, 0,
            [interfaceExport], [methodExport]);
    }

    private static byte[] CreateImportArtifact(string source,
        bool directExpectedReference = false,
        string moduleIdentity = ConsumerIdentity,
        string providerIdentity = ProviderIdentity,
        string providerKey = ProviderKey,
        string? secondImportKey = null,
        string? secondProviderIdentity = null,
        IReadOnlyList<TrRmSourceTypeExport>? typeExports = null)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            CreateOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var mappings = program.GenericMethodDefinitions
            .Where(static definition => definition.IsPublic)
            .Select(definition => new TrRmSourceGenericMethodExport(
                program, definition.SourceInputOrdinal, definition.Span,
                definition.Name)).ToArray();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, SchemaIdentity, 0,
            typeExports ?? Array.Empty<TrRmSourceTypeExport>(), mappings);
        artifact = RemoveMethodBody(artifact, "Expected");
        var tables = ReadTables(artifact);
        var strings = new List<(string Text, int Encoding)>();
        for (var row = 1; row <= tables.Strings.Count; row++)
        {
            strings.Add((tables.Strings.DecodeNamespace(row), 1));
        }
        var expectedMethodRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            if (StringComparer.Ordinal.Equals(name, "Expected")
                || name.EndsWith(".Expected", StringComparison.Ordinal))
            {
                if (expectedMethodRow != 0)
                {
                    throw new InvalidDataException(
                        "The expected generic method name is ambiguous.");
                }
                expectedMethodRow = row;
            }
        }
        Assert.That(expectedMethodRow, Is.GreaterThan(0));
        var expectedMethodName = tables.Methods.GetName(expectedMethodRow);
        var expectedDefinition = program.GenericMethodDefinitions.Single(
            definition => definition.Name == expectedMethodName);
        var expectedFunction = expectedDefinition.ProofProgram.Functions[
            expectedDefinition.FunctionIndex];
        var existingLabels = GetSection(artifact, 0x0066);
        var existingLabelCount
            = checked((int)existingLabels.RecordCount);
        var labelBytes = new byte[checked(
            (existingLabelCount + expectedFunction.ParameterCount) * 12)];
        existingLabels.Bytes.Span.CopyTo(labelBytes);
        for (var ordinal = 0;
            ordinal < expectedFunction.ParameterCount; ordinal++)
        {
            var nameStringRow = strings.Count + 1;
            strings.Add((expectedFunction.Variables[ordinal].Name, 1));
            var rowOffset = checked((existingLabelCount + ordinal) * 12);
            Write(labelBytes, rowOffset,
                checked((uint)expectedMethodRow));
            Write(labelBytes, rowOffset + 4, checked((uint)ordinal));
            Write(labelBytes, rowOffset + 8,
                checked((uint)nameStringRow));
        }
        strings.Add((providerIdentity, 1));
        var providerIdentityRow = strings.Count;
        strings.Add((providerKey, 1));
        var providerKeyRow = strings.Count;
        var secondKeyRow = 0;
        var secondIdentityRow = providerIdentityRow;
        if (secondImportKey is not null)
        {
            strings.Add((secondImportKey, 1));
            secondKeyRow = strings.Count;
            if (secondProviderIdentity is not null)
            {
                strings.Add((secondProviderIdentity, 1));
                secondIdentityRow = strings.Count;
            }
        }

        var importCount = secondImportKey is null ? 1u : 2u;
        var references = new byte[checked((int)importCount * 12)];
        Write(references, 0, checked((uint)providerIdentityRow));
        var imports = new byte[references.Length];
        Write(imports, 0, 1);
        Write(imports, 4, checked((uint)providerKeyRow));
        Write(imports, 8, 0x1600_0000u
            | checked((uint)expectedMethodRow));
        if (secondImportKey is not null)
        {
            Write(references, 12, checked((uint)secondIdentityRow));
            Write(imports, 12, 2);
            Write(imports, 16, checked((uint)secondKeyRow));
            Write(imports, 20, 0x1600_0000u
                | checked((uint)expectedMethodRow));
        }
        var bodies = new List<ReadOnlyMemory<byte>>();
        for (var row = 1; row <= tables.Bodies.Count; row++)
        {
            var bytes = tables.Bodies.GetRow(row).ToArray();
            if (bytes[0] == 0x01)
            {
                var body = new TrRmRegisterBodyReader(bytes,
                    schema: TrRmArtifactSchema.GenericMethodCallsDraft);
                var methodMap = body.MethodMap.ToArray();
                Assert.That(methodMap, Has.Length.EqualTo(4));
                Write(methodMap, 0, directExpectedReference
                    ? 0x1600_0000u | checked((uint)expectedMethodRow)
                    : 0x1C00_0000u | importCount);
                var blocks = new List<TrRmRegisterBodyBlock>();
                for (var blockIndex = 0; blockIndex < body.BlockCount;
                    blockIndex++)
                {
                    var block = body.GetBlock(blockIndex);
                    blocks.Add(new TrRmRegisterBodyBlock(
                        ReadUInt32(block, 0), ReadUInt32(block, 4),
                        BinaryPrimitives.ReadUInt16LittleEndian(block[8..])));
                }
                var records = new List<ReadOnlyMemory<byte>>();
                for (var record = 0; record < body.RecordCount; record++)
                {
                    records.Add(body.GetRecord(record).ToArray());
                }
                bytes = TrRmRegisterBodyWriter.Encode(
                    body.HomeMap.ToArray(), body.TypeMap.ToArray(),
                    methodMap, body.FieldMap.ToArray(),
                    body.ConstantMap.ToArray(), blocks, records,
                    body.Code.ToArray(), tables.Contracts.Count,
                    TrRmArtifactSchema.GenericMethodCallsDraft);
            }
            bodies.Add(bytes);
        }

        return ReplaceSections(artifact,
            new TrRmSection(0x0002, 0, checked((uint)strings.Count),
                TrRmStringTableWriter.EncodeTextRows(strings)),
            new TrRmSection(0x0010, 0, importCount, references),
            new TrRmSection(0x001C, 0, importCount, imports),
            new TrRmSection(0x0020, 0, checked((uint)bodies.Count),
                TrRmVariableTableWriter.EncodeBodies(bodies)),
            new TrRmSection(0x0066, 0,
                checked((uint)(existingLabelCount
                    + expectedFunction.ParameterCount)), labelBytes));
    }

    private static byte[] AddUnrelatedLayoutImports(
        byte[] artifact, bool includeField)
    {
        var tables = ReadTables(artifact);
        var names = new List<(string Text, int Encoding)>();
        for (var row = 1; row <= tables.Strings.Count; row++)
        {
            names.Add((tables.Strings.DecodeNamespace(row), 1));
        }
        names.Add(("unused.layout-provider", 1));
        var identityRow = names.Count;
        names.Add(("Unrelated", 1));
        var typeNameRow = names.Count;
        names.Add(("Value", 1));
        var fieldNameRow = names.Count;
        names.Add(("unused.type", 1));
        var typeKeyRow = names.Count;
        names.Add(("unused.field", 1));
        var fieldKeyRow = names.Count;
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(names);
        var strings = new TrRmStringTableReader(stringBytes, names.Count);
        var module = GetSection(artifact, 0x0001);
        var moduleBytes = module.Bytes.ToArray();
        Write(moduleBytes, 28, 3);
        var (sourceFirst, sourceEnd) = tables.Modules.GetSourceFileRange(1);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            2, 1, tables.Methods.Count,             sourceEnd - sourceFirst, 3);
        var fieldCount = includeField ? 1 : 0;
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(typeNameRow, typeNameRow, 6,
                includeField ? 1 : 0, fieldCount, 0, 0, 1, 1, 0, 0),
        ], strings, modules, fieldCount, tables.GenericParams.Count, 1,
            tables.Contracts.Count);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings,
            modules, fieldCount, tables.GenericParams.Count, 1,
            tables.Contracts.Count);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
        ], types, new TrRmTargetProfileTableReader([], 0), 0);
        var referenceBytes = new byte[24];
        GetSection(artifact, 0x0010).Bytes.Span.CopyTo(referenceBytes);
        Write(referenceBytes, 12, checked((uint)identityRow));
        var importCount = includeField ? 3u : 2u;
        var importBytes = new byte[checked((int)importCount * 12)];
        GetSection(artifact, 0x001C).Bytes.Span.CopyTo(importBytes);
        Write(importBytes, 12, 2);
        Write(importBytes, 16, checked((uint)typeKeyRow));
        Write(importBytes, 20, 0x1300_0001);
        var replacements = new List<TrRmSection> {
            module with { Bytes = moduleBytes },
            new(0x0002, 0, checked((uint)names.Count), stringBytes),
            new(0x0004, 0, 3, TrRmFeatureTableWriter.Encode([
                new TrRmFeatureRow(13, 1), new TrRmFeatureRow(23, 1),
                new TrRmFeatureRow(26, 1),
            ], TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft)),
            new(0x0010, 0, 2, referenceBytes),
            new(0x0013, 0, 1, typeBytes),
            new(0x0015, 0, 1, layoutBytes),
        };
        if (includeField)
        {
            var scalarTypeSpec = 0;
            for (var row = 1; row <= tables.TypeSpecs.Count; row++)
            {
                if (tables.TypeSpecs.GetRow(row) is [0x01, 0, 8])
                {
                    scalarTypeSpec = row;
                }
            }
            Assert.That(scalarTypeSpec, Is.GreaterThan(0));
            var fields = TrRmFieldDefTableWriter.Encode([
                new TrRmFieldDefRow(1, fieldNameRow,
                    checked((uint)scalarTypeSpec), 10),
            ], strings, types, tables.TypeSpecs);
            replacements.Add(new(0x0014, 0, 1, fields));
            Write(importBytes, 24, 2);
            Write(importBytes, 28, checked((uint)fieldKeyRow));
            Write(importBytes, 32, 0x1400_0001);
        }
        replacements.Add(new(0x001C, 0, importCount, importBytes));
        return ReplaceSections(artifact, [.. replacements]);
    }

    private static byte[] RemoveMethodBody(byte[] artifact, string methodName)
    {
        var tables = ReadTables(artifact);
        var methodRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            if (StringComparer.Ordinal.Equals(name, methodName)
                || name.EndsWith("." + methodName,
                    StringComparison.Ordinal))
            {
                methodRow = row;
                break;
            }
        }
        Assert.That(methodRow, Is.GreaterThan(0));
        var removedBody = tables.Methods.GetBodyRow(methodRow);
        var methods = GetSection(artifact, 0x0016);
        var methodBytes = methods.Bytes.ToArray();
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var bodyRow = tables.Methods.GetBodyRow(row);
            Write(methodBytes, ((row - 1) * 28) + 24,
                row == methodRow ? 0
                    : checked((uint)(bodyRow > removedBody
                        ? bodyRow - 1 : bodyRow)));
        }
        var bodies = new List<ReadOnlyMemory<byte>>();
        for (var row = 1; row <= tables.Bodies.Count; row++)
        {
            if (row != removedBody)
            {
                bodies.Add(tables.Bodies.GetRow(row).ToArray());
            }
        }
        return ReplaceSections(artifact,
            methods with { Bytes = methodBytes },
            new TrRmSection(0x0020, 0, checked((uint)bodies.Count),
                TrRmVariableTableWriter.EncodeBodies(bodies)));
    }

    private static byte[] AddCallerUnsafeEffects(byte[] artifact)
    {
        var tables = ReadTables(artifact);
        Assert.That(tables.Contracts.Count, Is.Zero);
        var methods = GetSection(artifact, 0x0016);
        var methodBytes = methods.Bytes.ToArray();
        var contracts = new List<ReadOnlyMemory<byte>>();
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var clause = new byte[12];
            Write(clause, 0, 0x1600_0000u | checked((uint)row));
            BinaryPrimitives.WriteUInt16LittleEndian(clause.AsSpan(4), 1);
            Write(clause, 8, 4);
            contracts.Add(clause);
            Write(methodBytes, ((row - 1) * 28) + 16,
                checked((uint)contracts.Count));
            Write(methodBytes, ((row - 1) * 28) + 20, 1);
        }
        return ReplaceSections(artifact, methods with { Bytes = methodBytes },
            new TrRmSection(0x001B, 0, checked((uint)contracts.Count),
                TrRmVariableTableWriter.EncodeBlobs(contracts)));
    }

    private static TrRmStructuralMetadataValidator.ValidatedExecutionTables
        ReadTables(byte[] artifact)
        => TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForGenericMethodCallsParameterLabelsDraft(
                    artifact));

    private static TrRmSection GetSection(byte[] artifact, ushort kind)
    {
        var index = TrRmArtifactIndex
            .ForGenericMethodCallsParameterLabelsDraft(artifact);
        Assert.That(index.TryGetSection(kind, out var bytes, out var count),
            Is.True);
        return new TrRmSection(kind, 0, checked((uint)count), bytes.ToArray());
    }

    private static byte[] ReplaceSections(byte[] artifact,
        params TrRmSection[] replacements)
    {
        var index = TrRmArtifactIndex
            .ForGenericMethodCallsParameterLabelsDraft(artifact);
        var sections = new SortedDictionary<ushort, TrRmSection>();
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var count);
            sections.Add(kind, new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }
        foreach (var replacement in replacements)
        {
            sections[replacement.Kind] = replacement;
        }
        return TrRmArtifactWriter.Encode([.. sections.Values],
            TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);

    private static void Write(Span<byte> bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], value);
}
