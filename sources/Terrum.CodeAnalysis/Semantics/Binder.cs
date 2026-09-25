// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Threading;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed class Binder
{
    private const int MaximumSemanticDepth = 128;
    private SyntaxTree _tree;
    private readonly ImmutableArray<SyntaxTree> _trees;
    private readonly Dictionary<SyntaxTree, (string Namespace, ImmutableArray<string> Usings)> _contexts = [];
    private readonly CancellationToken _cancellationToken;
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly List<SourceDiagnostic> _sourceDiagnostics = [];
    private readonly List<Signature> _signatures = [];
    private readonly List<BoundStruct> _structs = [];
    private readonly Dictionary<string, PrimitiveType> _structNames = new(StringComparer.Ordinal);
    private readonly ImmutableArray<HostBuffer> _buffers;
    private readonly Dictionary<string, int> _functions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usings = new(StringComparer.Ordinal);
    private string _namespace = "";
    private readonly List<BoundVariable> _variables = [];
    private readonly List<Dictionary<string, int>> _scopes = [];
    private PrimitiveType _returnType;
    private string? _currentStruct;
    private bool _constructor;
    private readonly bool _isLibrary;
    private readonly CompilationReference? _reference;

    internal Binder(SyntaxTree tree, CancellationToken cancellationToken)
        : this([tree], [], cancellationToken)
    {
    }

    internal Binder(SyntaxTree tree, bool isLibrary, CancellationToken cancellationToken, CompilationReference? reference = null)
        : this([tree], [], cancellationToken, isLibrary, reference)
    {
    }

    internal Binder(ImmutableArray<SyntaxTree> trees, CancellationToken cancellationToken)
        : this(trees, [], cancellationToken)
    {
    }

    internal Binder(ImmutableArray<SyntaxTree> trees, ImmutableArray<HostBuffer> buffers, CancellationToken cancellationToken,
        bool isLibrary = false, CompilationReference? reference = null)
    {
        _tree = trees[0];
        _trees = trees;
        _buffers = buffers;
        _cancellationToken = cancellationToken;
        _isLibrary = isLibrary;
        _reference = reference;
    }

    internal IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;
    internal IReadOnlyList<SourceDiagnostic> SourceDiagnostics => _sourceDiagnostics;

    private void SetContext(SyntaxTree tree)
    {
        _tree = tree;
        var (namespaceName, usings) = _contexts[tree];
        _namespace = namespaceName;
        _usings.Clear();
        foreach (var imported in usings)
        {
            _ = _usings.Add(imported);
        }
    }

    internal BoundProgram? Bind()
    {
        // Parser depth limits do not bound left-associated flat expression trees.
        var pending = new Stack<(SyntaxTree Tree, SyntaxNode Node, int Depth)>();
        if (_reference is not null)
        {
            foreach (var structure in _reference.Structures)
            {
                _structNames.Add(structure.Name, (PrimitiveType)(100 + _structs.Count));
                _structs.Add(structure);
            }
            foreach (var function in _reference.Functions)
            {
                _functions.Add(function.Name, _signatures.Count);
                var parameters = ImmutableArray.CreateBuilder<Parameter>();
                var offset = function.IsConstructor || (function.DeclaringStruct is not null && !function.IsStatic) ? 1 : 0;
                for (var index = offset; index < function.Variables.Length; index++)
                {
                    var parameter = function.Variables[index];
                    parameters.Add(new Parameter(parameter.Name, parameter.Type, default, parameter.IsOut));
                }
                _signatures.Add(new Signature(function.Name, function.ReturnType, parameters.ToImmutable(),
                    null, null, _trees[0], function.DeclaringStruct, function.IsConstructor,
                    false, false, false, function.IsStatic, function));
            }
        }
        foreach (var tree in _trees)
        {
            pending.Push((tree, tree.Root, 0));
        }

        while (pending.TryPop(out var item))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _tree = item.Tree;

            if (item.Depth >= MaximumSemanticDepth)
            {
                Report("TRM3015", $"Semantic nesting exceeds {MaximumSemanticDepth} nodes.", item.Node.Span);
                return null;
            }

            foreach (var child in item.Node.Children)
            {
                if (child is SyntaxNode node)
                {
                    pending.Push((item.Tree, node, item.Depth + 1));
                }
            }
        }

        foreach (var tree in _trees)
        {
            _tree = tree;
            _namespace = "";
            _usings.Clear();
            foreach (var child in tree.Root.Children)
            {
                if (child is SyntaxNode { Kind: SyntaxKind.UsingDirective } usingNode)
                {
                    _ = _usings.Add(DirectiveName(usingNode));
                }
                else if (child is SyntaxNode { Kind: SyntaxKind.NamespaceDeclaration } namespaceNode)
                {
                    _namespace = DirectiveName(namespaceNode);
                }
            }
            _contexts.Add(tree, (_namespace, [.. _usings]));
            foreach (var child in tree.Root.Children)
            {
                if (child is SyntaxNode { Kind: SyntaxKind.StructDeclaration } structure)
                {
                    DeclareStruct(structure);
                }
            }
        }

        foreach (var tree in _trees)
        {
            SetContext(tree);
            foreach (var child in tree.Root.Children)
            {
                if (child is not SyntaxNode node || node.Kind is SyntaxKind.UsingDirective or SyntaxKind.NamespaceDeclaration)
                {
                    continue;
                }
                if (node.Kind == SyntaxKind.StructDeclaration)
                {
                    var structName = StructName(node).Text;
                    foreach (var member in node.Children)
                    {
                        if (member is SyntaxNode declaration && declaration.Kind is SyntaxKind.FunctionDeclaration or SyntaxKind.ConstructorDeclaration)
                        {
                            Declare(declaration, structName);
                        }
                    }
                    continue;
                }
                if (node.Kind != SyntaxKind.FunctionDeclaration)
                {
                    Report("TRM3001", "Only top-level function declarations are supported.", node.Span);
                    continue;
                }
                Declare(node, null);
            }
        }

        var entryPoint = -1;

        for (var index = 0; !_isLibrary && index < _signatures.Count; index++)
        {
            var signature = _signatures[index];
            if (signature.External is null && signature.Declaration is SyntaxNode declaration && signature.StructName is null
                && (signature.Name == "Main" || signature.Name.EndsWith(".Main", StringComparison.Ordinal))
                && signature.Parameters.Length == 0 &&
                signature.ReturnType is PrimitiveType.Int32 or PrimitiveType.Void)
            {
                if (entryPoint >= 0)
                {
                    _tree = signature.Tree;
                    Report("TRM3002", "Multiple parameterless Main functions make the entry point ambiguous.", declaration.Span);
                }
                else
                {
                    entryPoint = index;
                }
            }
        }

        if (!_isLibrary && entryPoint < 0)
        {
            Report("TRM3002", "A parameterless int32 or void Main function is required.", _tree.Root.Span);
        }

        var bound = ImmutableArray.CreateBuilder<BoundFunction>(_signatures.Count);
        foreach (var signature in _signatures)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (signature.External is BoundFunction external)
            {
                bound.Add(external);
                continue;
            }
            var declaration = signature.Declaration ?? throw new InvalidOperationException("Source declaration is missing.");
            var syntaxBody = signature.Body ?? throw new InvalidOperationException("Source body is missing.");
            SetContext(signature.Tree);
            _variables.Clear();
            _scopes.Clear();
            _scopes.Add(new Dictionary<string, int>(StringComparer.Ordinal));
            _returnType = signature.ReturnType;
            _currentStruct = signature.StructName;
            _constructor = signature.IsConstructor;

            if (signature.StructName is not null && !signature.IsStatic)
            {
                var receiverType = ResolveStruct(signature.StructName);
                _ = AddVariable("this", receiverType, signature.IsConstructor || signature.IsMutable, declaration.Span);
            }

            foreach (var parameter in signature.Parameters)
            {
                var slot = AddVariable(parameter.Name, parameter.Type, parameter.IsOut, parameter.Span);
                if (parameter.IsOut)
                {
                    _variables[slot] = _variables[slot] with { IsOut = true };
                }
            }

            var body = BindBlock(syntaxBody);
            if (_constructor)
            {
                var end = new TextSpan(syntaxBody.Span.End, 0);
                body = body with {
                    Statements = body.Statements.Add(new BoundReturn(
                    new BoundRead(0, _returnType, end), end))
                };
            }
            else if (_returnType == PrimitiveType.Void && !AlwaysReturns(body))
            {
                var end = new TextSpan(syntaxBody.Span.End, 0);
                body = body with { Statements = body.Statements.Add(new BoundReturn(null, end)) };
            }
            else if (_returnType != PrimitiveType.Void && !AlwaysReturns(body))
            {
                Report("TRM3012", $"Not all paths in '{signature.Name}' return a value.", syntaxBody.Span);
            }

            bound.Add(new BoundFunction(signature.Name, signature.ReturnType, [.. _variables],
                signature.Parameters.Length + (signature.StructName is not null && !signature.IsStatic && !signature.IsConstructor ? 1 : 0),
                body, declaration.Span) {
                IsConstructor = signature.IsConstructor,
                IsPrivate = signature.IsPrivate,
                IsInternal = signature.IsInternal,
                IsStatic = signature.IsStatic,
                DeclaringStruct = signature.StructName is null ? null
                   : (string.IsNullOrEmpty(_namespace) ? "" : _namespace + ".") + signature.StructName,
            });
        }

        if (_diagnostics.Count != 0)
        {
            return null;
        }
        var program = new BoundProgram(bound.ToImmutable(), entryPoint) {
            Structures = [.. _structs],
            Buffers = _buffers,
        };
        if (!SemanticVerifier.TryVerifySource(program, out var functionIndex, out var span, out var exceededWorkLimit))
        {
            _tree = _signatures.Count == 0 ? _trees[0] : _signatures[functionIndex].Tree;
            Report("TRM3016", exceededWorkLimit
                ? "Source exceeds the semantic verification work limit."
                : "Source violates definite assignment, initialization, or executable type rules.", span);
            return null;
        }
        return program;
    }

    private static string DirectiveName(SyntaxNode directive)
    {
        var builder = new StringBuilder();
        for (var index = 1; index < directive.Children.Length - 1; index++)
        {
            _ = builder.Append(((SyntaxTokenElement)directive.Children[index]).Token.Text);
        }
        return builder.ToString();
    }

    private static string NameText(SyntaxNode node)
    {
        var builder = new StringBuilder();
        foreach (var token in node.DescendantTokens())
        {
            _ = builder.Append(token.Text);
        }
        return builder.ToString();
    }

    private void DeclareStruct(SyntaxNode declaration)
    {
        var name = StructName(declaration);
        var qualified = string.IsNullOrEmpty(_namespace) ? name.Text : _namespace + "." + name.Text;
        var isPublic = false;
        var visibilityCount = 0;
        foreach (var element in declaration.Children)
        {
            if (element is SyntaxTokenElement modifier)
            {
                isPublic |= modifier.Token.Kind == TokenKind.PublicKeyword;
                visibilityCount += modifier.Token.Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword or TokenKind.PrivateKeyword ? 1 : 0;
                if (modifier.Token.Kind is TokenKind.PrivateKeyword or TokenKind.StaticKeyword or TokenKind.MutKeyword)
                {
                    Report("TRM3001", "Top-level structs support only public or internal visibility.", modifier.Span);
                }
            }
        }
        if (visibilityCount > 1)
        {
            Report("TRM3001", "A struct can declare only one visibility modifier.", declaration.Span);
        }
        if (!_structNames.TryAdd(qualified, (PrimitiveType)(100 + _structs.Count)))
        {
            Report("TRM3003", $"Duplicate struct '{name.Text}'.", name.Span);
        }
        var fields = ImmutableArray.CreateBuilder<BoundStructField>();
        var size = 0;
        var alignment = 1;
        foreach (var member in declaration.Children)
        {
            if (member is not SyntaxNode field || field.Kind != SyntaxKind.FieldDeclaration)
            {
                continue;
            }
            if (field.Children[^2] is not SyntaxTokenElement { Token.Kind: TokenKind.Identifier })
            {
                Report("TRM3005", "Field initializers are not supported; constructors must initialize every field.", field.Span);
                continue;
            }
            var typeNode = (SyntaxNode)field.Children[^3];
            var fieldName = ((SyntaxTokenElement)field.Children[^2]).Token;
            var type = BindType(typeNode, allowVoid: false);
            var width = type switch {
                PrimitiveType.Bool => 1,
                PrimitiveType.Char16 => 2,
                PrimitiveType.Int32 or PrimitiveType.UInt32 => 4,
                PrimitiveType.Void => 0,
                _ => 0,
            };
            var duplicate = false;
            foreach (var previous in fields)
            {
                duplicate |= previous.Name == fieldName.Text;
            }
            var offset = width == 0 ? size : (size + width - 1) & -width;
            if (width == 0 || offset + width > 8 || duplicate)
            {
                Report("TRM3005", "Structs require distinct, naturally aligned primitive fields fitting one 8-byte scalar register.", field.Span);
                continue;
            }
            var fieldIsPublic = false;
            var fieldIsInternal = false;
            var accessModifiers = 0;
            foreach (var child in field.Children)
            {
                if (child is SyntaxTokenElement modifier)
                {
                    fieldIsPublic |= modifier.Token.Kind == TokenKind.PublicKeyword;
                    fieldIsInternal |= modifier.Token.Kind == TokenKind.InternalKeyword;
                    accessModifiers += modifier.Token.Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword or TokenKind.PrivateKeyword ? 1 : 0;
                    if (modifier.Token.Kind is TokenKind.StaticKeyword or TokenKind.MutKeyword)
                    {
                        Report("TRM3001", "Struct fields cannot be static or independently mutable.", modifier.Span);
                    }
                }
            }
            if (accessModifiers > 1)
            {
                Report("TRM3001", "A field can declare only one visibility modifier.", field.Span);
            }
            fields.Add(new BoundStructField(fieldName.Text, type, offset, width) {
                IsPrivate = !fieldIsPublic && !fieldIsInternal,
                IsInternal = fieldIsInternal,
            });
            size = offset + width;
            alignment = Math.Max(alignment, width);
        }
        if (fields.Count == 0)
        {
            Report("TRM3005", "A struct requires at least one field.", declaration.Span);
        }
        size = (size + alignment - 1) & -alignment;
        if (size is not (1 or 2 or 4 or 8))
        {
            Report("TRM3005", "A struct must have a naturally aligned size of 1, 2, 4, or 8 bytes for the Windows scalar ABI.", declaration.Span);
        }
        _structs.Add(new BoundStruct(qualified, fields.ToImmutable(), size) { IsInternal = !isPublic });
    }

    private static SyntaxToken StructName(SyntaxNode declaration)
    {
        for (var index = 0; index + 1 < declaration.Children.Length; index++)
        {
            if (declaration.Children[index] is SyntaxTokenElement token && token.Token.Kind == TokenKind.StructKeyword)
            {
                return ((SyntaxTokenElement)declaration.Children[index + 1]).Token;
            }
        }
        throw new InvalidOperationException();
    }

    private void Declare(SyntaxNode declaration, string? containingType)
    {
        var constructor = declaration.Kind == SyntaxKind.ConstructorDeclaration;
        var modifierCount = declaration.Children.Length - (constructor ? 3 : 4);
        var isMutable = false;
        var isPrivate = false;
        var isInternal = false;
        var isStatic = false;
        var accessModifiers = 0;
        for (var index = 0; index < modifierCount; index++)
        {
            if (declaration.Children[index] is SyntaxTokenElement modifier &&
                modifier.Token.Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword or TokenKind.PrivateKeyword or TokenKind.StaticKeyword)
            {
                isPrivate |= modifier.Token.Kind == TokenKind.PrivateKeyword;
                isInternal |= modifier.Token.Kind == TokenKind.InternalKeyword;
                isStatic |= modifier.Token.Kind == TokenKind.StaticKeyword;
                accessModifiers += modifier.Token.Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword or TokenKind.PrivateKeyword ? 1 : 0;
                continue;
            }
            if (declaration.Children[index] is SyntaxTokenElement mut && mut.Token.Kind == TokenKind.MutKeyword)
            {
                isMutable = true;
                continue;
            }
            Report("TRM3001", "Function modifier is not supported.", declaration.Children[index].Span);
        }
        if ((constructor && (isStatic || isMutable)) || ((containingType is null) && isPrivate))
        {
            Report("TRM3001", "Constructor and function modifiers conflict with their declaration kind.", declaration.Span);
        }
        if (accessModifiers > 1)
        {
            Report("TRM3001", "A function can declare only one visibility modifier.", declaration.Span);
        }
        if (accessModifiers == 0)
        {
            isPrivate = containingType is not null;
            isInternal = containingType is null;
        }
        if (containingType is not null && isMutable && !constructor)
        {
            Report("TRM3001", "Mutating instance methods require a receiver write-back ABI not present in this value profile.", declaration.Span);
        }

        var returnNode = constructor ? null : (SyntaxNode)declaration.Children[^4];
        var nameToken = ((SyntaxTokenElement)declaration.Children[^3]).Token;
        var parameters = (SyntaxNode)declaration.Children[^2];
        var body = (SyntaxNode)declaration.Children[^1];
        var returnType = constructor ? ResolveStruct(containingType!) : BindType(returnNode!, allowVoid: true);
        var declared = ImmutableArray.CreateBuilder<Parameter>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var child in parameters.Children)
        {
            if (child is not SyntaxNode parameter)
            {
                continue;
            }

            var typeNode = (SyntaxNode)parameter.Children[^2];
            var token = ((SyntaxTokenElement)parameter.Children[^1]).Token;
            var type = BindType(typeNode, allowVoid: false);

            var isOut = parameter.Children.Length == 3
                && parameter.Children[0] is SyntaxTokenElement passing
                && passing.Token.Kind == TokenKind.OutKeyword;
            if (parameter.Children.Length != 2 && !isOut)
            {
                Report("TRM3001", "Passing modes are not supported.", parameter.Span);
            }
            if (isOut && returnType != PrimitiveType.Bool)
            {
                Report("TRM3011", "An out parameter requires a bool return type.", parameter.Span);
            }

            if (!names.Add(token.Text))
            {
                Report("TRM3004", $"Duplicate parameter '{token.Text}'.", token.Span);
            }

            declared.Add(new Parameter(token.Text, type, token.Span, isOut));
        }

        var qualifiedName = (string.IsNullOrEmpty(_namespace) ? "" : _namespace + ".")
            + (containingType is null ? "" : containingType + ".")
            + (constructor ? "#ctor" : nameToken.Text);
        if (!_functions.TryAdd(qualifiedName, _signatures.Count))
        {
            Report("TRM3003", $"Duplicate function '{nameToken.Text}'; overloads are not supported.", nameToken.Span);
        }

        _signatures.Add(new Signature(qualifiedName, returnType, declared.ToImmutable(), body, declaration, _tree,
            containingType, constructor, isPrivate, isInternal, isMutable, isStatic));
    }

    private PrimitiveType BindType(SyntaxNode node, bool allowVoid)
    {
        var name = NameText(node);
        var type = name switch {
            "int32" => PrimitiveType.Int32,
            "uint32" => PrimitiveType.UInt32,
            "char16" => PrimitiveType.Char16,
            "bool" => PrimitiveType.Bool,
            "void" when allowVoid => PrimitiveType.Void,
            _ => TryResolveStruct(name, out var structure) ? structure : (PrimitiveType?)null,
        };

        if (type is null)
        {
            Report("TRM3005", $"Type '{name}' is not supported here.", node.Span);
        }

        return type ?? PrimitiveType.Int32;
    }

    private PrimitiveType ResolveStruct(string name) =>
        TryResolveStruct(name, out var type) ? type : PrimitiveType.Int32;

    private bool TryResolveStruct(string name, out PrimitiveType type)
    {
        if (_structNames.TryGetValue(name, out type))
        {
            return true;
        }
        if (_structNames.TryGetValue(_namespace + "." + name, out type))
        {
            return true;
        }
        var parent = _namespace;
        while (parent.Contains('.', StringComparison.Ordinal))
        {
            var separator = parent.LastIndexOf('.', StringComparison.Ordinal);
            parent = parent[..separator];
            if (_structNames.TryGetValue(parent + "." + name, out type))
            {
                return true;
            }
        }
        foreach (var imported in _usings)
        {
            if (_structNames.TryGetValue(imported + "." + name, out type))
            {
                return true;
            }
        }
        return false;
    }

    private BoundBlock BindBlock(SyntaxNode block)
    {
        _scopes.Add(new Dictionary<string, int>(StringComparer.Ordinal));
        var statements = ImmutableArray.CreateBuilder<BoundStatement>();

        foreach (var child in block.Children)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (child is SyntaxNode statement)
            {
                statements.Add(BindStatement(statement));
            }
        }

        _scopes.RemoveAt(_scopes.Count - 1);
        return new BoundBlock(statements.ToImmutable(), block.Span);
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Unsupported syntax kinds are rejected by the default branch.")]
    private BoundStatement BindStatement(SyntaxNode node)
    {
        _cancellationToken.ThrowIfCancellationRequested();

        switch (node.Kind)
        {
            case SyntaxKind.Block:
                return BindBlock(node);

            case SyntaxKind.VariableDeclaration:
            {
                var isMutable = node.Children[0] is SyntaxTokenElement first && first.Token.Kind == TokenKind.MutKeyword;
                var offset = isMutable ? 1 : 0;
                var typeElement = node.Children[offset];
                var name = ((SyntaxTokenElement)node.Children[offset + 1]).Token;
                var initializerNode = (SyntaxNode)node.Children[offset + 3];
                var declaredType = typeElement is SyntaxNode typeNode ? BindType(typeNode, allowVoid: false) : (PrimitiveType?)null;
                var initializer = BindExpression(initializerNode, declaredType);

                if (declaredType is null && initializer.Type == PrimitiveType.Void)
                {
                    Report("TRM3005", "Cannot infer a variable's type from a void expression.", initializerNode.Span);
                }

                // Locals enter scope only after their initializer, including in nested blocks.
                var slot = AddVariable(name.Text, declaredType ?? initializer.Type, isMutable, name.Span);
                return new BoundLocal(slot, initializer, node.Span);
            }

            case SyntaxKind.AssignmentStatement:
            {
                var target = (SyntaxNode)node.Children[0];
                var valueNode = (SyntaxNode)node.Children[2];

                if (target.Kind == SyntaxKind.IndexExpression
                    && TryFindBuffer((SyntaxNode)target.Children[0], out var buffer))
                {
                    if (!_buffers[buffer].IsWritable)
                    {
                        Report("TRM3008", "A read-only host buffer cannot be modified.", target.Span);
                    }
                    return new BoundBufferAssignment(buffer,
                        BindExpression((SyntaxNode)target.Children[2], PrimitiveType.Int32),
                        BindExpression(valueNode, _buffers[buffer].ElementType), node.Span);
                }
                if (target.Kind == SyntaxKind.MemberAccessExpression
                    && (SyntaxNode)target.Children[0] is { Kind: SyntaxKind.NameExpression } receiver)
                {
                    var receiverName = ((SyntaxTokenElement)receiver.Children[0]).Token.Text;
                    var memberName = ((SyntaxTokenElement)target.Children[2]).Token.Text;
                    if (TryFindVariable(receiverName, out var variable)
                        && TryFindField(_variables[variable].Type, memberName, out var field))
                    {
                        CheckFieldAccess(_variables[variable].Type, field, target.Span);
                        if (!_variables[variable].IsMutable)
                        {
                            Report("TRM3008", $"Variable '{receiverName}' is immutable.", target.Span);
                        }
                        return new BoundFieldAssignment(variable, field,
                            BindExpression(valueNode, GetField(_variables[variable].Type, field).Type), node.Span);
                    }
                }
                if (target.Kind != SyntaxKind.NameExpression)
                {
                    Report("TRM3008", "Assignment requires a mutable local variable.", target.Span);
                    _ = BindExpression(valueNode, null);
                    return new BoundBlock([], node.Span);
                }

                var token = ((SyntaxTokenElement)target.Children[0]).Token;
                if (!TryFindVariable(token.Text, out var slot))
                {
                    if (_currentStruct is not null
                        && TryFindField(ResolveStruct(_currentStruct), token.Text, out var ownField))
                    {
                        if (!_variables[0].IsMutable)
                        {
                            Report("TRM3008", "An immutable receiver cannot be mutated.", target.Span);
                        }
                        return new BoundFieldAssignment(0, ownField,
                            BindExpression(valueNode, GetField(ResolveStruct(_currentStruct), ownField).Type), node.Span);
                    }
                    Report("TRM3006", $"Unknown or not-yet-declared variable '{token.Text}'.", token.Span);
                    _ = BindExpression(valueNode, null);
                    return new BoundBlock([], node.Span);
                }

                if (!_variables[slot].IsMutable)
                {
                    Report("TRM3008", $"Variable '{token.Text}' is immutable.", token.Span);
                }

                return new BoundAssignment(slot, BindExpression(valueNode, _variables[slot].Type), node.Span);
            }

            case SyntaxKind.ExpressionStatement:
            {
                var expressionNode = (SyntaxNode)node.Children[0];
                if (expressionNode.Kind != SyntaxKind.InvocationExpression)
                {
                    Report("TRM3009", "Only direct calls may be used as expression statements.", expressionNode.Span);
                }

                return new BoundExpressionStatement(BindExpression(expressionNode, null), node.Span);
            }

            case SyntaxKind.EmptyStatement:
                return new BoundBlock([], node.Span);

            case SyntaxKind.RequireStatement:
                return new BoundRequire(BindExpression((SyntaxNode)node.Children[1], PrimitiveType.Bool), node.Span);

            case SyntaxKind.ReturnStatement:
            {
                if (_constructor)
                {
                    Report("TRM3010", "Constructors initialize every field and return implicitly.", node.Span);
                    return new BoundBlock([], node.Span);
                }
                var valueNode = node.Children[1] as SyntaxNode;
                if (_returnType == PrimitiveType.Void)
                {
                    if (valueNode is not null)
                    {
                        var value = BindExpression(valueNode, PrimitiveType.Void);
                        if (value is BoundCall call)
                        {
                            return new BoundBlock(
                                [new BoundExpressionStatement(call, valueNode.Span), new BoundReturn(null, node.Span)],
                                node.Span);
                        }
                    }

                    return new BoundReturn(null, node.Span);
                }

                if (valueNode is null)
                {
                    Report("TRM3010", "A return value is required.", node.Span);
                    return new BoundReturn(null, node.Span);
                }

                return new BoundReturn(BindExpression(valueNode, _returnType), node.Span);
            }

            case SyntaxKind.IfStatement:
            {
                var conditionNode = (SyntaxNode)node.Children[2];
                var condition = BindExpression(conditionNode, PrimitiveType.Bool);
                var then = BindBlock((SyntaxNode)node.Children[4]);
                BoundStatement? otherwise = null;

                if (node.Children.Length > 5)
                {
                    var clause = (SyntaxNode)node.Children[5];
                    otherwise = BindStatement((SyntaxNode)clause.Children[1]);
                }

                return new BoundIf(condition, then, otherwise, node.Span);
            }

            case SyntaxKind.WhileStatement:
                return new BoundWhile(BindExpression((SyntaxNode)node.Children[2], PrimitiveType.Bool),
                    BindBlock((SyntaxNode)node.Children[4]), node.Span);

            default:
                Report("TRM3001", "This statement is not supported.", node.Span);
                return new BoundBlock([], node.Span);
        }
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Unsupported syntax kinds are rejected by the default branch.")]
    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Runtime struct IDs are represented by enum values outside the declared primitive range.")]
    private BoundExpression BindExpression(SyntaxNode node, PrimitiveType? expected)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        BoundExpression expression;

        switch (node.Kind)
        {
            case SyntaxKind.ParenthesizedExpression:
                expression = BindExpression((SyntaxNode)node.Children[1], expected);
                break;

            case SyntaxKind.VoidExpression:
                expression = new BoundConstant(0, PrimitiveType.Void, node.Span);
                break;

            case SyntaxKind.LiteralExpression:
                expression = BindLiteral(((SyntaxTokenElement)node.Children[0]).Token, expected);
                break;

            case SyntaxKind.NameExpression:
            {
                var token = ((SyntaxTokenElement)node.Children[0]).Token;
                if (TryFindVariable(token.Text, out var slot))
                {
                    expression = new BoundRead(slot, _variables[slot].Type, node.Span);
                }
                else if (_currentStruct is not null && TryFindField(ResolveStruct(_currentStruct), token.Text, out var field))
                {
                    var structure = ResolveStruct(_currentStruct);
                    expression = new BoundFieldRead(new BoundRead(0, structure, node.Span),
                        field, GetField(structure, field).Type, node.Span);
                }
                else
                {
                    Report("TRM3006", $"Unknown or not-yet-declared variable '{token.Text}'.", token.Span);
                    expression = ErrorExpression(node.Span);
                }

                break;
            }

            case SyntaxKind.CastExpression:
            {
                var target = ((SyntaxTokenElement)node.Children[1]).Token.Text switch {
                    "int32" => PrimitiveType.Int32,
                    "uint32" => PrimitiveType.UInt32,
                    "char16" => PrimitiveType.Char16,
                    _ => throw new InvalidOperationException(),
                };
                var castOperand = (SyntaxNode)node.Children[3];
                var literal = castOperand.Kind == SyntaxKind.LiteralExpression
                    ? ((SyntaxTokenElement)castOperand.Children[0]).Token : null;
                var literalContext = literal is { Kind: TokenKind.NumericLiteral }
                    && !literal.Text.EndsWith("i32", StringComparison.Ordinal)
                    && !literal.Text.EndsWith("u32", StringComparison.Ordinal)
                    ? target : (PrimitiveType?)null;
                var operand = BindExpression(castOperand, literalContext);
                if (operand.Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Char16))
                {
                    Report("TRM3007", "A checked cast requires a numeric operand.", operand.Span);
                }
                expression = new BoundCast(operand, target, node.Span);
                break;
            }

            case SyntaxKind.MemberAccessExpression:
            {
                if (((SyntaxTokenElement)node.Children[2]).Token.Text == "Length"
                    && TryFindBuffer((SyntaxNode)node.Children[0], out var buffer))
                {
                    expression = new BoundConstant(_buffers[buffer].Values.Length, PrimitiveType.Int32, node.Span);
                    break;
                }
                var member = ((SyntaxTokenElement)node.Children[2]).Token.Text;
                var receiverNode = (SyntaxNode)node.Children[0];
                var receiverType = KnownType(receiverNode);
                if (receiverType is PrimitiveType fieldType && TryFindField(fieldType, member, out var field))
                {
                    CheckFieldAccess(fieldType, field, node.Span);
                    expression = new BoundFieldRead(BindExpression(receiverNode, fieldType), field,
                        GetField(fieldType, field).Type, node.Span);
                    break;
                }
                var typeName = NameText(receiverNode);
                if (member is "MinValue" or "MaxValue" && typeName is "int32" or "uint32" or "char16")
                {
                    var type = typeName switch {
                        "int32" => PrimitiveType.Int32,
                        "uint32" => PrimitiveType.UInt32,
                        "char16" => PrimitiveType.Char16,
                        _ => throw new InvalidOperationException(),
                    };
                    var value = type switch {
                        PrimitiveType.Int32 => member == "MinValue" ? int.MinValue : int.MaxValue,
                        PrimitiveType.UInt32 => member == "MinValue" ? 0 : -1,
                        PrimitiveType.Char16 => member == "MinValue" ? 0 : ushort.MaxValue,
                        _ => throw new InvalidOperationException(),
                    };
                    expression = new BoundConstant(value, type, node.Span);
                }
                else
                {
                    Report("TRM3006", $"Unknown member '{member}'.", node.Span);
                    expression = ErrorExpression(node.Span);
                }
                break;
            }

            case SyntaxKind.UnaryExpression:
            {
                var op = ((SyntaxTokenElement)node.Children[0]).Token;
                var operandNode = (SyntaxNode)node.Children[1];
                var operandType = op.Kind == TokenKind.Bang ? PrimitiveType.Bool
                    : KnownType(operandNode) ?? expected ?? PrimitiveType.Int32;

                // The spelling of the negative minimum is a single checked int32 constant.
                if (op.Kind == TokenKind.Minus && operandNode.Kind == SyntaxKind.LiteralExpression &&
                    TryMinimumLiteral(((SyntaxTokenElement)operandNode.Children[0]).Token))
                {
                    var literal = ((SyntaxTokenElement)operandNode.Children[0]).Token;
                    if (!literal.Text.EndsWith("i32", StringComparison.Ordinal) && expected != PrimitiveType.Int32)
                    {
                        Report("TRM3014", "An unsuffixed integer literal requires an int32 context.", literal.Span);
                    }

                    expression = new BoundConstant(int.MinValue, PrimitiveType.Int32, node.Span);
                    break;
                }

                var operandContext = operandType == PrimitiveType.Bool ? operandType
                    : expected is PrimitiveType.Int32 or PrimitiveType.UInt32 || KnownType(operandNode) is PrimitiveType.Int32 or PrimitiveType.UInt32
                        ? operandType : (PrimitiveType?)null;
                var operand = BindExpression(operandNode, operandContext);
                if (operand.Type != operandType)
                {
                    Report("TRM3007", $"Unary operator requires {TypeName(operandType)}.", op.Span);
                }
                if (op.Kind is not (TokenKind.Plus or TokenKind.Minus or TokenKind.Tilde or TokenKind.Bang)
                    || (op.Kind == TokenKind.Minus && operandType != PrimitiveType.Int32)
                    || (op.Kind is TokenKind.Plus or TokenKind.Tilde && operandType is not (PrimitiveType.Int32 or PrimitiveType.UInt32)))
                {
                    Report("TRM3001", "Unsupported unary operator.", op.Span);
                }

                expression = new BoundUnary(op.Kind, operand, operandType, node.Span);
                break;
            }

            case SyntaxKind.BinaryExpression:
                expression = BindBinary(node, expected);
                break;

            case SyntaxKind.InvocationExpression:
                expression = BindCall(node);
                break;

            case SyntaxKind.ObjectCreationExpression:
            {
                var typeName = NameText((SyntaxNode)node.Children[1]);
                if (!TryResolveStruct(typeName, out var structure))
                {
                    Report("TRM3005", $"Unknown struct '{typeName}'.", node.Span);
                    expression = ErrorExpression(node.Span);
                    break;
                }

                var constructor = _structs[(int)structure - 100].Name + ".#ctor";
                if (!TryFindFunction(constructor, out var index))
                {
                    Report("TRM3011", $"Struct '{typeName}' has no explicit constructor.", node.Span);
                    expression = ErrorExpression(node.Span);
                    break;
                }
                expression = BindArguments(node, (SyntaxNode)node.Children[2], index, null);
                break;
            }

            case SyntaxKind.IndexExpression:
            {
                if (TryFindBuffer((SyntaxNode)node.Children[0], out var buffer))
                {
                    expression = new BoundBufferRead(buffer,
                        BindExpression((SyntaxNode)node.Children[2], PrimitiveType.Int32),
                        _buffers[buffer].ElementType, node.Span);
                }
                else
                {
                    Report("TRM3006", "Only compiler-supplied host buffers can be indexed.", node.Span);
                    expression = ErrorExpression(node.Span);
                }
                break;
            }

            default:
                Report("TRM3001", "This expression is not supported in the executable profile.", node.Span);
                expression = ErrorExpression(node.Span);
                break;
        }

        if (expected is not null && expression.Type != expected)
        {
            Report("TRM3007", $"Expected {TypeName(expected.Value)}, got {TypeName(expression.Type)}.", node.Span);
        }

        return expression;
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Unsupported operators are rejected by the default branch.")]
    private BoundBinary BindBinary(SyntaxNode node, PrimitiveType? expected)
    {
        var leftNode = (SyntaxNode)node.Children[0];
        var op = ((SyntaxTokenElement)node.Children[1]).Token;
        var rightNode = (SyntaxNode)node.Children[2];
        var leftKnown = KnownType(leftNode);
        var rightKnown = KnownType(rightNode);
        PrimitiveType? operandType;
        PrimitiveType resultType;

        switch (op.Kind)
        {
            case TokenKind.Plus:
            case TokenKind.Minus:
            case TokenKind.Star:
            case TokenKind.Slash:
            case TokenKind.Percent:
                operandType = leftKnown ?? rightKnown ?? (expected is PrimitiveType.Int32 or PrimitiveType.UInt32 ? expected : null);
                resultType = operandType ?? PrimitiveType.Int32;
                if (operandType is not (PrimitiveType.Int32 or PrimitiveType.UInt32))
                {
                    Report("TRM3007", "Arithmetic requires int32 or uint32 operands with a known context.", op.Span);
                }
                break;

            case TokenKind.Less:
            case TokenKind.LessEquals:
            case TokenKind.Greater:
            case TokenKind.GreaterEquals:
                operandType = leftKnown ?? rightKnown;
                resultType = PrimitiveType.Bool;
                if (operandType is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Char16))
                {
                    Report("TRM3007", "Comparison requires numeric operands with a known context.", op.Span);
                }
                break;

            case TokenKind.AmpersandAmpersand:
            case TokenKind.PipePipe:
                operandType = PrimitiveType.Bool;
                resultType = PrimitiveType.Bool;
                break;

            case TokenKind.Ampersand:
            case TokenKind.Pipe:
            case TokenKind.Caret:
                operandType = leftKnown ?? rightKnown ?? expected;
                resultType = operandType ?? PrimitiveType.Int32;
                if (operandType is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Bool))
                {
                    Report("TRM3007", "Bitwise operands must both be int32 or both be bool.", op.Span);
                }
                break;

            case TokenKind.EqualsEquals:
            case TokenKind.BangEquals:
                operandType = leftKnown ?? rightKnown;
                resultType = PrimitiveType.Bool;
                if (operandType is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Char16 or PrimitiveType.Bool))
                {
                    Report("TRM3007", "Equality requires two operands of the same known int32 or bool type.", op.Span);
                }
                break;

            default:
                Report("TRM3001", "Unsupported binary operator.", op.Span);
                operandType = null;
                resultType = PrimitiveType.Int32;
                break;
        }

        var left = BindExpression(leftNode, operandType);
        var right = BindExpression(rightNode, operandType);
        if (operandType is null || left.Type != right.Type || operandType == PrimitiveType.Void)
        {
            Report("TRM3007", "Binary operands must have the same supported type.", op.Span);
        }

        return new BoundBinary(op.Kind, left, right, resultType, node.Span);
    }

    private BoundCall BindCall(SyntaxNode node)
    {
        var callee = (SyntaxNode)node.Children[0];
        var function = -1;
        SyntaxNode? receiver = null;
        if (callee.Kind == SyntaxKind.NameExpression)
        {
            var name = ((SyntaxTokenElement)callee.Children[0]).Token;
            if (TryFindVariable(name.Text, out _))
            {
                Report("TRM3011", $"'{name.Text}' is a variable, not a function.", name.Span);
            }
            else if (!TryFindFunction(name.Text, out function))
            {
                Report("TRM3011", $"Unknown function '{name.Text}'.", name.Span);
            }
        }
        else if (callee.Kind == SyntaxKind.MemberAccessExpression)
        {
            var left = (SyntaxNode)callee.Children[0];
            var member = ((SyntaxTokenElement)callee.Children[2]).Token.Text;
            var type = KnownType(left);
            if (type is PrimitiveType structType && IsStruct(structType))
            {
                receiver = left;
                _ = TryFindFunction(_structs[(int)structType - 100].Name + "." + member, out function);
            }
            else
            {
                _ = TryFindFunction(NameText(callee), out function);
            }
            if (function < 0)
            {
                Report("TRM3011", $"Unknown method '{member}'.", callee.Span);
            }
        }
        else
        {
            Report("TRM3011", "Only direct calls to named functions are supported.", callee.Span);
        }
        return BindArguments(node, (SyntaxNode)node.Children[1], function, receiver);
    }

    private BoundCall BindArguments(SyntaxNode node, SyntaxNode arguments, int function, SyntaxNode? receiver)
    {
        var signature = function < 0 ? null : _signatures[function];
        if (signature is not null && signature.IsPrivate && signature.StructName != _currentStruct)
        {
            Report("TRM3011", $"'{signature.Name}' is private.", node.Span);
        }
        if (signature is not null && receiver is not null && signature.IsStatic)
        {
            Report("TRM3011", "A static method has no receiver.", node.Span);
        }
        var bound = ImmutableArray.CreateBuilder<BoundExpression>();
        var receiverCount = 0;
        if (receiver is not null && signature is not null)
        {
            bound.Add(BindExpression(receiver, ResolveStruct(signature.StructName!)));
            receiverCount = 1;
        }
        else if (signature is { StructName: not null, IsStatic: false, IsConstructor: false })
        {
            if (_currentStruct == signature.StructName && _variables.Count > 0 && _variables[0].Name == "this")
            {
                bound.Add(new BoundRead(0, ResolveStruct(signature.StructName), node.Span));
                receiverCount = 1;
            }
            else
            {
                Report("TRM3011", "Instance methods require a receiver.", node.Span);
            }
        }
        foreach (var child in arguments.Children)
        {
            if (child is not SyntaxNode argument)
            {
                continue;
            }

            var position = bound.Count - receiverCount;
            var expectedParameter = signature is not null && position < signature.Parameters.Length
                ? signature.Parameters[position] : null;
            if (argument.Children[0] is SyntaxTokenElement passing && passing.Token.Kind == TokenKind.OutKeyword)
            {
                if (expectedParameter is null || !expectedParameter.IsOut)
                {
                    Report("TRM3011", "Unexpected out argument.", argument.Span);
                    bound.Add(ErrorExpression(argument.Span));
                    continue;
                }
                var declares = argument.Children.Length == 3
                    && argument.Children[1] is SyntaxTokenElement variableKeyword
                    && variableKeyword.Token.Kind == TokenKind.VarKeyword;
                var outName = declares
                    ? ((SyntaxTokenElement)argument.Children[2]).Token
                    : argument.Children.Length == 2 && argument.Children[1] is SyntaxNode outExpression
                        && outExpression.Kind == SyntaxKind.NameExpression
                        ? ((SyntaxTokenElement)outExpression.Children[0]).Token : null;
                if (outName is null)
                {
                    Report("TRM3011", "Output argument must be a local variable.", argument.Span);
                    bound.Add(ErrorExpression(argument.Span));
                    continue;
                }
                var slot = declares ? AddVariable(outName.Text, expectedParameter.Type, false, outName.Span)
                    : TryFindVariable(outName.Text, out var existing) ? existing : -1;
                if (slot < 0 || _variables[slot].Type != expectedParameter.Type
                    || (!declares && !_variables[slot].IsMutable))
                {
                    Report("TRM3011", "Output target must be a mutable local of matching type.", argument.Span);
                    bound.Add(ErrorExpression(argument.Span));
                    continue;
                }
                bound.Add(new BoundOutArgument(slot, declares, expectedParameter.Type, argument.Span));
                continue;
            }
            if (expectedParameter?.IsOut == true)
            {
                Report("TRM3011", "An out parameter requires an out argument.", argument.Span);
            }
            if (argument.Children.Length != 1 || argument.Children[0] is not SyntaxNode value)
            {
                Report("TRM3011", "Only positional value arguments are supported.", argument.Span);
                // The malformed argument cannot become a valid program; retain no executable value.
                bound.Add(ErrorExpression(argument.Span));
                continue;
            }

            var expected = expectedParameter?.Type;
            bound.Add(BindExpression(value, expected));
        }

        if (signature is not null)
        {
            var supplied = bound.Count - receiverCount;
            if (signature.Parameters.Length != supplied)
            {
                Report("TRM3011", $"Function '{signature.Name}' expects {signature.Parameters.Length} arguments, got {supplied}.", arguments.Span);
            }
        }

        return new BoundCall(Math.Max(0, function), bound.ToImmutable(), signature?.ReturnType ?? PrimitiveType.Int32, node.Span);
    }

    private bool IsStruct(PrimitiveType type) => (int)type >= 100 && (int)type - 100 < _structs.Count;

    private bool TryFindBuffer(SyntaxNode node, out int buffer)
    {
        if (node.Kind == SyntaxKind.MemberAccessExpression
            && ((SyntaxNode)node.Children[0]).Kind == SyntaxKind.NameExpression
            && ((SyntaxTokenElement)((SyntaxNode)node.Children[0]).Children[0]).Token.Text == "Host")
        {
            var name = ((SyntaxTokenElement)node.Children[2]).Token.Text;
            for (var i = 0; i < _buffers.Length; i++)
            {
                if (_buffers[i].Name == name)
                {
                    buffer = i;
                    return true;
                }
            }
        }
        buffer = -1;
        return false;
    }

    private bool TryFindField(PrimitiveType type, string name, out int field)
    {
        if (IsStruct(type))
        {
            var fields = _structs[(int)type - 100].Fields;
            for (var i = 0; i < fields.Length; i++)
            {
                if (fields[i].Name == name)
                {
                    field = i;
                    return true;
                }
            }
        }
        field = -1;
        return false;
    }

    private BoundStructField GetField(PrimitiveType type, int field) => _structs[(int)type - 100].Fields[field];

    private void CheckFieldAccess(PrimitiveType type, int field, TextSpan span)
    {
        if (GetField(type, field).IsPrivate
            && _structs[(int)type - 100].Name != (string.IsNullOrEmpty(_namespace)
                ? _currentStruct : _namespace + "." + _currentStruct))
        {
            Report("TRM3011", "Private field cannot be accessed outside its declaring struct.", span);
        }
    }

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Unsupported literal contexts are diagnosed before code emission.")]
    private BoundConstant BindLiteral(SyntaxToken token, PrimitiveType? expected)
    {
        if (token.Kind is TokenKind.TrueKeyword or TokenKind.FalseKeyword)
        {
            return new BoundConstant(token.Kind == TokenKind.TrueKeyword ? 1 : 0, PrimitiveType.Bool, token.Span);
        }

        if (token.Kind != TokenKind.NumericLiteral)
        {
            Report("TRM3001", "Only boolean and int32 literals are supported.", token.Span);
            return ErrorExpression(token.Span);
        }

        var text = _tree.Source.GetSubText(token.Span);
        var suffix = text.EndsWith("i32", StringComparison.Ordinal) ? PrimitiveType.Int32
            : text.EndsWith("u32", StringComparison.Ordinal) ? PrimitiveType.UInt32 : (PrimitiveType?)null;
        var digits = suffix is not null ? text.AsSpan(0, text.Length - 3) : text.AsSpan();

        if (digits.IndexOfAny('.', 'e', 'E') >= 0 || digits.IsEmpty || !AllDigits(digits))
        {
            Report("TRM3013", "Floating-point literals and numeric tags other than i32 are not supported.", token.Span);
            return ErrorExpression(token.Span);
        }

        var type = suffix ?? expected;
        if (suffix is null && type is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Char16))
        {
            Report("TRM3014", "An unsuffixed integer literal requires a numeric context.", token.Span);
        }

        var max = type switch {
            PrimitiveType.UInt32 => uint.MaxValue,
            PrimitiveType.Char16 => ushort.MaxValue,
            _ => (uint)int.MaxValue,
        };
        if (!ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed > max)
        {
            Report("TRM3013", "Integer literal is outside its destination range.", token.Span);
            return ErrorExpression(token.Span);
        }

        return new BoundConstant(unchecked((int)parsed), type ?? PrimitiveType.Int32, token.Span);
    }

    private bool TryMinimumLiteral(SyntaxToken token)
    {
        if (token.Kind != TokenKind.NumericLiteral)
        {
            return false;
        }

        var text = _tree.Source.GetSubText(token.Span);
        var digits = text.EndsWith("i32", StringComparison.Ordinal) ? text.AsSpan(0, text.Length - 3) : text.AsSpan();
        if (digits.IsEmpty || !AllDigits(digits) ||
            !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value != 2147483648L)
        {
            return false;
        }

        return true;
    }

    private static bool AllDigits(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Only expressions supported by this profile contribute a known type.")]
    [SuppressMessage("Style", "IDE0072:Add missing cases", Justification = "Unsupported token kinds have no known type.")]
    private PrimitiveType? KnownType(SyntaxNode node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ParenthesizedExpression:
                return KnownType((SyntaxNode)node.Children[1]);
            case SyntaxKind.VoidExpression:
                return PrimitiveType.Void;
            case SyntaxKind.NameExpression:
            {
                var name = ((SyntaxTokenElement)node.Children[0]).Token.Text;
                if (TryFindVariable(name, out var slot))
                {
                    return _variables[slot].Type;
                }
                return _currentStruct is not null && TryFindField(ResolveStruct(_currentStruct), name, out var field)
                    ? GetField(ResolveStruct(_currentStruct), field).Type : null;
            }
            case SyntaxKind.LiteralExpression:
            {
                var token = ((SyntaxTokenElement)node.Children[0]).Token;
                return token.Kind switch {
                    TokenKind.TrueKeyword or TokenKind.FalseKeyword => PrimitiveType.Bool,
                    TokenKind.NumericLiteral when token.Text.EndsWith("i32", StringComparison.Ordinal) => PrimitiveType.Int32,
                    TokenKind.NumericLiteral when token.Text.EndsWith("u32", StringComparison.Ordinal) => PrimitiveType.UInt32,
                    _ => null,
                };
            }
            case SyntaxKind.InvocationExpression:
            {
                var callee = (SyntaxNode)node.Children[0];
                if (callee.Kind == SyntaxKind.NameExpression)
                {
                    var name = ((SyntaxTokenElement)callee.Children[0]).Token.Text;
                    if (TryFindFunction(name, out var index) && !TryFindVariable(name, out _))
                    {
                        return _signatures[index].ReturnType;
                    }
                }

                if (callee.Kind == SyntaxKind.MemberAccessExpression)
                {
                    var left = (SyntaxNode)callee.Children[0];
                    var member = ((SyntaxTokenElement)callee.Children[2]).Token.Text;
                    var receiverType = KnownType(left);
                    var name = receiverType is PrimitiveType type && IsStruct(type)
                        ? _structs[(int)type - 100].Name + "." + member
                        : NameText(callee);
                    return TryFindFunction(name, out var method) ? _signatures[method].ReturnType : null;
                }
                return null;
            }
            case SyntaxKind.ObjectCreationExpression:
                return TryResolveStruct(NameText((SyntaxNode)node.Children[1]), out var created)
                    ? created : null;
            case SyntaxKind.CastExpression:
                return ((SyntaxTokenElement)node.Children[1]).Token.Text switch {
                    "int32" => PrimitiveType.Int32,
                    "uint32" => PrimitiveType.UInt32,
                    _ => PrimitiveType.Char16,
                };
            case SyntaxKind.MemberAccessExpression:
            {
                if (((SyntaxTokenElement)node.Children[2]).Token.Text == "Length"
                    && TryFindBuffer((SyntaxNode)node.Children[0], out _))
                {
                    return PrimitiveType.Int32;
                }
                var receiver = (SyntaxNode)node.Children[0];
                var member = ((SyntaxTokenElement)node.Children[2]).Token.Text;
                var receiverType = KnownType(receiver);
                if (receiverType is PrimitiveType type && TryFindField(type, member, out var field))
                {
                    return GetField(type, field).Type;
                }
                var text = NameText(node);
                if (text is "int32.MinValue" or "int32.MaxValue")
                {
                    return PrimitiveType.Int32;
                }
                if (text is "uint32.MinValue" or "uint32.MaxValue")
                {
                    return PrimitiveType.UInt32;
                }
                if (text is "char16.MinValue" or "char16.MaxValue")
                {
                    return PrimitiveType.Char16;
                }
                return null;
            }
            case SyntaxKind.IndexExpression:
                return TryFindBuffer((SyntaxNode)node.Children[0], out var buffer)
                    ? _buffers[buffer].ElementType : null;
            case SyntaxKind.UnaryExpression:
                return ((SyntaxTokenElement)node.Children[0]).Token.Kind == TokenKind.Bang
                    ? PrimitiveType.Bool : KnownType((SyntaxNode)node.Children[1]);
            case SyntaxKind.BinaryExpression:
            {
                var op = ((SyntaxTokenElement)node.Children[1]).Token.Kind;
                if (op is TokenKind.EqualsEquals or TokenKind.BangEquals or TokenKind.Less or TokenKind.LessEquals
                    or TokenKind.Greater or TokenKind.GreaterEquals or TokenKind.AmpersandAmpersand or TokenKind.PipePipe)
                {
                    return PrimitiveType.Bool;
                }

                return KnownType((SyntaxNode)node.Children[0]) ?? KnownType((SyntaxNode)node.Children[2]);
            }
            default:
                return null;
        }
    }

    private int AddVariable(string name, PrimitiveType type, bool isMutable, TextSpan span)
    {
        foreach (var scope in _scopes)
        {
            if (scope.ContainsKey(name))
            {
                Report("TRM3004", $"Variable '{name}' already exists in an enclosing scope; shadowing is not supported.", span);
                break;
            }
        }

        var slot = _variables.Count;
        _variables.Add(new BoundVariable(name, type, isMutable, span));
        _scopes[^1][name] = slot;
        return slot;
    }

    private bool TryFindVariable(string name, out int slot)
    {
        for (var index = _scopes.Count - 1; index >= 0; index--)
        {
            if (_scopes[index].TryGetValue(name, out slot))
            {
                return true;
            }
        }

        slot = -1;
        return false;
    }

    private bool TryFindFunction(string name, out int function)
    {
        if (_functions.TryGetValue(name, out function))
        {
            return true;
        }
        if (!name.Contains('.', StringComparison.Ordinal) && _currentStruct is not null && _functions.TryGetValue(
            (string.IsNullOrEmpty(_namespace) ? "" : _namespace + ".") + _currentStruct + "." + name, out function))
        {
            return true;
        }
        if (_functions.TryGetValue(_namespace + "." + name, out function))
        {
            return true;
        }
        var parent = _namespace;
        while (parent.Contains('.', StringComparison.Ordinal))
        {
            var separator = parent.LastIndexOf('.', StringComparison.Ordinal);
            parent = parent[..separator];
            if (_functions.TryGetValue(parent + "." + name, out function))
            {
                return true;
            }
        }
        foreach (var imported in _usings)
        {
            if (_functions.TryGetValue(imported + "." + name, out function))
            {
                return true;
            }
        }
        function = -1;
        return false;
    }

    private static bool AlwaysReturns(BoundStatement statement)
    {
        switch (statement)
        {
            case BoundReturn:
                return true;
            case BoundIf conditional:
                return conditional.Else is not null && AlwaysReturns(conditional.Then) && AlwaysReturns(conditional.Else);
            case BoundBlock block:
                foreach (var child in block.Statements)
                {
                    if (AlwaysReturns(child))
                    {
                        return true;
                    }
                }

                return false;
            default:
                // A loop is not presumed to execute or terminate.
                return false;
        }
    }

    private static BoundConstant ErrorExpression(TextSpan span) => new(0, PrimitiveType.Int32, span);

    private string TypeName(PrimitiveType type) => type switch {
        PrimitiveType.Int32 => "int32",
        PrimitiveType.UInt32 => "uint32",
        PrimitiveType.Char16 => "char16",
        PrimitiveType.Bool => "bool",
        PrimitiveType.Void => "void",
        _ when IsStruct(type) => _structs[(int)type - 100].Name,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private void Report(string code, string message, TextSpan span)
    {
        var diagnostic = new Diagnostic(code, DiagnosticSeverity.Error, message, span);
        _diagnostics.Add(diagnostic);
        _sourceDiagnostics.Add(new SourceDiagnostic(_tree, diagnostic));
    }

    private sealed record Parameter(string Name, PrimitiveType Type, TextSpan Span, bool IsOut);

    private sealed record Signature(string Name, PrimitiveType ReturnType, ImmutableArray<Parameter> Parameters,
        SyntaxNode? Body, SyntaxNode? Declaration, SyntaxTree Tree, string? StructName, bool IsConstructor, bool IsPrivate,
        bool IsInternal, bool IsMutable, bool IsStatic, BoundFunction? External = null);
}
