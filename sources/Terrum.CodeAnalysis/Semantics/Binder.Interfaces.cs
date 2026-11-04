// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private sealed record InterfaceFormalSyntax(
        string Name,
        BoundInterfaceFormalKind Kind,
        bool IsSelf,
        SyntaxNode? ConstantType,
        SyntaxNode? TypeDefault,
        SyntaxNode? ConstantDefault,
        ImmutableArray<SyntaxNode> Attributes,
        TextSpan Span);

    private sealed record InterfaceDeclarationPart(
        SyntaxTree Tree,
        SyntaxNode Declaration,
        string QualifiedName,
        bool IsPublic,
        bool IsPartial,
        BoundInterfaceAccessibility Accessibility,
        ImmutableArray<string> AttributeTokens,
        ImmutableArray<InterfaceFormalSyntax> Formals);

    private sealed record ResolvedInterfaceType(
        string QualifiedName,
        BoundModuleImport? ModuleImport,
        ImmutableArray<BoundInterfaceFormal> Formals);

    private readonly record struct InterfaceConstantFormal(
        BoundInterfaceType Type,
        BoundInterfaceTypeParameterScope Scope,
        int Ordinal);

    private static ImmutableDictionary<string, InterfaceConstantFormal>
        InterfaceConstantParameters(
            ImmutableArray<BoundInterfaceFormal> formals,
            BoundInterfaceTypeParameterScope scope)
    {
        var result = ImmutableDictionary.CreateBuilder<string,
            InterfaceConstantFormal>(StringComparer.Ordinal);
        foreach (var formal in formals)
        {
            if (formal.Kind == BoundInterfaceFormalKind.Constant
                && formal.ConstantType is BoundInterfaceType constantType)
            {
                result.Add(formal.Name,
                    new InterfaceConstantFormal(constantType, scope, formal.Ordinal));
            }
        }
        return result.ToImmutable();
    }

    private void DeclareInterfaces()
    {
        var groups = new Dictionary<(string Name, int Arity), List<InterfaceDeclarationPart>>();
        foreach (var (tree, declaration) in _scopedDeclarations.Where(static item =>
                     item.Declaration.Kind == SyntaxKind.InterfaceDeclaration))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SetContext(tree, declaration);
            var interfaceTokenIndex = declaration.Children
                .Select((child, index) => (child, index))
                .First(static item => item.child is SyntaxTokenElement {
                    Token.Kind: TokenKind.InterfaceKeyword,
                }).index;
            var nameToken = declaration.Children.Skip(interfaceTokenIndex + 1)
                .OfType<SyntaxTokenElement>()
                .First(static token => token.Token.Kind == TokenKind.Identifier).Token;
            var parameters = declaration.Children.OfType<SyntaxNode>()
                .FirstOrDefault(static child => child.Kind == SyntaxKind.TypeParameterList);
            var formals = ParseInterfaceFormals(parameters);
            var qualifiedName = Qualify(nameToken.Name);
            if (_structNames.ContainsKey(qualifiedName) || _classNames.ContainsKey(qualifiedName)
                || _genericStructTemplates.ContainsKey(qualifiedName) || _aliases.ContainsKey(qualifiedName))
            {
                Report("TRM3003", $"Duplicate type declaration '{qualifiedName}'.", declaration.Span);
                continue;
            }

            var part = new InterfaceDeclarationPart(
                tree,
                declaration,
                qualifiedName,
                declaration.Children.OfType<SyntaxTokenElement>()
                    .Any(static token => token.Token.Kind == TokenKind.PublicKeyword),
                declaration.Children.OfType<SyntaxTokenElement>()
                    .Any(static token => token.Token.Kind == TokenKind.Identifier
                        && token.Token.Text == "partial"),
                BindInterfaceAccessibility(declaration.Children, declaration.Span),
                InterfaceAttributeTokens(declaration),
                formals);
            var key = (qualifiedName, formals.Length);
            if (!groups.TryGetValue(key, out var parts))
            {
                groups.Add(key, [part]);
            }
            else
            {
                parts.Add(part);
            }
        }

        foreach (var ((qualifiedName, _), parts) in groups)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var first = parts[0];
            SetContext(first.Tree, first.Declaration);
            if (parts.Count > 1 && parts.Any(static part => !part.IsPartial))
            {
                foreach (var duplicate in parts.Skip(1))
                {
                    SetContext(duplicate.Tree, duplicate.Declaration);
                    Report("TRM3003", $"Duplicate interface declaration '{qualifiedName}'.",
                        duplicate.Declaration.Span);
                }
                continue;
            }
            if (parts.Any(part => part.IsPublic != first.IsPublic
                || part.Accessibility != first.Accessibility
                || !part.AttributeTokens.SequenceEqual(first.AttributeTokens, StringComparer.Ordinal)
                || !InterfaceFormalsMatch(first.Formals, part.Formals)))
            {
                Report("TRM3003", $"Partial interface declarations for '{qualifiedName}' disagree.",
                    first.Declaration.Span);
                continue;
            }

            var typeParameters = first.Formals
                .Select((formal, ordinal) => (formal, ordinal))
                .Where(static item => item.formal.Kind == BoundInterfaceFormalKind.Type)
                .ToDictionary(static item => item.formal.Name, static item => item.ordinal,
                    StringComparer.Ordinal);
            var formals = BindInterfaceFormals(first.Formals, typeParameters, groups);
            var constantParameters = InterfaceConstantParameters(formals,
                BoundInterfaceTypeParameterScope.Interface);
            var bases = ImmutableArray.CreateBuilder<BoundInterfaceType>();
            var constraints = ImmutableArray.CreateBuilder<BoundInterfaceConstraint>();
            var members = ImmutableArray.CreateBuilder<BoundInterfaceMemberDefinition>();
            foreach (var part in parts)
            {
                SetContext(part.Tree, part.Declaration);
                var baseList = part.Declaration.Children.OfType<SyntaxNode>()
                    .FirstOrDefault(static child => child.Kind == SyntaxKind.ConformanceList);
                if (baseList is not null)
                {
                    foreach (var baseType in baseList.Children.OfType<SyntaxNode>())
                    {
                        if (BindInterfaceType(baseType, typeParameters, groups,
                            first.Formals.Length,
                            interfaceConstantParameters: constantParameters) is
                                BoundInterfaceType boundBase)
                        {
                            if (boundBase.Kind != BoundInterfaceTypeKind.Nominal)
                            {
                                Report("TRM3005", "An interface base must name an interface declaration.",
                                    baseType.Span);
                            }
                            else
                            {
                                bases.Add(boundBase);
                            }
                        }
                    }
                }
                for (var ordinal = 0; ordinal < part.Formals.Length; ordinal++)
                {
                    foreach (var attribute in part.Formals[ordinal].Attributes)
                    {
                        constraints.Add(new BoundInterfaceConstraint(
                            ordinal, BoundInterfaceConstraintKind.Attribute, null, AttributeName(attribute)));
                    }
                }
                BindInterfaceConstraints(part.Declaration, typeParameters, groups,
                    constraints, interfaceConstantParameters: constantParameters);
                members.AddRange(BindInterfaceMembers(part, typeParameters, groups,
                    formals, members.Count));
            }

            if (!HasUniqueInterfaceMembers(members, first.Declaration.Span))
            {
                continue;
            }
            _interfaces.Add(new BoundInterfaceDefinition(
                qualifiedName,
                first.IsPublic,
                first.IsPartial,
                formals,
                [.. bases.Distinct()],
                [.. constraints.Distinct()],
                members.ToImmutable())
            {
                Accessibility = first.Accessibility,
                AttributeTokens = first.AttributeTokens,
            });
        }

        if (!HasAcyclicInterfaceBases())
        {
            var (tree, declaration) = _scopedDeclarations.First(item =>
                item.Declaration.Kind == SyntaxKind.InterfaceDeclaration);
            SetContext(tree, declaration);
            Report("TRM3005", "Interface inheritance must be finite and acyclic.", declaration.Span);
        }
    }

    private bool HasAcyclicInterfaceBases()
        => BoundInterfaceContract.HasAcyclicBases([.. _interfaces]);

    private ImmutableArray<InterfaceFormalSyntax> ParseInterfaceFormals(SyntaxNode? parameters)
    {
        if (parameters is null)
        {
            return [];
        }

        var groups = new List<List<SyntaxElement>>();
        var current = new List<SyntaxElement>();
        foreach (var child in parameters.Children)
        {
            if (child is SyntaxTokenElement { Token.Kind: TokenKind.Comma })
            {
                groups.Add(current);
                current = [];
            }
            else if (child is not SyntaxTokenElement { Token.Kind: TokenKind.Less or TokenKind.Greater })
            {
                current.Add(child);
            }
        }
        if (current.Count != 0)
        {
            groups.Add(current);
        }

        var result = ImmutableArray.CreateBuilder<InterfaceFormalSyntax>(groups.Count);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var selfCount = 0;
        for (var ordinal = 0; ordinal < groups.Count; ordinal++)
        {
            var group = groups[ordinal];
            var attributes = group.OfType<SyntaxNode>()
                .Where(static node => node.Kind == SyntaxKind.Attribute).ToImmutableArray();
            var directTokens = group.OfType<SyntaxTokenElement>().ToArray();
            var firstToken = directTokens.FirstOrDefault();
            var isSelf = firstToken?.Token.Text == "this";
            var isConstant = firstToken?.Token.Text == "const";
            var typeNodes = group.OfType<SyntaxNode>()
                .Where(static node => node.Kind != SyntaxKind.Attribute).ToArray();
            SyntaxToken? nameToken = null;
            SyntaxNode? constantType = null;
            if (isSelf)
            {
                nameToken = directTokens.Skip(1)
                    .Select(static token => token.Token)
                    .FirstOrDefault(static token => token.Kind == TokenKind.Identifier);
                selfCount++;
            }
            else if (isConstant)
            {
                constantType = typeNodes.FirstOrDefault();
                var typeNodeIndex = constantType is null ? -1 : group.IndexOf(constantType);
                nameToken = typeNodeIndex < 0
                    ? null
                    : group.Skip(typeNodeIndex + 1).OfType<SyntaxTokenElement>()
                        .Select(static token => token.Token)
                        .FirstOrDefault(static token => token.Kind == TokenKind.Identifier);
            }
            else
            {
                nameToken = directTokens
                    .Select(static token => token.Token)
                    .FirstOrDefault(static token => token.Kind == TokenKind.Identifier);
            }
            if (nameToken is null || !seenNames.Add(nameToken.Name)
                || (isSelf && (ordinal != 0 || isConstant)))
            {
                Report("TRM3005", "Interface generic formals require distinct names and a single leading <this T>.",
                    firstToken?.Token.Span ?? parameters.Span);
                continue;
            }

            var equalsIndex = group.FindIndex(static element => element is SyntaxTokenElement {
                Token.Kind: TokenKind.Equals,
            });
            var defaultNode = equalsIndex < 0
                ? null : group.Skip(equalsIndex + 1).OfType<SyntaxNode>()
                    .FirstOrDefault(static node => node.Kind != SyntaxKind.Attribute);
            result.Add(new InterfaceFormalSyntax(
                nameToken.Name,
                isConstant ? BoundInterfaceFormalKind.Constant : BoundInterfaceFormalKind.Type,
                isSelf,
                constantType,
                isConstant ? null : defaultNode,
                isConstant ? defaultNode : null,
                attributes,
                firstToken?.Token.Span ?? parameters.Span));
        }
        if (selfCount > 1)
        {
            Report("TRM3005", "An interface can declare at most one <this T> formal.", parameters.Span);
        }
        return result.ToImmutable();
    }

    private static bool InterfaceFormalsMatch(
        ImmutableArray<InterfaceFormalSyntax> expected,
        ImmutableArray<InterfaceFormalSyntax> actual)
    {
        if (expected.Length != actual.Length)
        {
            return false;
        }
        for (var index = 0; index < expected.Length; index++)
        {
            if (expected[index].Name != actual[index].Name
                || expected[index].Kind != actual[index].Kind
                || expected[index].IsSelf != actual[index].IsSelf
                || !SyntaxArraysMatch(expected[index].Attributes, actual[index].Attributes)
                || NormalizeSyntax(expected[index].ConstantType) != NormalizeSyntax(actual[index].ConstantType)
                || NormalizeSyntax(expected[index].TypeDefault) != NormalizeSyntax(actual[index].TypeDefault)
                || NormalizeSyntax(expected[index].ConstantDefault) != NormalizeSyntax(actual[index].ConstantDefault))
            {
                return false;
            }
        }
        return true;
    }

    private static bool SyntaxArraysMatch(
        ImmutableArray<SyntaxNode> expected, ImmutableArray<SyntaxNode> actual)
    {
        if (expected.Length != actual.Length)
        {
            return false;
        }
        for (var index = 0; index < expected.Length; index++)
        {
            if (NormalizeSyntax(expected[index]) != NormalizeSyntax(actual[index]))
            {
                return false;
            }
        }
        return true;
    }

    private static string? NormalizeSyntax(SyntaxNode? syntax) =>
        syntax is null ? null : string.Concat(syntax.DescendantTokens().Select(static token => token.Text));

    private ImmutableArray<BoundInterfaceFormal> BindInterfaceFormals(
        ImmutableArray<InterfaceFormalSyntax> formals,
        IReadOnlyDictionary<string, int> typeParameters,
        IReadOnlyDictionary<(string Name, int Arity), List<InterfaceDeclarationPart>> interfaces)
    {
        var result = ImmutableArray.CreateBuilder<BoundInterfaceFormal>(formals.Length);
        var priorTypeParameters = new Dictionary<string, int>(StringComparer.Ordinal);
        var priorConstantParameters = new Dictionary<string,
            InterfaceConstantFormal>(StringComparer.Ordinal);
        for (var ordinal = 0; ordinal < formals.Length; ordinal++)
        {
            var formal = formals[ordinal];
            var constantType = formal.Kind == BoundInterfaceFormalKind.Constant
                && formal.ConstantType is SyntaxNode constantTypeSyntax
                    ? BindInterfaceType(constantTypeSyntax, priorTypeParameters,
                        interfaces, ordinal,
                        interfaceConstantParameters: priorConstantParameters)
                    : null;
            var typeDefault = formal.Kind == BoundInterfaceFormalKind.Type
                && formal.TypeDefault is SyntaxNode typeDefaultSyntax
                    ? BindInterfaceType(typeDefaultSyntax, priorTypeParameters,
                        interfaces, ordinal,
                        interfaceConstantParameters: priorConstantParameters)
                    : null;
            BoundConstant? constantDefault = null;
            if (formal.ConstantDefault is SyntaxNode constantDefaultSyntax)
            {
                constantDefault = BindInterfaceConstantDefault(constantDefaultSyntax, constantType, formal.Span);
            }
            result.Add(new BoundInterfaceFormal(
                ordinal, formal.Name, formal.Kind, formal.IsSelf,
                [.. formal.Attributes.Select(AttributeName)],
                [.. formal.Attributes.Select(NormalizeSyntax).Where(static text => text is not null)
                    .Select(static text => text!)],
                constantType, typeDefault, constantDefault));
            if (formal.Kind == BoundInterfaceFormalKind.Type)
            {
                priorTypeParameters.Add(formal.Name, ordinal);
            }
            else if (constantType is not null)
            {
                priorConstantParameters.Add(formal.Name,
                    new InterfaceConstantFormal(constantType,
                        BoundInterfaceTypeParameterScope.Interface, ordinal));
            }
        }
        return result.MoveToImmutable();
    }

    private BoundConstant? BindInterfaceConstantDefault(
        SyntaxNode expression,
        BoundInterfaceType? constantType,
        TextSpan span)
    {
        if (constantType is not { Kind: BoundInterfaceTypeKind.Primitive } type
            || !ConstEvalScalar(type.PrimitiveType))
        {
            Report("TRM3005", "An interface constant value requires a supported scalar type.", span);
            return null;
        }

        var diagnostics = DiagnosticCount;
        var bound = BindExpression(expression, type.PrimitiveType);
        var folded = FoldScalarConstant(bound);
        if (DiagnosticCount != diagnostics || folded is not BoundConstant constant
            || constant.Type != type.PrimitiveType)
        {
            Report("TRM3005", "An interface constant value must be an exact compile-time scalar value.",
                expression.Span);
            return null;
        }
        return constant;
    }

    private BoundInterfaceType? BindInterfaceType(
        SyntaxNode node,
        IReadOnlyDictionary<string, int> typeParameters,
        IReadOnlyDictionary<(string Name, int Arity), List<InterfaceDeclarationPart>> interfaces,
        int visibleTypeParameterCount,
        IReadOnlyDictionary<string, int>? methodTypeParameters = null,
        int visibleMethodTypeParameterCount = int.MaxValue,
        bool allowVoid = false,
        IReadOnlyDictionary<string, InterfaceConstantFormal>? interfaceConstantParameters = null,
        IReadOnlyDictionary<string, InterfaceConstantFormal>? methodConstantParameters = null)
    {
        if (node.Kind == SyntaxKind.ReferenceTypeName)
        {
            var arguments = node.Children.OfType<SyntaxNode>().ToArray();
            if (arguments.Length != 1)
            {
                Report("TRM3005", "A checked-reference type requires one referent type.", node.Span);
                return null;
            }
            var referent = BindInterfaceType(arguments[0], typeParameters, interfaces,
                visibleTypeParameterCount, methodTypeParameters,
                visibleMethodTypeParameterCount,
                interfaceConstantParameters: interfaceConstantParameters,
                methodConstantParameters: methodConstantParameters);
            if (referent is null)
            {
                return null;
            }
            var isWritable = node.Children[0] is SyntaxTokenElement {
                Token.Kind: TokenKind.WriteKeyword,
            };
            var isGc = node.Children.OfType<SyntaxTokenElement>()
                .Any(static token => token.Token.Kind == TokenKind.GcKeyword);
            var isInitialization = node.Children.OfType<SyntaxTokenElement>()
                .Any(static token => token.Token.Kind == TokenKind.Identifier
                    && token.Token.Text == "init");
            var isOptional = node.Children[^1] is SyntaxTokenElement {
                Token.Kind: TokenKind.Question,
            };
            if (isGc && isInitialization)
            {
                Report("TRM3005", "A GC-backed reference cannot be an initialization destination.", node.Span);
                return null;
            }
            return new BoundInterfaceType(
                BoundInterfaceTypeKind.CheckedReference, PrimitiveType.Void, null, -1,
                [BoundInterfaceTypeArgument.ForType(referent)],
                isWritable, isOptional, isGc, isInitialization);
        }

        if (node.Kind != SyntaxKind.TypeName)
        {
            var diagnosticCount = DiagnosticCount;
            var primitive = BindType(node, allowVoid: false);
            return DiagnosticCount == diagnosticCount
                ? ConcreteInterfaceType(primitive) : null;
        }

        var name = TypeNameIdentifier(node);
        var typeArguments = node.Children.OfType<SyntaxNode>().ToArray();
        var isWritableType = node.Children[0] is SyntaxTokenElement {
            Token.Kind: TokenKind.WriteKeyword,
        };
        var isOptionalType = node.Children[^1] is SyntaxTokenElement {
            Token.Kind: TokenKind.Question,
        };
        var isGcType = node.Children.OfType<SyntaxTokenElement>()
            .Any(static token => token.Token.Kind == TokenKind.GcKeyword);
        if (name == "void" && typeArguments.Length == 0 && allowVoid)
        {
            return ConcreteInterfaceType(PrimitiveType.Void);
        }
        if (typeParameters.TryGetValue(name, out var parameterOrdinal))
        {
            if (typeArguments.Length != 0 || parameterOrdinal >= visibleTypeParameterCount)
            {
                Report("TRM3005", $"Type formal '{name}' is not valid at this position.", node.Span);
                return null;
            }
            return new BoundInterfaceType(
                BoundInterfaceTypeKind.TypeParameter, PrimitiveType.Void, null, parameterOrdinal,
                [], isWritableType, isOptionalType, isGcType, false)
                { TypeParameterScope = BoundInterfaceTypeParameterScope.Interface };
        }
        if (methodTypeParameters is not null
            && methodTypeParameters.TryGetValue(name, out parameterOrdinal))
        {
            if (typeArguments.Length != 0 || parameterOrdinal >= visibleMethodTypeParameterCount)
            {
                Report("TRM3005", $"Method type formal '{name}' is not valid at this position.", node.Span);
                return null;
            }
            return new BoundInterfaceType(
                BoundInterfaceTypeKind.TypeParameter, PrimitiveType.Void, null, parameterOrdinal,
                [], isWritableType, isOptionalType, isGcType, false)
                { TypeParameterScope = BoundInterfaceTypeParameterScope.Member };
        }

        if (ResolveInterfaceName(name, typeArguments.Length, interfaces, node.Span,
            out var resolvedInterface) is string interfaceName
            && resolvedInterface is not null)
        {
            var arguments = ImmutableArray.CreateBuilder<BoundInterfaceTypeArgument>(
                resolvedInterface.Formals.Length);
            for (var ordinal = 0; ordinal < resolvedInterface.Formals.Length; ordinal++)
            {
                var formal = resolvedInterface.Formals[ordinal];
                if (ordinal >= typeArguments.Length)
                {
                    if (formal.TypeDefault is BoundInterfaceType typeDefault)
                    {
                        arguments.Add(BoundInterfaceTypeArgument.ForType(
                            BoundInterfaceContract.SubstituteTypeParameters(
                                typeDefault, arguments.ToImmutable())));
                        continue;
                    }
                    if (formal.Kind == BoundInterfaceFormalKind.Constant
                        && formal.ConstantType is BoundInterfaceType constantDefaultType
                        && formal.ConstantDefault is BoundConstant constantDefault)
                    {
                        var substitutedType =
                            BoundInterfaceContract.SubstituteTypeParameters(
                                constantDefaultType, arguments.ToImmutable());
                        if (substitutedType.Kind != BoundInterfaceTypeKind.Primitive
                            || substitutedType.PrimitiveType != constantDefault.Type)
                        {
                            Report("TRM3005",
                                "An interface constant default must retain its complete value domain.",
                                node.Span);
                            return null;
                        }
                        arguments.Add(BoundInterfaceTypeArgument.ForConstant(
                            substitutedType, constantDefault));
                        continue;
                    }
                    Report("TRM3005",
                        $"Interface type '{name}' omits a generic argument without a default.",
                        node.Span);
                    return null;
                }

                var argument = typeArguments[ordinal];
                if (formal.Kind == BoundInterfaceFormalKind.Type)
                {
                    if (argument.Kind
                        == SyntaxKind.ExistentialTypeArgument)
                    {
                        arguments.Add(
                            BoundInterfaceTypeArgument.ForExistentialType());
                        continue;
                    }
                    if (argument.Kind == SyntaxKind.ConstantGenericArgument)
                    {
                        Report("TRM3005",
                            "A constant generic argument cannot satisfy a type formal.",
                            argument.Span);
                        return null;
                    }
                    if (BindInterfaceType(argument, typeParameters, interfaces,
                        visibleTypeParameterCount, methodTypeParameters,
                        visibleMethodTypeParameterCount,
                        interfaceConstantParameters: interfaceConstantParameters,
                        methodConstantParameters: methodConstantParameters)
                        is not BoundInterfaceType boundTypeArgument)
                    {
                        return null;
                    }
                    arguments.Add(BoundInterfaceTypeArgument.ForType(boundTypeArgument));
                }
                else
                {
                    if (argument.Kind
                        == SyntaxKind.ExistentialTypeArgument)
                    {
                        Report("TRM3005",
                            "An existential type argument cannot satisfy a constant formal.",
                            argument.Span);
                        return null;
                    }
                    if (formal.ConstantType is not BoundInterfaceType constantType)
                    {
                        return null;
                    }
                    var substitutedConstantType =
                        BoundInterfaceContract.SubstituteTypeParameters(
                            constantType, arguments.ToImmutable());
                    if (BindInterfaceConstantTypeArgument(argument,
                            substitutedConstantType,
                            interfaceConstantParameters, methodConstantParameters)
                            is not BoundInterfaceTypeArgument boundConstantArgument)
                    {
                        return null;
                    }
                    arguments.Add(boundConstantArgument);
                }
            }
            return new BoundInterfaceType(
                BoundInterfaceTypeKind.Nominal, PrimitiveType.Void, interfaceName, -1,
                arguments.MoveToImmutable(), isWritableType, isOptionalType, isGcType, false)
                { ModuleImport = resolvedInterface.ModuleImport };
        }
        if (typeArguments.Any(ContainsInterfaceTypeParameter)
            || typeArguments.Any(argument => argument.Kind is not SyntaxKind.TypeName
                and not SyntaxKind.ReferenceTypeName))
        {
            Report("TRM3005", $"Constructed type '{name}' is not a declared interface type.", node.Span);
            return null;
        }
        var before = DiagnosticCount;
        var concreteType = BindType(node, allowVoid);
        return DiagnosticCount == before ? ConcreteInterfaceType(concreteType) : null;
    }

    private ImmutableArray<BoundInterfaceFormal> GetInterfaceFormals(
        string interfaceName, int arity, BoundModuleImport? moduleImport,
        IReadOnlyDictionary<(string Name, int Arity), List<InterfaceDeclarationPart>> interfaces)
    {
        var existing = _interfaces.FirstOrDefault(definition =>
            definition.QualifiedName == interfaceName
            && definition.Formals.Length == arity
            && definition.ModuleImport == moduleImport);
        if (existing is not null)
        {
            return existing.Formals;
        }
        if (moduleImport is not null
            || !interfaces.TryGetValue((interfaceName, arity), out var parts)
            || parts.Count == 0)
        {
            return [];
        }

        var part = parts[0];
        var typeParameters = part.Formals
            .Select((formal, ordinal) => (formal, ordinal))
            .Where(static item => item.formal.Kind == BoundInterfaceFormalKind.Type)
            .ToDictionary(static item => item.formal.Name,
                static item => item.ordinal, StringComparer.Ordinal);
        var previousContext = CaptureContext();
        SetContext(part.Tree, part.Declaration);
        try
        {
            return BindInterfaceFormals(part.Formals, typeParameters, interfaces);
        }
        finally
        {
            RestoreContext(previousContext);
        }
    }

    private BoundInterfaceTypeArgument? BindInterfaceConstantTypeArgument(
        SyntaxNode argument, BoundInterfaceType constantType,
        IReadOnlyDictionary<string, InterfaceConstantFormal>? interfaceConstantParameters,
        IReadOnlyDictionary<string, InterfaceConstantFormal>? methodConstantParameters)
    {
        if (argument.Kind == SyntaxKind.TypeName)
        {
            var name = TypeNameIdentifier(argument);
            if (argument.Children.OfType<SyntaxNode>().Any())
            {
                Report("TRM3005",
                    "A constant generic argument cannot have type arguments.",
                    argument.Span);
                return null;
            }
            if (methodConstantParameters is not null
                && methodConstantParameters.TryGetValue(name,
                    out var methodConstant))
            {
                if (!AreInterfaceTypesEquivalent(constantType, methodConstant.Type))
                {
                    Report("TRM3005",
                        "A constant generic argument has an incompatible value domain.",
                        argument.Span);
                    return null;
                }
                return BoundInterfaceTypeArgument.ForConstantParameter(
                    constantType, methodConstant.Scope, methodConstant.Ordinal);
            }
            if (interfaceConstantParameters is not null
                && interfaceConstantParameters.TryGetValue(name,
                    out var interfaceConstant))
            {
                if (!AreInterfaceTypesEquivalent(constantType, interfaceConstant.Type))
                {
                    Report("TRM3005",
                        "A constant generic argument has an incompatible value domain.",
                        argument.Span);
                    return null;
                }
                return BoundInterfaceTypeArgument.ForConstantParameter(
                    constantType, interfaceConstant.Scope, interfaceConstant.Ordinal);
            }
        }

        var expression = argument.Kind switch {
            SyntaxKind.ConstantGenericArgument
                when argument.Children.OfType<SyntaxNode>().SingleOrDefault()
                    is SyntaxNode constantExpression => constantExpression,
            SyntaxKind.TypeName => new SyntaxNode(SyntaxKind.NameExpression,
                argument.Children, argument.Span.Start),
            _ => argument,
        };
        var constant = BindInterfaceConstantDefault(expression,
            constantType, argument.Span);
        return constant is BoundConstant value
            ? BoundInterfaceTypeArgument.ForConstant(constantType, value) : null;
    }

    private static bool AreInterfaceTypesEquivalent(
        BoundInterfaceType left, BoundInterfaceType right)
        => BoundInterfaceContract.AreEquivalent(left, right);

    private static BoundInterfaceType ConcreteInterfaceType(PrimitiveType type) =>
        new(BoundInterfaceTypeKind.Primitive, type, null, -1, [], false, false, false, false);

    private static bool ContainsInterfaceTypeParameter(SyntaxNode node) =>
        node.Kind == SyntaxKind.TypeName
        && node.Children.OfType<SyntaxTokenElement>()
            .Any(static token => token.Token.Kind == TokenKind.Identifier
                && token.Token.Text == "this");

    private string? ResolveInterfaceName(
        string name,
        int arity,
        IReadOnlyDictionary<(string Name, int Arity), List<InterfaceDeclarationPart>> interfaces,
        TextSpan span,
        out ResolvedInterfaceType? resolvedInterface)
    {
        resolvedInterface = null;
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        if (name.Contains('.', StringComparison.Ordinal))
        {
            _ = candidates.Add(name);
        }
        else
        {
            _ = candidates.Add(name);
            _ = candidates.Add(Qualify(name));
            var parent = _namespace;
            while (parent.Contains('.', StringComparison.Ordinal))
            {
                parent = parent[..parent.LastIndexOf('.', StringComparison.Ordinal)];
                _ = candidates.Add(parent + "." + name);
            }
            foreach (var imported in _usings)
            {
                _ = candidates.Add(imported + "." + name);
            }
        }
        var localMatches = interfaces
            .Where(item => candidates.Contains(item.Key.Name)
                && CanOmitInterfaceArguments(item.Value[0].Formals, arity))
            .Select(static item => item.Key)
            .ToArray();
        var exactLocalMatches = localMatches
            .Where(key => key.Arity == arity).ToArray();
        var selectedLocalMatches = exactLocalMatches.Length == 0
            ? localMatches : exactLocalMatches;
        if (selectedLocalMatches.Length > 1)
        {
            Report("TRM3005", $"Interface type '{name}' is ambiguous.", span);
            return null;
        }
        if (selectedLocalMatches.Length == 1)
        {
            var (selectedName, selectedArity) = selectedLocalMatches[0];
            var formals = GetInterfaceFormals(selectedName, selectedArity,
                moduleImport: null, interfaces);
            resolvedInterface = new ResolvedInterfaceType(selectedName, null, formals);
            return selectedName;
        }

        var importedInterfaces = _interfaces.Where(definition =>
                definition.ModuleImport is not null
                && candidates.Contains(definition.QualifiedName)
                && CanOmitInterfaceArguments(definition.Formals, arity))
            .OrderByDescending(definition => definition.Formals.Length == arity)
            .GroupBy(static definition => definition.ModuleImport)
            .Select(static group => group.First()).ToArray();
        if (importedInterfaces.Length > 1)
        {
            Report("TRM3005", $"Imported interface type '{name}' is ambiguous.", span);
            return null;
        }
        if (importedInterfaces.Length == 1)
        {
            resolvedInterface = new ResolvedInterfaceType(
                importedInterfaces[0].QualifiedName,
                importedInterfaces[0].ModuleImport,
                importedInterfaces[0].Formals);
            return importedInterfaces[0].QualifiedName;
        }
        return null;
    }

    private static bool CanOmitInterfaceArguments(
        ImmutableArray<InterfaceFormalSyntax> formals, int suppliedCount)
    {
        if ((uint)suppliedCount > (uint)formals.Length)
        {
            return false;
        }
        for (var ordinal = suppliedCount; ordinal < formals.Length; ordinal++)
        {
            if (formals[ordinal].TypeDefault is null
                && formals[ordinal].ConstantDefault is null)
            {
                return false;
            }
        }
        return true;
    }

    private static bool CanOmitInterfaceArguments(
        ImmutableArray<BoundInterfaceFormal> formals, int suppliedCount)
    {
        if ((uint)suppliedCount > (uint)formals.Length)
        {
            return false;
        }
        for (var ordinal = suppliedCount; ordinal < formals.Length; ordinal++)
        {
            if (formals[ordinal].TypeDefault is null
                && formals[ordinal].ConstantDefault is null)
            {
                return false;
            }
        }
        return true;
    }

    private ImmutableArray<BoundInterfaceMemberDefinition> BindInterfaceMembers(
        InterfaceDeclarationPart part,
        IReadOnlyDictionary<string, int> interfaceTypeParameters,
        IReadOnlyDictionary<(string Name, int Arity), List<InterfaceDeclarationPart>> interfaces,
        ImmutableArray<BoundInterfaceFormal> ownerFormals,
        int firstOrdinal)
    {
        var result = ImmutableArray.CreateBuilder<BoundInterfaceMemberDefinition>();
        var interfaceConstantParameters = InterfaceConstantParameters(ownerFormals,
            BoundInterfaceTypeParameterScope.Interface);
        foreach (var declaration in part.Declaration.Children.OfType<SyntaxNode>())
        {
            var kind = declaration.Kind switch {
                SyntaxKind.FunctionDeclaration => BoundInterfaceMemberKind.Callable,
                SyntaxKind.OperatorDeclaration => BoundInterfaceMemberKind.Operator,
                SyntaxKind.PropertyDeclaration => BoundInterfaceMemberKind.Property,
                SyntaxKind.IndexerDeclaration => BoundInterfaceMemberKind.Indexer,
                SyntaxKind.AssociatedTypeRequirement => BoundInterfaceMemberKind.AssociatedType,
                SyntaxKind.InterfaceConstantDeclaration => BoundInterfaceMemberKind.Constant,
                _ => (BoundInterfaceMemberKind?)null,
            };
            if (kind is not BoundInterfaceMemberKind memberKind)
            {
                continue;
            }

            SetContext(part.Tree, declaration);
            var typeNode = FirstInterfaceTypeNode(declaration);
            var methodTypeParameterNode = declaration.Children.OfType<SyntaxNode>()
                .FirstOrDefault(static child => child.Kind == SyntaxKind.TypeParameterList);
            var methodFormalSyntax = ParseInterfaceFormals(methodTypeParameterNode);
            var methodFormals = BindInterfaceMemberFormals(methodFormalSyntax,
                interfaceTypeParameters, interfaces, ownerFormals.Length,
                interfaceConstantParameters);
            var methodTypeParameters = methodFormals
                .Where(static formal => formal.Kind == BoundInterfaceFormalKind.Type)
                .ToDictionary(static formal => formal.Name, static formal => formal.Ordinal,
                    StringComparer.Ordinal);
            var methodConstantParameters = InterfaceConstantParameters(
                methodFormals, BoundInterfaceTypeParameterScope.Member);

            var parameters = ImmutableArray.CreateBuilder<BoundInterfaceParameter>();
            var parameterList = declaration.Children.OfType<SyntaxNode>()
                .FirstOrDefault(static child => child.Kind is SyntaxKind.ParameterList
                    or SyntaxKind.IndexerParameterList);
            if (parameterList is not null)
            {
                foreach (var parameter in parameterList.Children.OfType<SyntaxNode>()
                             .Where(static child => child.Kind == SyntaxKind.Parameter))
                {
                    if (BindInterfaceParameter(parameter, interfaceTypeParameters,
                        methodTypeParameters, interfaces, ownerFormals.Length,
                        methodFormals.Length, interfaceConstantParameters,
                        methodConstantParameters) is BoundInterfaceParameter boundParameter)
                    {
                        parameters.Add(boundParameter);
                    }
                }
            }

            BoundInterfaceType? resultType = null;
            if (memberKind is BoundInterfaceMemberKind.Callable or BoundInterfaceMemberKind.Operator)
            {
                resultType = typeNode is null ? null
                    : BindInterfaceType(typeNode, interfaceTypeParameters, interfaces,
                        ownerFormals.Length, methodTypeParameters, methodFormals.Length,
                        allowVoid: true,
                        interfaceConstantParameters: interfaceConstantParameters,
                        methodConstantParameters: methodConstantParameters);
            }
            else if (memberKind is BoundInterfaceMemberKind.Property or BoundInterfaceMemberKind.Indexer)
            {
                resultType = typeNode is null ? null
                    : BindInterfaceType(typeNode, interfaceTypeParameters, interfaces,
                        ownerFormals.Length,
                        interfaceConstantParameters: interfaceConstantParameters);
            }

            BoundInterfaceType? associatedDefaultType = null;
            if (memberKind == BoundInterfaceMemberKind.AssociatedType
                && AssociatedTypeDefaultNode(declaration) is SyntaxNode associatedDefault)
            {
                associatedDefaultType = BindInterfaceType(associatedDefault,
                    interfaceTypeParameters, interfaces, ownerFormals.Length,
                    interfaceConstantParameters: interfaceConstantParameters);
            }

            BoundInterfaceType? associatedConstantType = null;
            BoundConstant? associatedConstantDefault = null;
            if (memberKind == BoundInterfaceMemberKind.Constant && typeNode is not null)
            {
                associatedConstantType = BindInterfaceType(typeNode,
                    interfaceTypeParameters, interfaces, ownerFormals.Length,
                    interfaceConstantParameters: interfaceConstantParameters);
                if (AssociatedConstantDefaultNode(declaration) is SyntaxNode constantDefault)
                {
                    associatedConstantDefault = BindInterfaceConstantDefault(constantDefault,
                        associatedConstantType, declaration.Span);
                }
            }

            var constraints = ImmutableArray.CreateBuilder<BoundInterfaceConstraint>();
            BindInterfaceConstraints(declaration, interfaceTypeParameters, interfaces,
                constraints, methodTypeParameters, interfaceConstantParameters,
                methodConstantParameters);
            var isStatic = declaration.Children.OfType<SyntaxTokenElement>()
                .Any(static token => token.Token.Kind == TokenKind.StaticKeyword);
            var accessors = BindInterfaceAccessors(declaration, interfaceTypeParameters,
                methodTypeParameters, interfaces, ownerFormals.Length,
                methodFormals.Length, resultType, isStatic,
                interfaceConstantParameters, methodConstantParameters);
            var operatorToken = memberKind == BoundInterfaceMemberKind.Operator
                ? InterfaceOperatorToken(declaration) : TokenKind.Bad;
            var name = InterfaceMemberName(declaration, memberKind, typeNode, operatorToken);
            var attributes = InterfaceDeclarationAttributes(declaration);
            if (attributes.Contains("NoThrow", StringComparer.Ordinal))
            {
                Report("TRM3017", "The general NoThrow source contract is retired.", declaration.Span);
            }
            var contractTokens = declaration.Children.OfType<SyntaxNode>()
                .Where(static child => child.Kind == SyntaxKind.ContractClause)
                .Select(NormalizeSyntax).Where(static text => text is not null)
                .Select(static text => text!).ToImmutableArray();
            var dependencies = BindInterfaceMemberDependencies(
                declaration, memberKind, parameters.ToImmutable(), resultType);
            var declarationKey = InterfaceMemberKey(memberKind, name, operatorToken,
                parameters, methodFormals);
            var accessibility = BindInterfaceAccessibility(declaration.Children, declaration.Span);
            var hasBody = declaration.DescendantNodes().Any(static node =>
                node.Kind is SyntaxKind.Block or SyntaxKind.ExpressionBody);
            if (accessibility == BoundInterfaceAccessibility.Private && !hasBody)
            {
                Report("TRM3001", "A private interface helper must have a complete body.", declaration.Span);
            }
            result.Add(new BoundInterfaceMemberDefinition(
                firstOrdinal + result.Count,
                name,
                declarationKey,
                memberKind,
                accessibility,
                isStatic,
                declaration.Children.OfType<SyntaxTokenElement>()
                    .Any(static token => token.Token.Kind == TokenKind.ExternKeyword),
                declaration.Children.OfType<SyntaxTokenElement>()
                    .Any(static token => token.Token.Kind == TokenKind.Identifier
                        && token.Token.Text == "partial"),
                declaration.Children.OfType<SyntaxTokenElement>()
                    .Any(static token => token.Token.Kind == TokenKind.UnsafeKeyword),
                attributes.Contains("DoesNotReturn", StringComparer.Ordinal),
                declaration.Children.OfType<SyntaxTokenElement>()
                    .Any(static token => token.Token.Kind == TokenKind.AsyncKeyword),
                hasBody,
                attributes,
                InterfaceAttributeTokens(declaration),
                contractTokens,
                operatorToken,
                resultType,
                methodFormals,
                constraints.ToImmutable(),
                parameters.ToImmutable(),
                dependencies,
                accessors,
                associatedDefaultType,
                associatedConstantType,
                associatedConstantDefault));
        }
        return result.ToImmutable();
    }

    private ImmutableArray<BoundInterfaceFormal> BindInterfaceMemberFormals(
        ImmutableArray<InterfaceFormalSyntax> formals,
        IReadOnlyDictionary<string, int> interfaceTypeParameters,
        IReadOnlyDictionary<(string Name, int Arity), List<InterfaceDeclarationPart>> interfaces,
        int interfaceFormalCount,
        IReadOnlyDictionary<string, InterfaceConstantFormal> interfaceConstantParameters)
    {
        var result = ImmutableArray.CreateBuilder<BoundInterfaceFormal>(formals.Length);
        var priorMethodTypeParameters = new Dictionary<string, int>(StringComparer.Ordinal);
        var priorMethodConstantParameters = new Dictionary<string,
            InterfaceConstantFormal>(StringComparer.Ordinal);
        for (var ordinal = 0; ordinal < formals.Length; ordinal++)
        {
            var formal = formals[ordinal];
            if (formal.IsSelf || interfaceTypeParameters.ContainsKey(formal.Name)
                || interfaceConstantParameters.ContainsKey(formal.Name))
            {
                Report("TRM3005", "Method generic formals cannot redeclare interface formals or use <this>.",
                    formal.Span);
                continue;
            }
            var constantType = formal.Kind == BoundInterfaceFormalKind.Constant
                && formal.ConstantType is SyntaxNode constantTypeSyntax
                    ? BindInterfaceType(constantTypeSyntax, interfaceTypeParameters, interfaces,
                        interfaceFormalCount, priorMethodTypeParameters, ordinal,
                        interfaceConstantParameters: interfaceConstantParameters,
                        methodConstantParameters: priorMethodConstantParameters)
                    : null;
            var typeDefault = formal.Kind == BoundInterfaceFormalKind.Type
                && formal.TypeDefault is SyntaxNode typeDefaultSyntax
                    ? BindInterfaceType(typeDefaultSyntax, interfaceTypeParameters, interfaces,
                        interfaceFormalCount, priorMethodTypeParameters, ordinal,
                        interfaceConstantParameters: interfaceConstantParameters,
                        methodConstantParameters: priorMethodConstantParameters)
                    : null;
            BoundConstant? constantDefault = null;
            if (formal.ConstantDefault is SyntaxNode constantDefaultSyntax)
            {
                constantDefault = BindInterfaceConstantDefault(constantDefaultSyntax,
                    constantType, formal.Span);
            }
            result.Add(new BoundInterfaceFormal(
                ordinal, formal.Name, formal.Kind, false,
                [.. formal.Attributes.Select(AttributeName)],
                [.. formal.Attributes.Select(NormalizeSyntax).Where(static text => text is not null)
                    .Select(static text => text!)],
                constantType, typeDefault, constantDefault));
            if (formal.Kind == BoundInterfaceFormalKind.Type
                && !priorMethodTypeParameters.TryAdd(formal.Name, ordinal))
            {
                Report("TRM3005", $"Duplicate method type formal '{formal.Name}'.", formal.Span);
            }
            if (formal.Kind == BoundInterfaceFormalKind.Constant
                && constantType is not null
                && !priorMethodConstantParameters.TryAdd(formal.Name,
                    new InterfaceConstantFormal(constantType,
                        BoundInterfaceTypeParameterScope.Member, ordinal)))
            {
                Report("TRM3005", $"Duplicate method constant formal '{formal.Name}'.",
                    formal.Span);
            }
        }
        return result.ToImmutable();
    }

    private ImmutableArray<BoundDependencyContract> BindInterfaceMemberDependencies(
        SyntaxNode declaration,
        BoundInterfaceMemberKind memberKind,
        ImmutableArray<BoundInterfaceParameter> parameters,
        BoundInterfaceType? resultType,
        bool? receiverOverride = null)
    {
        var attributes = new List<(SyntaxNode Attribute, int? InlineParameterOrdinal)>();
        foreach (var attribute in declaration.Children.OfType<SyntaxNode>()
                     .Where(static child => child.Kind == SyntaxKind.Attribute))
        {
            if (TryGetDependencyRelation(AttributeName(attribute), out _))
            {
                if (memberKind is BoundInterfaceMemberKind.Property or BoundInterfaceMemberKind.Indexer)
                {
                    Report("TRM3023",
                        "Dependency annotations on an interface property must target an exact accessor.",
                        attribute.Span);
                }
                else
                {
                    attributes.Add((attribute, null));
                }
            }
        }
        var parameterSyntax = declaration.Children.OfType<SyntaxNode>()
            .Where(static child => child.Kind is SyntaxKind.ParameterList
                or SyntaxKind.IndexerParameterList)
            .SelectMany(static child => child.Children.OfType<SyntaxNode>()
                .Where(static parameter => parameter.Kind == SyntaxKind.Parameter))
            .ToArray();
        var ordinal = 0;
        foreach (var parameter in parameterSyntax)
        {
            foreach (var attribute in parameter.Children.OfType<SyntaxNode>()
                         .Where(static child => child.Kind == SyntaxKind.Attribute))
            {
                if (TryGetDependencyRelation(AttributeName(attribute), out _))
                {
                    attributes.Add((attribute, ordinal));
                }
            }
            ordinal++;
        }
        if (attributes.Count == 0)
        {
            return [];
        }

        var hasReceiver = receiverOverride
            ?? declaration.Children.OfType<SyntaxTokenElement>()
                .All(static token => token.Token.Kind != TokenKind.StaticKeyword);
        var placeholderParameters = parameters.Select(static parameter => new Parameter(
            parameter.Name,
            parameter.Type.Kind == BoundInterfaceTypeKind.Primitive
                ? parameter.Type.PrimitiveType : PrimitiveType.Int32,
            default,
            parameter.Mode is BoundInterfaceParameterMode.Out or BoundInterfaceParameterMode.Init,
            parameter.Mode == BoundInterfaceParameterMode.Init,
            parameter.Mode == BoundInterfaceParameterMode.ImplicitRef)).ToImmutableArray();
        var hasResult = resultType is not null
            && !(resultType.Kind == BoundInterfaceTypeKind.Primitive
                && resultType.PrimitiveType == PrimitiveType.Void);
        var contracts = ImmutableArray.CreateBuilder<BoundDependencyContract>();
        var contractSpans = new List<TextSpan>();
        var seenTargets = new HashSet<(BoundDependencyRelation, BoundDependencyDestination, int?)>();
        foreach (var (attribute, inlineParameterOrdinal) in attributes)
        {
            if (!TryGetDependencyRelation(AttributeName(attribute), out var relation))
            {
                continue;
            }
            var target = GetDependencyAttributeTarget(attribute);
            if (inlineParameterOrdinal is int inlineOrdinal)
            {
                if (target.Kind != DependencyAttributeTargetKind.Default
                    || (uint)inlineOrdinal >= (uint)parameters.Length
                    || parameters[inlineOrdinal].Mode != BoundInterfaceParameterMode.Init
                    || relation is not (BoundDependencyRelation.DependsOn
                        or BoundDependencyRelation.ForwardsBacking))
                {
                    Report("TRM3023",
                        "An inline dependency annotation must target an init parameter and cannot select a second target.",
                        attribute.Span);
                    continue;
                }
                target = new DependencyAttributeTarget(
                    DependencyAttributeTargetKind.Parameter, parameters[inlineOrdinal].Name);
            }
            if (!TryResolveInterfaceDependencyDestination(
                relation, target, parameters, resultType, hasReceiver,
                attribute.Span, out var destination, out var destinationOrdinal))
            {
                continue;
            }
            if (!TryBindDependencySources(attribute, placeholderParameters, hasReceiver,
                out var sources))
            {
                continue;
            }
            if (relation == BoundDependencyRelation.ForwardsBacking && sources.IsEmpty)
            {
                Report("TRM3023", "ForwardsBacking requires at least one source.", attribute.Span);
                continue;
            }
            if (!seenTargets.Add((relation, destination, destinationOrdinal)))
            {
                Report("TRM3023", "A dependency schema cannot repeat the same effective target.",
                    attribute.Span);
                continue;
            }
            contracts.Add(new BoundDependencyContract(relation, destination,
                destinationOrdinal, sources));
            contractSpans.Add(attribute.Span);
        }

        var boundContracts = contracts.ToImmutable();
        for (var index = 0; index < boundContracts.Length; index++)
        {
            var forwarding = boundContracts[index];
            if (forwarding.Relation != BoundDependencyRelation.ForwardsBacking)
            {
                continue;
            }
            var hasRelationship = false;
            var permittedSources = new HashSet<BoundDependencySource>();
            foreach (var relationship in boundContracts)
            {
                if (relationship.Destination != forwarding.Destination
                    || relationship.DestinationOrdinal != forwarding.DestinationOrdinal
                    || relationship.Relation is not (BoundDependencyRelation.DependsOn
                        or BoundDependencyRelation.Captures))
                {
                    continue;
                }
                hasRelationship = true;
                permittedSources.UnionWith(relationship.Sources);
            }
            if (!hasRelationship || forwarding.Sources.Any(source => !permittedSources.Contains(source)))
            {
                Report("TRM3023",
                    "ForwardsBacking requires an existing dependency or capture relationship and cannot add sources.",
                    contractSpans[index]);
            }
        }
        return boundContracts;
    }

    private bool TryResolveInterfaceDependencyDestination(
        BoundDependencyRelation relation,
        DependencyAttributeTarget target,
        ImmutableArray<BoundInterfaceParameter> parameters,
        BoundInterfaceType? resultType,
        bool hasReceiver,
        TextSpan span,
        out BoundDependencyDestination destination,
        out int? destinationOrdinal)
    {
        destination = default;
        destinationOrdinal = null;
        var hasResult = resultType is not null
            && !(resultType.Kind == BoundInterfaceTypeKind.Primitive
                && resultType.PrimitiveType == PrimitiveType.Void);
        switch (target.Kind)
        {
            case DependencyAttributeTargetKind.Default:
                if (relation == BoundDependencyRelation.DependsOn)
                {
                    if (hasResult)
                    {
                        destination = BoundDependencyDestination.Result;
                        return true;
                    }
                    Report("TRM3023", "DependsOn requires a result or init-output target.", span);
                    return false;
                }
                if (relation == BoundDependencyRelation.Captures)
                {
                    if (hasReceiver)
                    {
                        destination = BoundDependencyDestination.Receiver;
                        return true;
                    }
                    Report("TRM3023", "Captures requires an explicit destination on a static interface member.", span);
                    return false;
                }
                if (hasResult)
                {
                    destination = BoundDependencyDestination.Result;
                    return true;
                }
                if (hasReceiver)
                {
                    destination = BoundDependencyDestination.Receiver;
                    return true;
                }
                Report("TRM3023", "ForwardsBacking requires a result or receiver destination.", span);
                return false;

            case DependencyAttributeTargetKind.Return:
                if (!hasResult || (relation == BoundDependencyRelation.Captures
                    && !CanCaptureInterfaceValue(resultType!)))
                {
                    Report("TRM3023", "The return target is incompatible with this dependency schema.", span);
                    return false;
                }
                destination = BoundDependencyDestination.Result;
                return true;

            case DependencyAttributeTargetKind.Receiver:
                if (relation == BoundDependencyRelation.DependsOn || !hasReceiver)
                {
                    Report("TRM3023", "The receiver target is incompatible with this dependency schema.", span);
                    return false;
                }
                destination = BoundDependencyDestination.Receiver;
                return true;

            case DependencyAttributeTargetKind.Parameter:
                var parameterOrdinal = FindParameterOrdinal(target.ParameterName,
                    [.. parameters.Select(static parameter => new Parameter(parameter.Name,
                        PrimitiveType.Int32, default,
                        parameter.Mode is BoundInterfaceParameterMode.Out
                            or BoundInterfaceParameterMode.Init,
                        parameter.Mode == BoundInterfaceParameterMode.Init,
                        parameter.Mode == BoundInterfaceParameterMode.ImplicitRef))]);
                if (parameterOrdinal < 0)
                {
                    Report("TRM3023", $"Dependency annotation targets unknown parameter '{target.ParameterName}'.",
                        span);
                    return false;
                }
                var formal = parameters[parameterOrdinal];
                if (relation == BoundDependencyRelation.DependsOn)
                {
                    if (formal.Mode != BoundInterfaceParameterMode.Init)
                    {
                        Report("TRM3023", "A parameter DependsOn target requires an init destination.", span);
                        return false;
                    }
                    destination = BoundDependencyDestination.InitializedOutput;
                }
                else if (formal.Mode == BoundInterfaceParameterMode.Init)
                {
                    destination = BoundDependencyDestination.InitializedOutput;
                }
                else if (relation == BoundDependencyRelation.Captures
                    && !CanCaptureInterfaceParameter(formal))
                {
                    Report("TRM3023", "Captures requires a retained formal destination.", span);
                    return false;
                }
                else if (relation == BoundDependencyRelation.ForwardsBacking
                    && !CanCaptureInterfaceParameter(formal))
                {
                    Report("TRM3023", "ForwardsBacking requires an initialized output or retained formal destination.", span);
                    return false;
                }
                else
                {
                    destination = BoundDependencyDestination.RetainedFormal;
                }
                destinationOrdinal = parameterOrdinal;
                return true;

            case DependencyAttributeTargetKind.Operation:
                Report("TRM3023", "The operation target requires an admitted completion relationship.", span);
                return false;

            default:
                Report("TRM3023", "Dependency annotation target is unsupported.", span);
                return false;
        }
    }

    private bool CanCaptureInterfaceParameter(BoundInterfaceParameter parameter) =>
        parameter.Mode is BoundInterfaceParameterMode.Out or BoundInterfaceParameterMode.Init
        or BoundInterfaceParameterMode.ImplicitRef
        || parameter.Type.Kind is BoundInterfaceTypeKind.TypeParameter or BoundInterfaceTypeKind.Nominal
        || (parameter.Type.Kind == BoundInterfaceTypeKind.CheckedReference
            && parameter.Type.IsWritable && !parameter.Type.IsGarbageCollected
            && !parameter.Type.IsInitialization && !parameter.Type.IsOptional)
        || (parameter.Type.Kind == BoundInterfaceTypeKind.Primitive
            && CanCaptureParameter(new Parameter(parameter.Name,
                parameter.Type.PrimitiveType, default, false)));

    private bool CanCaptureInterfaceValue(BoundInterfaceType type) =>
        type.Kind is BoundInterfaceTypeKind.TypeParameter or BoundInterfaceTypeKind.Nominal
        || (type.Kind == BoundInterfaceTypeKind.CheckedReference
            && type.IsWritable && !type.IsGarbageCollected
            && !type.IsInitialization && !type.IsOptional)
        || (type.Kind == BoundInterfaceTypeKind.Primitive
            && CanCaptureValue(type.PrimitiveType));

    private BoundInterfaceParameter? BindInterfaceParameter(
        SyntaxNode parameter,
        IReadOnlyDictionary<string, int> interfaceTypeParameters,
        IReadOnlyDictionary<string, int> methodTypeParameters,
        IReadOnlyDictionary<(string Name, int Arity), List<InterfaceDeclarationPart>> interfaces,
        int interfaceFormalCount,
        int methodFormalCount,
        IReadOnlyDictionary<string, InterfaceConstantFormal> interfaceConstantParameters,
        IReadOnlyDictionary<string, InterfaceConstantFormal> methodConstantParameters)
    {
        var typeNode = parameter.Children.OfType<SyntaxNode>()
            .FirstOrDefault(static child => child.Kind != SyntaxKind.Attribute);
        var nameToken = parameter.Children.OfType<SyntaxTokenElement>()
            .LastOrDefault(static token => token.Token.Kind == TokenKind.Identifier)?.Token;
        if (typeNode is null || nameToken is null
            || BindInterfaceType(typeNode, interfaceTypeParameters, interfaces,
                interfaceFormalCount, methodTypeParameters, methodFormalCount,
                interfaceConstantParameters: interfaceConstantParameters,
                methodConstantParameters: methodConstantParameters)
                is not BoundInterfaceType type)
        {
            Report("TRM3005", "An interface parameter requires a complete type and identity.",
                parameter.Span);
            return null;
        }

        var mode = parameter.Children.OfType<SyntaxTokenElement>().Any(static token =>
                token.Token.Kind == TokenKind.OutKeyword)
            ? BoundInterfaceParameterMode.Out
            : parameter.Children.OfType<SyntaxTokenElement>().Any(static token =>
                token.Token.Kind == TokenKind.Identifier && token.Token.Text == "init")
                ? BoundInterfaceParameterMode.Init
                : parameter.Children.OfType<SyntaxTokenElement>().Any(static token =>
                    token.Token.Kind == TokenKind.Identifier && token.Token.Text == "implicit")
                    ? BoundInterfaceParameterMode.ImplicitRef
                    : BoundInterfaceParameterMode.Value;
        var attributes = parameter.Children.OfType<SyntaxNode>()
            .Where(static child => child.Kind == SyntaxKind.Attribute)
            .Select(AttributeName).ToImmutableArray();
        var attributeTokens = parameter.Children.OfType<SyntaxNode>()
            .Where(static child => child.Kind == SyntaxKind.Attribute)
            .Select(NormalizeSyntax).Where(static text => text is not null)
            .Select(static text => text!).ToImmutableArray();
        return new BoundInterfaceParameter(nameToken.Name, type, mode, attributes, attributeTokens);
    }

    private ImmutableArray<BoundInterfaceAccessor> BindInterfaceAccessors(
        SyntaxNode declaration,
        IReadOnlyDictionary<string, int> interfaceTypeParameters,
        IReadOnlyDictionary<string, int> methodTypeParameters,
        IReadOnlyDictionary<(string Name, int Arity), List<InterfaceDeclarationPart>> interfaces,
        int interfaceFormalCount,
        int methodFormalCount,
        BoundInterfaceType? propertyType,
        bool isStatic,
        IReadOnlyDictionary<string, InterfaceConstantFormal> interfaceConstantParameters,
        IReadOnlyDictionary<string, InterfaceConstantFormal> methodConstantParameters)
    {
        var result = ImmutableArray.CreateBuilder<BoundInterfaceAccessor>();
        foreach (var accessor in declaration.Children.OfType<SyntaxNode>())
        {
            var kind = accessor.Kind switch {
                SyntaxKind.GetAccessor => BoundInterfaceAccessorKind.Get,
                SyntaxKind.SetAccessor => BoundInterfaceAccessorKind.Set,
                SyntaxKind.InitAccessor => BoundInterfaceAccessorKind.Init,
                _ => (BoundInterfaceAccessorKind?)null,
            };
            if (kind is not BoundInterfaceAccessorKind accessorKind)
            {
                continue;
            }
            var attributes = InterfaceDeclarationAttributes(accessor);
            if (attributes.Contains("NoThrow", StringComparer.Ordinal))
            {
                Report("TRM3017", "The general NoThrow source contract is retired.", accessor.Span);
            }
            var valueParameters = (accessorKind is BoundInterfaceAccessorKind.Set
                or BoundInterfaceAccessorKind.Init)
                && propertyType is BoundInterfaceType assignedType
                    ? [new BoundInterfaceParameter(
                        "value", assignedType, BoundInterfaceParameterMode.Value, [], [])]
                    : ImmutableArray<BoundInterfaceParameter>.Empty;
            var accessorResult = accessorKind == BoundInterfaceAccessorKind.Get
                ? propertyType
                : ConcreteInterfaceType(PrimitiveType.Void);
            var dependencies = BindInterfaceMemberDependencies(accessor,
                BoundInterfaceMemberKind.Callable, valueParameters, accessorResult,
                receiverOverride: !isStatic);
            var accessibility = BindInterfaceAccessibility(accessor.Children, accessor.Span);
            var hasBody = accessor.DescendantNodes().Any(static node =>
                node.Kind is SyntaxKind.Block or SyntaxKind.ExpressionBody);
            if (accessibility == BoundInterfaceAccessibility.Private && !hasBody)
            {
                Report("TRM3001", "A private interface accessor must have a complete body.", accessor.Span);
            }
            result.Add(new BoundInterfaceAccessor(
                accessorKind,
                accessibility,
                accessor.Children.OfType<SyntaxTokenElement>()
                    .Any(static token => token.Token.Kind == TokenKind.MutKeyword),
                accessor.Children.OfType<SyntaxTokenElement>()
                    .Any(static token => token.Token.Kind == TokenKind.UnsafeKeyword),
                attributes.Contains("DoesNotReturn", StringComparer.Ordinal),
                hasBody,
                attributes,
                InterfaceAttributeTokens(accessor),
                dependencies,
                [.. accessor.Children.OfType<SyntaxNode>()
                    .Where(static child => child.Kind == SyntaxKind.ContractClause)
                    .Select(NormalizeSyntax).Where(static text => text is not null)
                    .Select(static text => text!)]));
        }
        return result.ToImmutable();
    }

    private bool HasUniqueInterfaceMembers(
        ImmutableArray<BoundInterfaceMemberDefinition>.Builder members,
        TextSpan span)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < members.Count; index++)
        {
            var member = members[index];
            if (!keys.Add(member.DeclarationKey))
            {
                Report("TRM3003", $"Duplicate interface member '{member.Name}'.", span);
                return false;
            }
            for (var previous = 0; previous < index; previous++)
            {
                var existing = members[previous];
                if (existing.Name == member.Name
                    && (existing.Kind is not (BoundInterfaceMemberKind.Callable or BoundInterfaceMemberKind.Operator)
                        || member.Kind is not (BoundInterfaceMemberKind.Callable or BoundInterfaceMemberKind.Operator)))
                {
                    Report("TRM3003", $"Conflicting interface member '{member.Name}'.", span);
                    return false;
                }
            }
        }
        return true;
    }

    private static SyntaxNode? FirstInterfaceTypeNode(SyntaxNode declaration) =>
        declaration.Children.OfType<SyntaxNode>().FirstOrDefault(static child =>
            child.Kind is SyntaxKind.TypeName or SyntaxKind.ReferenceTypeName
                or SyntaxKind.AttributedTypeName or SyntaxKind.InlineArrayTypeName);

    private static SyntaxNode? AssociatedTypeDefaultNode(SyntaxNode declaration)
    {
        for (var index = 0; index < declaration.Children.Length; index++)
        {
            if (declaration.Children[index] is not SyntaxTokenElement { Token.Kind: TokenKind.Equals })
            {
                continue;
            }
            return declaration.Children.Skip(index + 1).OfType<SyntaxNode>().FirstOrDefault();
        }
        return null;
    }

    private static SyntaxNode? AssociatedConstantDefaultNode(SyntaxNode declaration) =>
        AssociatedTypeDefaultNode(declaration);

    private BoundInterfaceAccessibility BindInterfaceAccessibility(
        IReadOnlyList<SyntaxElement> elements, TextSpan span)
    {
        var result = BoundInterfaceAccessibility.Default;
        foreach (var element in elements)
        {
            BoundInterfaceAccessibility access;
            if (element is SyntaxNode { Kind: SyntaxKind.FriendVisibility })
            {
                access = BoundInterfaceAccessibility.Friend;
            }
            else if (element is SyntaxTokenElement token)
            {
                access = token.Token.Kind switch {
                    TokenKind.PublicKeyword => BoundInterfaceAccessibility.Public,
                    TokenKind.InternalKeyword => BoundInterfaceAccessibility.Internal,
                    TokenKind.PrivateKeyword => BoundInterfaceAccessibility.Private,
                    TokenKind.Identifier when token.Token.Text == "file" => BoundInterfaceAccessibility.File,
                    _ => BoundInterfaceAccessibility.Default,
                };
            }
            else
            {
                continue;
            }
            if (access == BoundInterfaceAccessibility.Default)
            {
                continue;
            }
            if (result != BoundInterfaceAccessibility.Default)
            {
                Report("TRM3001", "An interface declaration or requirement may specify one access domain.", span);
                return result;
            }
            result = access;
        }
        return result;
    }

    private static ImmutableArray<string> InterfaceDeclarationAttributes(SyntaxNode declaration) =>
        [.. declaration.Children.OfType<SyntaxNode>()
            .Where(static child => child.Kind is SyntaxKind.Attribute or SyntaxKind.FriendVisibility)
            .Select(static child => child.Kind == SyntaxKind.Attribute
                ? AttributeName(child) : NormalizeSyntax(child) ?? "")];

    private static ImmutableArray<string> InterfaceAttributeTokens(SyntaxNode declaration) =>
        [.. declaration.Children.OfType<SyntaxNode>()
            .Where(static child => child.Kind is SyntaxKind.Attribute or SyntaxKind.FriendVisibility)
            .Select(NormalizeSyntax).Where(static text => text is not null)
            .Select(static text => text!)];

    private static TokenKind InterfaceOperatorToken(SyntaxNode declaration)
    {
        for (var index = 0; index + 1 < declaration.Children.Length; index++)
        {
            if (declaration.Children[index] is SyntaxTokenElement {
                Token.Kind: TokenKind.OperatorKeyword,
            } && declaration.Children[index + 1] is SyntaxTokenElement operatorToken)
            {
                return operatorToken.Token.Kind;
            }
        }
        return TokenKind.Bad;
    }

    private static string InterfaceMemberName(
        SyntaxNode declaration,
        BoundInterfaceMemberKind kind,
        SyntaxNode? typeNode,
        TokenKind operatorToken)
    {
        if (kind == BoundInterfaceMemberKind.Indexer)
        {
            return "this";
        }
        if (kind == BoundInterfaceMemberKind.Operator)
        {
            for (var index = 0; index + 1 < declaration.Children.Length; index++)
            {
                if (declaration.Children[index] is SyntaxTokenElement {
                    Token.Kind: TokenKind.OperatorKeyword,
                } && declaration.Children[index + 1] is SyntaxTokenElement symbol)
                {
                    return symbol.Token.Text;
                }
            }
            return operatorToken.ToString();
        }
        if (kind == BoundInterfaceMemberKind.AssociatedType)
        {
            for (var index = 0; index + 1 < declaration.Children.Length; index++)
            {
                if (declaration.Children[index] is SyntaxTokenElement {
                    Token.Kind: TokenKind.Identifier, Token.Text: "type",
                } && declaration.Children[index + 1] is SyntaxTokenElement associatedName)
                {
                    return associatedName.Token.Name;
                }
            }
        }
        if (typeNode is not null)
        {
            var typeIndex = declaration.Children.IndexOf(typeNode);
            if (typeIndex >= 0)
            {
                var nameToken = declaration.Children.Skip(typeIndex + 1)
                    .OfType<SyntaxTokenElement>()
                    .FirstOrDefault(static token => token.Token.Kind == TokenKind.Identifier);
                if (nameToken is not null)
                {
                    return nameToken.Token.Name;
                }
            }
        }
        return "";
    }

    private static string InterfaceMemberKey(
        BoundInterfaceMemberKind kind,
        string name,
        TokenKind operatorToken,
        ImmutableArray<BoundInterfaceParameter>.Builder parameters,
        ImmutableArray<BoundInterfaceFormal> methodFormals)
    {
        var parts = new List<string> {
            ((byte)kind).ToString(System.Globalization.CultureInfo.InvariantCulture),
            name,
            operatorToken.ToString(),
            methodFormals.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        parts.AddRange(methodFormals.Select(static formal => ((byte)formal.Kind)
            .ToString(System.Globalization.CultureInfo.InvariantCulture)));
        foreach (var parameter in parameters)
        {
            parts.Add(((byte)parameter.Mode).ToString(System.Globalization.CultureInfo.InvariantCulture));
            parts.Add(InterfaceTypeKey(parameter.Type));
        }
        return string.Join('|', parts);
    }

    private static string InterfaceTypeKey(BoundInterfaceType type) =>
        $"{(byte)type.Kind}:{(int)type.PrimitiveType}:{type.NominalName}:{InterfaceImportKey(type.ModuleImport)}:{type.TypeParameterScope}:{type.TypeParameterOrdinal}:{type.IsWritable}:{type.IsOptional}:{type.IsGarbageCollected}:{type.IsInitialization}[{string.Join(",", type.TypeArguments.Select(InterfaceTypeArgumentKey))}]";

    private static string InterfaceImportKey(BoundModuleImport? import) =>
        import is null ? "" : $"{import.ProducerIdentity}:{import.ExportKey}:{import.TargetRequirement}:{import.RuntimePolicy}";

    private static string InterfaceTypeArgumentKey(
        BoundInterfaceTypeArgument argument) =>
        argument.Kind switch {
            BoundInterfaceTypeArgumentKind.Type
                => $"T:{InterfaceTypeKey(argument.Type!)}",
            BoundInterfaceTypeArgumentKind.ExistentialType => "X",
            _ => $"C:{InterfaceTypeKey(argument.ConstantType!)}:"
                + (argument.ConstantValue is BoundConstant constant
                    ? $"{(int)constant.Type}:{constant.Bits.Low}:{constant.Bits.High}"
                    : $"{argument.ConstantParameterScope}:{argument.ConstantParameterOrdinal}"),
        };
    private void BindInterfaceConstraints(
        SyntaxNode declaration,
        IReadOnlyDictionary<string, int> typeParameters,
        IReadOnlyDictionary<(string Name, int Arity), List<InterfaceDeclarationPart>> interfaces,
        ImmutableArray<BoundInterfaceConstraint>.Builder constraints,
        Dictionary<string, int>? methodTypeParameters = null,
        IReadOnlyDictionary<string, InterfaceConstantFormal>? interfaceConstantParameters = null,
        IReadOnlyDictionary<string, InterfaceConstantFormal>? methodConstantParameters = null)
    {
        foreach (var constraint in declaration.Children.OfType<SyntaxNode>()
                     .Where(static node => node.Kind == SyntaxKind.ConstraintClause))
        {
            var formalName = constraint.Children.OfType<SyntaxTokenElement>()
                .FirstOrDefault(static token => token.Token.Kind == TokenKind.Identifier)?.Token.Name;
            var formalOrdinal = -1;
            var formalScope = BoundInterfaceTypeParameterScope.Interface;
            var foundFormal = formalName is not null
                && typeParameters.TryGetValue(formalName, out formalOrdinal);
            if (!foundFormal && formalName is not null && methodTypeParameters is not null
                && methodTypeParameters.TryGetValue(formalName, out formalOrdinal))
            {
                foundFormal = true;
                formalScope = BoundInterfaceTypeParameterScope.Member;
            }
            if (!foundFormal || formalName is null)
            {
                Report("TRM3005", "An interface constraint must target a declared type formal.", constraint.Span);
                continue;
            }
            foreach (var item in constraint.Children.OfType<SyntaxNode>())
            {
                if (item.Kind == SyntaxKind.Attribute)
                {
                    constraints.Add(new BoundInterfaceConstraint(
                        formalOrdinal, BoundInterfaceConstraintKind.Attribute, null, AttributeName(item))
                        { FormalScope = formalScope });
                }
                else if (BindInterfaceType(item, typeParameters, interfaces, int.MaxValue,
                    methodTypeParameters,
                    interfaceConstantParameters: interfaceConstantParameters,
                    methodConstantParameters: methodConstantParameters)
                    is BoundInterfaceType type)
                {
                    constraints.Add(new BoundInterfaceConstraint(
                        formalOrdinal, BoundInterfaceConstraintKind.Type, type, null)
                        { FormalScope = formalScope });
                }
                else if (item.Kind == SyntaxKind.ConstraintTerm)
                {
                    Report("TRM3005",
                        "This generic property constraint is not supported by interface contracts.",
                        item.Span);
                }
            }
        }
    }
}
