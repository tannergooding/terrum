// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class DependencySourceRecipeTests
{
    [Test]
    public void BindsSourceRecipesToExactTargetsAndOrdinals()
    {
        string[] declarations = [
            "[return: DependsOn(this)] internal int32 Identity() { return Value; }",
            "[return: DependsOn(nameof(@this))] internal int32 FromFormal(Receiver @this) { return @this.Value; }",
            "[return: DependsOn(\"this\")] internal int32 FromLiteralFormal(Receiver @this) { return @this.Value; }",
            "[this: Captures(nameof(source))] internal void CaptureReceiver(Holder source) { }",
            "[param(holder): Captures(\"source\")] internal void CaptureHolder(Holder holder, Holder source) { }",
            "[param(holder): Captures(nameof(source))] [param(holder): ForwardsBacking(nameof(source))] internal void ForwardCapture(Holder holder, Holder source) { }",
            "[param(output): Captures(nameof(source))] internal bool CaptureOutput(init ref<write Holder> output, Holder source) { require Holder.TryCreate(out var created); *output = created; return true; }",
            "[Captures()] internal void CaptureNothing() { }",
            "[param(output): DependsOn(nameof(source))] internal bool Initialize(init ref<int32> output, int32 source) { output = source; return true; }",
            "[param(output): DependsOn(nameof(source))] [param(output): ForwardsBacking(nameof(source))] internal bool ForwardOutput(init ref<write Holder> output, write Holder source) { *output = source; return true; }",
            "internal bool InitializeInline([DependsOn(nameof(source))] init ref<int32> output, int32 source) { output = source; return true; }",
            "[return: DependsOn()] internal int32 Independent() { return 42; }",
        ];
        var prefix = """
            mut class Holder {
                public int32 Value;
                public Holder() { Value = 0; }
            }
            class Receiver {
                public int32 Value;
                public Receiver() { Value = 0; }
            """;
        BoundProgram program = null!;
        for (var count = 1; count <= declarations.Length; count++)
        {
            program = Compile(prefix + string.Join(Environment.NewLine,
                declarations.Take(count)) + " }");
        }

        var identity = Find(program, "Receiver.Identity");
        var identityDependency = GetContract(identity, BoundDependencyRelation.DependsOn);
        Assert.That(identityDependency.Destination,
            Is.EqualTo(BoundDependencyDestination.Result));
        Assert.That(identityDependency.Sources, Has.Length.EqualTo(1));
        Assert.That(identityDependency.Sources[0].Kind,
            Is.EqualTo(BoundDependencySourceKind.Receiver));
        Assert.That(identityDependency.Sources[0].FormalOrdinal, Is.Null);

        var formalDependency = GetContract(Find(program, "Receiver.FromFormal"),
            BoundDependencyRelation.DependsOn);
        Assert.That(formalDependency.Sources[0].Kind,
            Is.EqualTo(BoundDependencySourceKind.Formal));
        Assert.That(formalDependency.Sources[0].FormalOrdinal, Is.EqualTo(0));
        var literalFormalDependency = GetContract(Find(program, "Receiver.FromLiteralFormal"),
            BoundDependencyRelation.DependsOn);
        Assert.That(literalFormalDependency.Sources[0].Kind,
            Is.EqualTo(BoundDependencySourceKind.Formal));
        Assert.That(literalFormalDependency.Sources[0].FormalOrdinal, Is.EqualTo(0));

        var receiverCapture = GetContract(Find(program, "Receiver.CaptureReceiver"),
            BoundDependencyRelation.Captures);
        Assert.That(receiverCapture.Destination,
            Is.EqualTo(BoundDependencyDestination.Receiver));
        Assert.That(receiverCapture.Sources[0].FormalOrdinal, Is.EqualTo(0));

        var holderCapture = GetContract(Find(program, "Receiver.CaptureHolder"),
            BoundDependencyRelation.Captures);
        Assert.That(holderCapture.Destination,
            Is.EqualTo(BoundDependencyDestination.RetainedFormal));
        Assert.That(holderCapture.DestinationOrdinal, Is.EqualTo(0));
        Assert.That(holderCapture.Sources[0].FormalOrdinal, Is.EqualTo(1));

        var forwardingCapture = Find(program, "Receiver.ForwardCapture");
        var captured = GetContract(forwardingCapture, BoundDependencyRelation.Captures);
        var capturedForwarding = GetContract(forwardingCapture,
            BoundDependencyRelation.ForwardsBacking);
        Assert.That(captured.Destination,
            Is.EqualTo(BoundDependencyDestination.RetainedFormal));
        Assert.That(captured.DestinationOrdinal, Is.EqualTo(0));
        Assert.That(captured.Sources[0].FormalOrdinal, Is.EqualTo(1));
        Assert.That(capturedForwarding.Destination,
            Is.EqualTo(BoundDependencyDestination.RetainedFormal));
        Assert.That(capturedForwarding.DestinationOrdinal, Is.EqualTo(0));
        Assert.That(capturedForwarding.Sources[0].FormalOrdinal, Is.EqualTo(1));

        var outputCapture = GetContract(Find(program, "Receiver.CaptureOutput"),
            BoundDependencyRelation.Captures);
        Assert.That(outputCapture.Destination,
            Is.EqualTo(BoundDependencyDestination.InitializedOutput));
        Assert.That(outputCapture.DestinationOrdinal, Is.EqualTo(0));
        Assert.That(outputCapture.Sources[0].FormalOrdinal, Is.EqualTo(1));

        var emptyCapture = GetContract(Find(program, "Receiver.CaptureNothing"),
            BoundDependencyRelation.Captures);
        Assert.That(emptyCapture.Destination,
            Is.EqualTo(BoundDependencyDestination.Receiver));
        Assert.That(emptyCapture.Sources, Is.Empty);

        var initialize = Find(program, "Receiver.Initialize");
        var outputDependency = GetContract(initialize, BoundDependencyRelation.DependsOn);
        Assert.That(outputDependency.Destination,
            Is.EqualTo(BoundDependencyDestination.InitializedOutput));
        Assert.That(outputDependency.DestinationOrdinal, Is.EqualTo(0));
        Assert.That(outputDependency.Sources[0].FormalOrdinal, Is.EqualTo(1));
        var outputForwarding = GetContract(Find(program, "Receiver.ForwardOutput"),
            BoundDependencyRelation.ForwardsBacking);
        Assert.That(outputForwarding.Destination,
            Is.EqualTo(BoundDependencyDestination.InitializedOutput));
        Assert.That(outputForwarding.DestinationOrdinal, Is.EqualTo(0));
        Assert.That(outputForwarding.Sources[0].FormalOrdinal, Is.EqualTo(1));
        var inlineOutput = GetContract(Find(program, "Receiver.InitializeInline"),
            BoundDependencyRelation.DependsOn);
        Assert.That(inlineOutput.Destination,
            Is.EqualTo(BoundDependencyDestination.InitializedOutput));
        Assert.That(inlineOutput.Sources[0].FormalOrdinal, Is.EqualTo(1));

        var emptyDependency = GetContract(Find(program, "Receiver.Independent"),
            BoundDependencyRelation.DependsOn);
        Assert.That(emptyDependency.Destination,
            Is.EqualTo(BoundDependencyDestination.Result));
        Assert.That(emptyDependency.Sources, Is.Empty);
    }

    [TestCase("[return: DependsOn(this)] internal int32 Identity() { return Value; }")]
    [TestCase("[return: DependsOn(nameof(@this))] internal int32 FromFormal(Receiver @this) { return @this.Value; }")]
    [TestCase("[this: Captures(nameof(source))] internal void CaptureReceiver(Holder source) { }")]
    [TestCase("[param(holder): Captures(\"source\")] internal void CaptureHolder(Holder holder, Holder source) { }")]
    [TestCase("[param(holder): Captures(nameof(source))] [param(holder): ForwardsBacking(nameof(source))] internal void ForwardCapture(Holder holder, Holder source) { }")]
    [TestCase("[param(output): Captures(nameof(source))] internal bool CaptureOutput(init ref<write Holder> output, Holder source) { require Holder.TryCreate(out var created); *output = created; return true; }")]
    [TestCase("[Captures()] internal void CaptureNothing() { }")]
    [TestCase("[param(output): DependsOn(nameof(source))] internal bool Initialize(init ref<int32> output, int32 source) { output = source; return true; }")]
    [TestCase("[param(output): DependsOn(nameof(source))] [param(output): ForwardsBacking(nameof(source))] internal bool ForwardOutput(init ref<write Holder> output, write Holder source) { *output = source; return true; }")]
    [TestCase("internal bool InitializeInline([DependsOn(nameof(source))] init ref<int32> output, int32 source) { output = source; return true; }")]
    [TestCase("[return: DependsOn()] internal int32 Independent() { return 42; }")]
    [TestCase("internal int32 Current { [DependsOn(this)] get { return Value; } }")]
    public void EachValidRecipeBindsIndependently(string declaration)
    {
        var compilation = CompileLibrary(declaration);
        Assert.That(compilation.HasErrors, Is.False,
            declaration + Environment.NewLine
                + string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase("[return: DependsOn(nameof(missing))]")]
    [TestCase("[return: DependsOn(nameof(value), \"value\")]")]
    [TestCase("[return: DependsOn(other(value))]")]
    [TestCase("[return: ForwardsBacking()]")]
    [TestCase("[param(value): DependsOn(nameof(value))]")]
    [TestCase("[this: DependsOn()]")]
    [TestCase("[method: Captures(this)]")]
    [TestCase("[return: DependsOn(this)] internal static int32 Select() { return 42; }")]
    [TestCase("[param(missing): Captures()] internal void Store(Holder value) { }")]
    [TestCase("[return: DependsOn(nameof(value))] [return: DependsOn(\"value\")]")]
    [TestCase("internal bool InitializeInline([ForwardsBacking()] init ref<int32> output) { output = 42; return true; }")]
    public void RejectsUnknownDuplicateOrIncompatibleRecipes(string attribute)
    {
        var compilation = CompileLibrary(attribute
            + " internal Holder Select(Holder value) { return value; }");

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void RejectsCaptureOnAnIneligibleRetainedFormal()
    {
        var compilation = CompileLibrary(
            "[param(scalar): Captures(nameof(holder))] "
            + "internal void Store(int32 scalar, Holder holder) { }");

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    [TestCase(
        "[return: DependsOn(nameof(first))] [return: ForwardsBacking(nameof(second))] "
        + "internal int32 Select(Holder first, Holder second) { return first.Value; }")]
    [TestCase(
        "[return: DependsOn()] [return: ForwardsBacking(nameof(value))] "
        + "internal int32 Select(Holder value) { return value.Value; }")]
    [TestCase(
        "[param(holder): ForwardsBacking(nameof(source))] "
        + "internal void Store(Holder holder, Holder source) { }")]
    public void RejectsForwardingSourcesOutsideTheDeclaredRelationship(string declaration)
    {
        var compilation = CompileLibrary(declaration);

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3023"), Is.True);
    }

    [Test]
    public void ForwardsBackingMayRefineAStrictSubsetOfDependencySources()
    {
        var compilation = CompileLibrary(
            "[return: DependsOn(nameof(first), nameof(second))] "
            + "[return: ForwardsBacking(nameof(first))] "
            + "internal int32 Select(Holder first, Holder second) { return first.Value; }");

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = Find(compilation.Program!, "Receiver.Select");
        var dependency = GetContract(function, BoundDependencyRelation.DependsOn);
        var forwarding = GetContract(function, BoundDependencyRelation.ForwardsBacking);

        Assert.That(dependency.Sources, Has.Length.EqualTo(2));
        Assert.That(dependency.Sources[0].FormalOrdinal, Is.EqualTo(0));
        Assert.That(dependency.Sources[1].FormalOrdinal, Is.EqualTo(1));
        Assert.That(forwarding.Sources, Has.Length.EqualTo(1));
        Assert.That(forwarding.Sources[0].FormalOrdinal, Is.EqualTo(0));
    }

    [Test]
    public void VerifierRejectsForgedForwardingSource()
    {
        var compilation = CompileLibrary(
            "[return: DependsOn(nameof(first))] "
            + "[return: ForwardsBacking(nameof(first))] "
            + "internal int32 Select(Holder first, Holder second) { return first.Value; }");
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var functionIndex = Array.FindIndex(program.Functions.ToArray(),
            static function => function.Name == "Receiver.Select");
        var function = program.Functions[functionIndex];
        var forwardingIndex = Array.FindIndex(function.DependencyContracts.ToArray(),
            static contract => contract.Relation == BoundDependencyRelation.ForwardsBacking);
        var forwarding = function.DependencyContracts[forwardingIndex] with {
            Sources = [new BoundDependencySource(BoundDependencySourceKind.Formal, 1)],
        };
        var forgedFunction = function with {
            DependencyContracts = function.DependencyContracts.SetItem(
                forwardingIndex, forwarding),
        };
        var forgedProgram = program with {
            Functions = program.Functions.SetItem(functionIndex, forgedFunction),
        };

        _ = Assert.Throws<ArgumentException>(
            () => SemanticVerifier.VerifyLibrary(forgedProgram))!;
    }

    [Test]
    public void EmptyResultDependencyBindsOnTopLevelCallable()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[return: DependsOn()] internal int32 Independent() { return 42; }"));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [Test]
    public void AccessorRecipeBindsToSelectedGetter()
    {
        var compilation = CompileLibrary(
            "internal int32 Current { [return: DependsOn(this)] get { return Value; } }");
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var getter = program.Functions.Single(function => function.PropertyName == "Current");
        var dependency = GetContract(getter, BoundDependencyRelation.DependsOn);

        Assert.That(dependency.Destination,
            Is.EqualTo(BoundDependencyDestination.Result));
        Assert.That(dependency.Sources[0].Kind,
            Is.EqualTo(BoundDependencySourceKind.Receiver));
    }

    private static Compilation CompileLibrary(string declaration)
    {
        var source = """
            mut class Holder {
                public int32 Value;
                public Holder() { Value = 0; }
            }
            """ + " class Receiver { public int32 Value; public Receiver() { Value = 0; } "
                + declaration + " }";
        return Compilation.CreateLibrary(SyntaxTree.Parse(source));
    }

    private static BoundProgram Compile(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
        return compilation.Program!;
    }

    private static BoundFunction Find(BoundProgram program, string name)
        => program.Functions.Single(function => function.Name == name);

    private static BoundDependencyContract GetContract(BoundFunction function,
        BoundDependencyRelation relation)
        => function.DependencyContracts.Single(contract => contract.Relation == relation);
}
