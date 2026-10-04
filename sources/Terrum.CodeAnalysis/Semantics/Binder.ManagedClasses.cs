// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private readonly List<BoundClass> _classes = [];
    private readonly Dictionary<string, PrimitiveType> _classNames = new(StringComparer.Ordinal);
    private readonly Dictionary<int, (SyntaxTree Tree, SyntaxNode Declaration)> _classDeclarations = [];

    private bool IsClass(PrimitiveType type) => ManagedClassTypes.IsClass(type)
        && ManagedClassTypes.ClassIndex(type) < _classes.Count;

    private void DeclareClassName(SyntaxTree tree, SyntaxNode declaration)
    {
        _usesManagedNode = true;
        var name = StructName(declaration);
        if (TryPrimitiveType(name.Text, out _))
        {
            Report("TRM3003", $"The primitive type '{name.Text}' cannot be declared as a class.", declaration.Span);
            return;
        }
        var qualified = Qualify(name.Name);
        if (_classes.Count >= SemanticVerifier.MaximumVariables
            || !_classNames.TryAdd(qualified, (PrimitiveType)(ManagedClassTypes.First + _classes.Count))
            || _structNames.ContainsKey(qualified)
            || qualified is NativeManagedNodeIntrinsics.TypeName or NativeManagedNodeIntrinsics.HeapName)
        {
            Report("TRM3003", $"Duplicate or reserved managed class '{qualified}'.", declaration.Span);
            return;
        }
        var index = _classes.Count;
        _classes.Add(new BoundClass(qualified, [], 0) {
            IsMutable = declaration.Children.OfType<SyntaxTokenElement>().Any(static child =>
                child.Token.Kind == Terrum.CodeAnalysis.Lexing.TokenKind.MutKeyword),
        });
        _classDeclarations.Add(index, (tree, declaration));
    }

    private enum DeclaredTypeResolution
    {
        Missing,
        Unique,
        Ambiguous,
        Invalid,
    }

    private DeclaredTypeResolution ResolveTypeName(string name, out PrimitiveType type) =>
        ResolveTypeName(name, null, default, out type);

    private DeclaredTypeResolution ResolveTypeName(string name, ImmutableArray<PrimitiveType>? typeArguments,
        TextSpan useSite, out PrimitiveType type)
    {
        if (typeArguments is not null && _genericProofSource is not null
            && FindAliasDefinition(name) is AliasDefinition localAlias)
        {
            return ResolveAlias(localAlias, typeArguments, useSite, out type);
        }
        if (_genericContext is GenericInstantiation generic
            && generic.Template.ParameterIndex(name) is var index && index >= 0)
        {
            type = generic.Types[index];
            return DeclaredTypeResolution.Unique;
        }
        if (_genericProofSource is Binder source)
        {
            var resolution = source.ResolveTypeName(name, typeArguments, useSite, out var sourceType);
            if (resolution is DeclaredTypeResolution.Ambiguous or DeclaredTypeResolution.Invalid)
            {
                type = default;
                return resolution;
            }
            if (resolution == DeclaredTypeResolution.Unique)
            {
                if (source.IsStruct(sourceType)
                    || (ManagedClassTypes.IsManaged(sourceType) && (int)sourceType >= ManagedClassTypes.First))
                {
                    if (HydrateProofType(sourceType) is not PrimitiveType mapped)
                    {
                        type = default;
                        return DeclaredTypeResolution.Invalid;
                    }
                    type = mapped;
                }
                else
                {
                    type = sourceType;
                }
                return DeclaredTypeResolution.Unique;
            }
        }
        var invalidAlias = false;
        var hasCandidate = false;
        var ambiguous = false;
        var candidate = default(PrimitiveType);
        if (name.Contains('.', StringComparison.Ordinal))
        {
            AddCandidate(name);
        }
        else
        {
            AddCandidate(name);
            AddCandidate(Qualify(name));
            var parent = _namespace;
            while (parent.Contains('.', StringComparison.Ordinal))
            {
                parent = parent[..parent.LastIndexOf('.', StringComparison.Ordinal)];
                AddCandidate(parent + "." + name);
            }
            foreach (var imported in _contexts[_tree].Usings)
            {
                AddCandidate(imported + "." + name);
            }
        }
        type = candidate;
        if (invalidAlias)
        {
            type = default;
            return DeclaredTypeResolution.Invalid;
        }
        if (!hasCandidate)
        {
            type = default;
            return DeclaredTypeResolution.Missing;
        }
        if (ambiguous)
        {
            type = default;
            return DeclaredTypeResolution.Ambiguous;
        }
        return DeclaredTypeResolution.Unique;

        void AddCandidate(string qualified)
        {
            if (typeArguments is null && _structNames.TryGetValue(qualified, out var structure))
            {
                AddTypeCandidate(structure);
            }
            if (typeArguments is null && _classNames.TryGetValue(qualified, out var @class))
            {
                AddTypeCandidate(@class);
            }
            if (_aliases.TryGetValue(qualified, out var alias))
            {
                var aliasResolution = ResolveAlias(alias, typeArguments, useSite, out var aliasType);
                if (aliasResolution == DeclaredTypeResolution.Unique)
                {
                    AddTypeCandidate(aliasType);
                }
                else if (aliasResolution == DeclaredTypeResolution.Invalid)
                {
                    invalidAlias = true;
                }
            }
        }

        void AddTypeCandidate(PrimitiveType candidateType)
        {
            if (!hasCandidate)
            {
                candidate = candidateType;
                hasCandidate = true;
            }
            else if (candidate != candidateType)
            {
                ambiguous = true;
            }
        }
    }

    private bool TryResolveClass(string name, out PrimitiveType type) =>
        ResolveTypeName(name, out type) == DeclaredTypeResolution.Unique && IsClass(type);

    private DeclaredTypeResolution ResolveStaticMember(SyntaxNode receiver, string member, out string name)
    {
        var receiverName = NameText(receiver);
        var resolution = ResolveTypeName(receiverName, out var type);
        name = resolution == DeclaredTypeResolution.Unique
            ? IsClass(type) ? _classes[ManagedClassTypes.ClassIndex(type)].Name + "." + member
                : IsStruct(type) ? _structs[(int)type - 100].Name + "." + member
                : _genericContext is GenericInstantiation generic
                    && generic.Template.ParameterIndex(receiverName) >= 0
                    ? TypeName(type) + "." + member
                : receiverName + "." + member
            : receiverName + "." + member;
        return resolution;
    }

    private PrimitiveType ResolveDeclaredType(string name)
    {
        if (TryPrimitiveType(name, out var primitive))
        {
            return primitive;
        }
        if (!name.Contains('.', StringComparison.Ordinal)
            && _structNames.TryGetValue(name, out var intrinsic) && (int)intrinsic < 100)
        {
            return intrinsic;
        }
        var qualified = name.Contains('.', StringComparison.Ordinal) ? name : Qualify(name);
        if (_classNames.TryGetValue(qualified, out var type)
            || _structNames.TryGetValue(qualified, out type))
        {
            return type;
        }
        Report("TRM3005", $"Declaring type '{name}' is missing or ambiguous.", _tree.Root.Span);
        return PrimitiveType.Int32;
    }

    private void DeclareClass(int index)
    {
        var (tree, declaration) = _classDeclarations[index];
        SetContext(tree);
        var name = _classes[index].Name;
        var publicClass = false;
        var mutableClass = false;
        foreach (var child in declaration.Children)
        {
            if (child is SyntaxNode {
                Kind: SyntaxKind.Attribute or SyntaxKind.ConformanceList
                or SyntaxKind.OperatorDeclaration
            })
            {
                Report("TRM3001", "Managed classes do not support attributes, inheritance, or operators.", child.Span);
            }
            if (child is SyntaxTokenElement token)
            {
                publicClass |= token.Token.Kind == Terrum.CodeAnalysis.Lexing.TokenKind.PublicKeyword;
                mutableClass |= token.Token.Kind == Terrum.CodeAnalysis.Lexing.TokenKind.MutKeyword;
                if (token.Token.Kind is Terrum.CodeAnalysis.Lexing.TokenKind.UniqueKeyword
                    or Terrum.CodeAnalysis.Lexing.TokenKind.StaticKeyword
                    or Terrum.CodeAnalysis.Lexing.TokenKind.PrivateKeyword
                    or Terrum.CodeAnalysis.Lexing.TokenKind.UnsafeKeyword)
                {
                    Report("TRM3001", "Classes support public/internal visibility and optional mutability.", child.Span);
                }
            }
        }
        var fields = ImmutableArray.CreateBuilder<BoundStructField>();
        var offset = 0;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in declaration.Children.OfType<SyntaxNode>())
        {
            if (child.Kind != SyntaxKind.FieldDeclaration)
            {
                continue;
            }
            if (child.Children[^2] is not SyntaxTokenElement fieldToken
                || fieldToken.Token.Kind != Terrum.CodeAnalysis.Lexing.TokenKind.Identifier)
            {
                Report("TRM3005", "Class fields must be initialized by an explicit constructor.", child.Span);
                continue;
            }
            var type = BindType((SyntaxNode)child.Children[^3], allowVoid: false);
            if (type is PrimitiveType.OwnedInt32Buffer or PrimitiveType.ReadonlyInt32BufferView
                or PrimitiveType.MutableInt32BufferView or PrimitiveType.TokenSpan || IsUnique(type))
            {
                Report("TRM3023", "Class fields require scalars, managed handles, or handle-free Copy values.", child.Span);
            }
            var width = ValueLayout.Size(type, _structs);
            var alignment = width == 0 ? 1 : ValueLayout.Alignment(type, _structs);
            offset = (offset + alignment - 1) & -alignment;
            if (width is < 1 or > ValueLayout.MaximumSize || offset + width > ManagedClassTypes.MaximumPayload
                || !names.Add(fieldToken.Token.Name))
            {
                Report("TRM3005", "Managed fields must have distinct names and fit the bounded payload.", child.Span);
                continue;
            }
            var isPublic = false;
            var isInternal = false;
            var mutable = false;
            var unsafeModifiers = 0;
            foreach (var modifier in child.Children)
            {
                if (modifier is SyntaxTokenElement token)
                {
                    isPublic |= token.Token.Kind == Terrum.CodeAnalysis.Lexing.TokenKind.PublicKeyword;
                    isInternal |= token.Token.Kind == Terrum.CodeAnalysis.Lexing.TokenKind.InternalKeyword;
                    mutable |= token.Token.Kind == Terrum.CodeAnalysis.Lexing.TokenKind.MutKeyword;
                    if (token.Token.Kind == Terrum.CodeAnalysis.Lexing.TokenKind.StaticKeyword)
                    {
                        Report("TRM3001", "Static managed fields are not supported.", modifier.Span);
                    }
                    else if (token.Token.Kind == Terrum.CodeAnalysis.Lexing.TokenKind.UnsafeKeyword)
                    {
                        unsafeModifiers++;
                    }
                }
            }
            if (unsafeModifiers > 1)
            {
                Report("TRM3001", "A field can declare only one unsafe modifier.", child.Span);
            }
            if (mutable && !mutableClass)
            {
                Report("TRM3008", "Mutable fields require a mut class declaration.", child.Span);
            }
            fields.Add(new BoundStructField(fieldToken.Token.Name, type, offset, width) {
                IsMutable = mutable,
                IsPrivate = !isPublic && !isInternal,
                IsInternal = isInternal,
                RequiresUnsafeCaller = unsafeModifiers != 0,
            });
            offset += width;
        }
        if (fields.Count == 0)
        {
            Report("TRM3005", "A managed class requires initialized instance fields.", declaration.Span);
        }
        if (fields.Count > ManagedClassTypes.MaximumFields)
        {
            Report("TRM3005", $"A managed class supports at most {ManagedClassTypes.MaximumFields} fields.", declaration.Span);
        }
        _classes[index] = new BoundClass(name, fields.ToImmutable(), (offset + 7) & ~7) {
            IsInternal = !publicClass,
            IsMutable = mutableClass,
        };
    }

    private void DeclareClassFactories()
    {
        foreach (var (index, (tree, declaration)) in _classDeclarations)
        {
            SetContext(tree);
            var type = (PrimitiveType)(ManagedClassTypes.First + index);
            if (_classes[index].IsMutable)
            {
                type = ManagedClassTypes.Writable(type);
            }
            var name = _classes[index].Name;
            if (!_functions.TryGetValue(name + ".#ctor", out var constructor))
            {
                Report("TRM3011", $"Class '{name}' requires an explicit constructor.", declaration.Span);
                continue;
            }
            var signature = _signatures[constructor];
            if (!signature.IsClassConstructor || signature.Parameters.Length >= SemanticVerifier.MaximumArguments
                || signature.Parameters.Any(parameter => parameter.IsOut
                    || !SemanticVerifier.IsClassArgumentType(parameter.Type, _structs)))
            {
                Report("TRM3011", "A class constructor takes at most 63 by-value scalar, managed-handle, or handle-free Copy parameters.",
                    declaration.Span);
                continue;
            }
            var factory = name + ".TryCreate";
            if (!_functions.TryAdd(factory, _signatures.Count))
            {
                Report("TRM3003", $"Compiler-owned factory '{factory}' collides with a declaration.", declaration.Span);
                continue;
            }
            var parameters = signature.Parameters.Add(new Parameter("result", type, declaration.Span, true));
            _signatures.Add(new Signature(factory, PrimitiveType.Bool, parameters, null, null, tree,
                name, false, signature.IsPrivate, !signature.IsPrivate, false, true, signature.IsNoThrow, false) {
                ClassConstructor = constructor,
                RequiresUnsafeCaller = signature.RequiresUnsafeCaller,
            });
        }
    }

    private BoundFunction CreateClassFactory(Signature signature)
    {
        var type = (PrimitiveType)(ManagedClassTypes.First
            + _classes.FindIndex(item => item.Name == signature.StructName));
        if (_classes[ManagedClassTypes.ClassIndex(type)].IsMutable)
        {
            type = ManagedClassTypes.Writable(type);
        }
        var variables = signature.Parameters.Select(parameter =>
            new BoundVariable(parameter.Name, parameter.Type, false, parameter.Span) { IsOut = parameter.IsOut })
            .Append(new BoundVariable("$provisional", type, false, signature.Tree.Root.Span)).ToImmutableArray();
        return new BoundFunction(signature.Name, PrimitiveType.Bool, variables,
            signature.Parameters.Length, new BoundBlock([], signature.Tree.Root.Span), signature.Tree.Root.Span) {
            DeclaringStruct = signature.StructName,
            IsStatic = true,
            IsPrivate = signature.IsPrivate,
            IsInternal = signature.IsInternal,
            IsNoThrow = signature.IsNoThrow,
            RequiresUnsafeCaller = signature.RequiresUnsafeCaller,
            Intrinsic = BoundIntrinsic.ClassTryCreate,
            ClassConstructor = signature.ClassConstructor,
        };
    }
}
