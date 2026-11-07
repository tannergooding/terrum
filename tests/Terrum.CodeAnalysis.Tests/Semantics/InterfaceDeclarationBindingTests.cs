// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class InterfaceDeclarationBindingTests
{
    [Test]
    public void BindsGenericFormalsDefaultsAndBaseInstances()
    {
        const string source = """
            public interface IBase<T> { }
            public interface IValue<this T, const int32 Size = 2, U = int32>
                : IBase<T> where T : [Copy] { }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [Test]
    public void BindsExistentialInterfaceTypeArgumentSlots()
    {
        const string source = """
            public interface IBox<T, U> { }
            public interface IView {
                IBox<int32, _> HideSecond();
                IBox<_, int32> HideFirst();
                IBox<_, _> HideBoth();
                IBox<IBox<_, int32>, _> HideNestedSlots();
            }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var interfaces = compilation.Program!.Interfaces;
        var view = interfaces.Single(static item =>
            item.QualifiedName == "IView");
        BoundInterfaceType ResultType(string name)
        {
            return view.Members.Single(member => member.Name == name)
                .ResultType!;
        }

        Assert.That(ResultType("HideSecond").TypeArguments[0].Kind,
            Is.EqualTo(BoundInterfaceTypeArgumentKind.Type));
        Assert.That(ResultType("HideSecond").TypeArguments[1].Kind,
            Is.EqualTo(BoundInterfaceTypeArgumentKind.ExistentialType));
        Assert.That(ResultType("HideFirst").TypeArguments[0].Kind,
            Is.EqualTo(BoundInterfaceTypeArgumentKind.ExistentialType));
        Assert.That(ResultType("HideBoth").TypeArguments.Select(
            static argument => argument.Kind),
            Is.All.EqualTo(BoundInterfaceTypeArgumentKind.ExistentialType));
        Assert.That(BoundInterfaceContract.AreEquivalent(
            ResultType("HideSecond"), ResultType("HideFirst")), Is.False);
        Assert.That(ResultType("HideNestedSlots").TypeArguments[0].Type!
            .TypeArguments[0].Kind,
            Is.EqualTo(BoundInterfaceTypeArgumentKind.ExistentialType));
        Assert.That(ResultType("HideNestedSlots").TypeArguments[1].Kind,
            Is.EqualTo(BoundInterfaceTypeArgumentKind.ExistentialType));

        var rewritten = interfaces.Select(@interface =>
            BoundTreeTraversal.RewriteTypes(@interface,
                static type => type == PrimitiveType.Int32
                    ? PrimitiveType.Int64 : type)).ToArray();
        var rewrittenView = rewritten.Single(static item =>
            item.QualifiedName == "IView");
        Assert.That(rewrittenView.Members.Single(static member =>
                member.Name == "HideSecond").ResultType!
                .TypeArguments[1].Kind,
            Is.EqualTo(BoundInterfaceTypeArgumentKind.ExistentialType));
        Assert.That(BoundInterfaceContract.IsValidTable([.. rewritten],
            static _ => true), Is.True);
    }

    [Test]
    public void RejectsExistentialInterfaceArgumentForConstantFormal()
    {
        const string source = """
            public interface ICount<const int32 Size> { }
            public interface IView { ICount<_> Read(); }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True);
    }

    [Test]
    public void BindsLiteralAndForwardedConstantArgumentsOnInterfaceBases()
    {
        const string source = """
            public interface ICount<this T, const int32 Size = 2> { }
            public interface IBase<T = int32> { }
            public interface IWrapper<this T> : ICount<T, 4> { }
            public interface IDefault<this T> : ICount<T> { }
            public interface ITypeDefault : IBase { }
            public interface IForward<this T, const int32 Size> : ICount<T, Size> {
                ICount<T, Count> Convert<const int32 Count>(
                    ICount<T, Count> value);
            }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var wrapper = compilation.Program!.Interfaces.Single(static item =>
            item.QualifiedName == "IWrapper").Bases.Single();
        Assert.That(wrapper.TypeArguments[0].Kind,
            Is.EqualTo(BoundInterfaceTypeArgumentKind.Type));
        Assert.That(wrapper.TypeArguments[1].Kind,
            Is.EqualTo(BoundInterfaceTypeArgumentKind.Constant));
        Assert.That(wrapper.TypeArguments[1].ConstantValue!.Value, Is.EqualTo(4));

        var defaulted = compilation.Program.Interfaces.Single(static item =>
            item.QualifiedName == "IDefault").Bases.Single();
        Assert.That(defaulted.TypeArguments.Length, Is.EqualTo(2));
        Assert.That(defaulted.TypeArguments[1].ConstantValue!.Value, Is.EqualTo(2));

        var typeDefault = compilation.Program.Interfaces.Single(static item =>
            item.QualifiedName == "ITypeDefault").Bases.Single();
        Assert.That(typeDefault.TypeArguments.Single().Type!.PrimitiveType,
            Is.EqualTo(PrimitiveType.Int32));

        var forwarding = compilation.Program.Interfaces.Single(static item =>
            item.QualifiedName == "IForward").Bases.Single();
        Assert.That(forwarding.TypeArguments[1].ConstantValue, Is.Null);
        Assert.That(forwarding.TypeArguments[1].ConstantParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Interface));
        Assert.That(forwarding.TypeArguments[1].ConstantParameterOrdinal, Is.EqualTo(1));
        var method = compilation.Program.Interfaces.Single(static item =>
            item.QualifiedName == "IForward").Members.Single();
        Assert.That(method.ResultType!.TypeArguments[1].ConstantParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Member));
        Assert.That(method.ResultType.TypeArguments[1].ConstantParameterOrdinal,
            Is.EqualTo(0));
        Assert.That(method.Parameters.Single().Type.TypeArguments[1]
                .ConstantParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Member));
    }

    [Test]
    public void RejectsInterfaceConstantArgumentsFromAnIncompatibleDomain()
    {
        const string source = """
            public interface ICount<this T, const int32 Size> { }
            public interface IInvalid<this T, const bool Enabled>
                : ICount<T, Enabled> { }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True);
    }

    [Test]
    public void RewritesInterfaceConstantTypeAndValueTogether()
    {
        const string source = """
            public interface ICount<this T, const int32 Size = 2> { }
            public interface IWrapper<this T> : ICount<T, 4> { }
            int32 Main() => 0;
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var rewritten = compilation.Program!.Interfaces
            .Select(@interface => BoundTreeTraversal.RewriteTypes(
                @interface,
                static type => type == PrimitiveType.Int32
                    ? PrimitiveType.Int64 : type))
            .ToArray();
        var wrapper = rewritten.Single(static item =>
            item.QualifiedName == "IWrapper").Bases.Single();
        var constant = wrapper.TypeArguments[1];

        Assert.That(constant.ConstantType!.PrimitiveType, Is.EqualTo(PrimitiveType.Int64));
        Assert.That(constant.ConstantValue!.Type, Is.EqualTo(PrimitiveType.Int64));
        Assert.That(constant.ConstantValue.Value, Is.EqualTo(4));
        Assert.That(BoundInterfaceContract.IsValidTable([.. rewritten], static _ => true), Is.True);
    }

    [Test]
    public void BindsCallablePropertyIndexerAndAssociatedRequirements()
    {
        const string source = """
            public interface IValue<this T> {
                T Read<U>(U value) where U : [Copy] require true ensure true;
                T Value { get; set; }
                T this[int32 index] { get; }
                type Item = T;
                const int32 Limit = 4;
            }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [Test]
    public void BindsGenericDependencyAndOutputContracts()
    {
        const string source = """
            public interface IStorage<T> {
                [return: DependsOn(nameof(value))]
                [return: ForwardsBacking(nameof(value))]
                T Forward(T value);
                bool Initialize([DependsOn(nameof(source))] init ref<T> output, T source);
            }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [Test]
    public void RejectsUnknownInterfaceDependencySource()
    {
        const string source = """
            public interface IStorage<T> {
                [return: DependsOn(nameof(missing))]
                T Forward(T value);
            }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3023"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void BindsAccessorLocalDependencyContracts()
    {
        const string source = """
            public interface IStorage<T> {
                T Current { [return: DependsOn(this)] get; }
                T Value { get; [this: Captures(nameof(value))] set; }
            }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [Test]
    public void ComposesPartialInterfacesAndDistinctCallableOverloads()
    {
        const string source = """
            public partial interface IValues<T> {
                T Read(int32 index);
            }
            public partial interface IValues<T> {
                T Read(bool selector);
            }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [Test]
    public void RejectsDuplicateInterfaceMemberIdentity()
    {
        const string source = """
            public interface IValues<T> {
                T Read(int32 index);
                bool Read(int32 position);
            }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3003"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase("public interface I<T> : Missing<T> { }")]
    [TestCase("public interface IBase<T> { } public interface I<T> : IBase { }")]
    public void RejectsUnresolvedOrWrongArityBaseInterface(string declarations)
    {
        var source = declarations + " int32 Main() => 0;";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code is "TRM3005" or "TRM3003"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RejectsCyclicInterfaceInheritance()
    {
        const string source = """
            public interface ILeft<T> : IRight<T> { }
            public interface IRight<T> : ILeft<T> { }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RetainsNativeSignatureTypesInInterfaceMembers()
    {
        const string source = """
            public interface ICallback {
                void Invoke(fnptr<int32> callback);
            }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }
}
