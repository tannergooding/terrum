// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private const int MaximumSemanticDepth = 128;
    private SyntaxTree _tree;
    private readonly ImmutableArray<SyntaxTree> _trees;
    private readonly Dictionary<SyntaxTree, (string Namespace, ImmutableArray<string> Usings)> _contexts = [];
    private readonly Dictionary<SyntaxNode, (string Namespace, ImmutableArray<string> Usings)> _nodeContexts =
        new(ReferenceEqualityComparer.Instance);
    private readonly List<(SyntaxTree Tree, SyntaxNode Declaration)> _scopedDeclarations = [];
    private readonly CancellationToken _cancellationToken;
    private List<Diagnostic>? _diagnostics;
    private List<SourceDiagnostic>? _sourceDiagnostics;
    private readonly List<Signature> _signatures = [];
    private readonly List<NativeFunctionPointerSignatureDefinition> _nativeFunctionPointerSignatures = [];
    private readonly List<NativePointerTypeDefinition> _nativePointerTypes = [];
    private readonly HashSet<BoundLocal> _constRequired = new(ReferenceEqualityComparer.Instance);
    private readonly List<BoundStruct> _structs = [];
    private readonly Dictionary<PrimitiveType, int> _structureAlignments = [];
    private readonly Dictionary<int, (SyntaxTree Tree, SyntaxNode Declaration)> _structDeclarations = [];
    private readonly HashSet<int> _layingOut = [];
    private readonly HashSet<int> _laidOut = [];
    private readonly Dictionary<string, PrimitiveType> _structNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GenericStructTemplate> _genericStructTemplates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PrimitiveType> _genericStructSpecializations = new(StringComparer.Ordinal);
    private readonly HashSet<PrimitiveType> _genericStructSpecializationTypes = [];
    private readonly Dictionary<string, AliasDefinition> _aliases = new(StringComparer.Ordinal);
    private readonly List<AliasDefinition> _aliasResolutionStack = [];
    private readonly List<IReadOnlyDictionary<string, PrimitiveType>> _aliasTypeArguments = [];
    private readonly ImmutableArray<HostBuffer> _buffers;
    private readonly Dictionary<string, int> _functions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usings = new(StringComparer.Ordinal);
    private string _namespace = "";
    private readonly List<BoundVariable> _variables = [];
    private readonly List<string> _nativeUtf16ZLiterals = [];
    private readonly Dictionary<string, int> _nativeUtf16ZLiteralIndexes = new(StringComparer.Ordinal);
    private readonly List<string> _nativeUtf8ZLiterals = [];
    private readonly Dictionary<string, int> _nativeUtf8ZLiteralIndexes = new(StringComparer.Ordinal);
    private long _nativeStringZLiteralBytes;
    private readonly HashSet<int> _unresolvedVacantLocals = [];
    private readonly HashSet<int> _constLocals = [];
    private readonly Dictionary<int, BoundConstant> _constScalarValues = [];
    private int _localStart;
    private readonly List<Dictionary<string, int>> _scopes = [];
    private readonly List<LoopBinding> _loops = [];
    private readonly List<SwitchBinding> _switches = [];
    private ImmutableDictionary<string, PrimitiveType>? _genericOwnerTypeArguments;
    private int _finallyLoopDepth;
    private GenericInstantiation? _genericContext;
    private PrimitiveType _returnType;
    private string? _currentStruct;
    private bool _constructor;
    private int _unsafeDepth;
    private bool _hasReceiver;
    private readonly bool _isLibrary;
    private readonly bool _coreLibrary;
    private readonly HashSet<string> _primitiveDeclarations = new(StringComparer.Ordinal);
    private readonly HashSet<SyntaxNode> _acceptedPrimitiveDeclarations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, string> _primitiveBackingFields = new(StringComparer.Ordinal);
    private readonly CompilationReference? _reference;
    private readonly BoundRuntimePolicy _runtimePolicy;
    private readonly SourceArithmeticProfile _sourceArithmeticProfile;
    private bool _usesNativeBuffer;
    private bool _usesManagedNode;

    private sealed class LoopBinding
    {
        internal string? Name { get; init; }
        internal bool HasContinue { get; set; }
        internal bool IsSwitchDispatcher { get; init; }
    }

    private readonly record struct SwitchTarget(BoundConstant Value, int ArmOrdinal);

    private sealed record SwitchBinding(int StateVariable, int DispatchLoopIndex,
        PrimitiveType SelectorType, ImmutableArray<SwitchTarget> Targets, int DefaultArm);

    private sealed record SwitchArmBinding(SyntaxNode Syntax, SyntaxNode BodySyntax,
        BoundConstant? Value, bool IsWildcard, BoundExpression? Guard, int Ordinal, bool IsFallback,
        BoundExpression? PatternTest, int PatternVariable, string? PatternName);

    private readonly record struct BindingContext(
        SyntaxTree Tree, string Namespace, ImmutableArray<string> Usings);

    private sealed class AliasDefinition(SyntaxTree tree, SyntaxNode declaration, SyntaxNode target,
        ImmutableArray<string> typeParameters, string qualifiedName, bool isPublic)
    {
        internal SyntaxTree Tree { get; } = tree;
        internal SyntaxNode Declaration { get; } = declaration;
        internal SyntaxNode Target { get; } = target;
        internal ImmutableArray<string> TypeParameters { get; } = typeParameters;
        internal string QualifiedName { get; } = qualifiedName;
        internal bool IsPublic { get; } = isPublic;
        internal bool IsResolving { get; set; }
        internal bool IsResolved { get; set; }
        internal bool IsInvalid { get; set; }
        internal PrimitiveType Type { get; set; }
    }

    private sealed record GenericStructTemplate(string Name, string QualifiedName,
        ImmutableArray<string> TypeParameters, SyntaxTree Tree, SyntaxNode Declaration);

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
        bool isLibrary = false, CompilationReference? reference = null,
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast, bool coreLibrary = false,
        SourceArithmeticProfile sourceArithmeticProfile = SourceArithmeticProfile.Development)
    {
        if (coreLibrary && (!isLibrary || reference is not null))
        {
            throw new ArgumentException("Core declarations require library compilation without a reference.");
        }
        _tree = trees[0];
        _trees = trees;
        _buffers = buffers;
        _cancellationToken = cancellationToken;
        _isLibrary = isLibrary;
        _coreLibrary = coreLibrary;
        _reference = reference;
        _runtimePolicy = runtimePolicy;
        _sourceArithmeticProfile = sourceArithmeticProfile;
    }

    private BoundIntegerOverflowBehavior SourceOverflowBehavior(TokenKind operation, PrimitiveType type,
        bool unary = false)
        => ResolveSourceOverflowBehavior(_sourceArithmeticProfile, operation, type, unary);

    internal static BoundIntegerOverflowBehavior ResolveSourceOverflowBehavior(
        SourceArithmeticProfile sourceArithmeticProfile, TokenKind operation, PrimitiveType type, bool unary = false)
    {
        if (sourceArithmeticProfile != SourceArithmeticProfile.Release
            || !PrimitiveTypeFacts.IsIntegerOrWide(type))
        {
            return BoundIntegerOverflowBehavior.Checked;
        }
        var arithmeticOperator = operation switch {
            TokenKind.PlusPlus => TokenKind.Plus,
            TokenKind.MinusMinus => TokenKind.Minus,
            _ => operation,
        };
        var profileAffectsOperation = arithmeticOperator is TokenKind.Plus or TokenKind.Minus or TokenKind.Star;
        return profileAffectsOperation && (!unary || arithmeticOperator == TokenKind.Minus)
            ? BoundIntegerOverflowBehavior.Wrapping : BoundIntegerOverflowBehavior.Checked;
    }

    internal IReadOnlyList<Diagnostic> Diagnostics => _diagnostics is not null
        ? _diagnostics : Array.Empty<Diagnostic>();
    internal IReadOnlyList<SourceDiagnostic> SourceDiagnostics => _sourceDiagnostics is not null
        ? _sourceDiagnostics : Array.Empty<SourceDiagnostic>();
    private int DiagnosticCount => _diagnostics?.Count ?? 0;

    private void SetContext(SyntaxTree tree)
    {
        _tree = tree;
        var (namespaceName, usings) = _contexts[tree];
        SetContext(tree, namespaceName, usings);
    }

    private void SetContext(SyntaxTree tree, SyntaxNode? declaration)
    {
        var (namespaceName, usings) = declaration is not null
            && _nodeContexts.TryGetValue(declaration, out var scopedContext)
            ? scopedContext : _contexts[tree];
        SetContext(tree, namespaceName, usings);
    }

    private void SetContext(SyntaxTree tree, string namespaceName, ImmutableArray<string> usings)
    {
        _tree = tree;
        _namespace = namespaceName;
        _usings.Clear();
        foreach (var imported in usings)
        {
            _ = _usings.Add(imported);
        }
    }

    private BindingContext CaptureContext()
        => new(_tree, _namespace, [.. _usings]);

    private void RestoreContext(BindingContext context)
        => SetContext(context.Tree, context.Namespace, context.Usings);

    private void CollectTreeContexts(SyntaxTree tree)
    {
        var namespaceName = "";
        var usings = ImmutableArray.CreateBuilder<string>();
        foreach (var child in tree.Root.Children.OfType<SyntaxNode>())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            switch (child.Kind)
            {
                case SyntaxKind.UsingDirective:
                    usings.Add(DirectiveName(child));
                    break;
                case SyntaxKind.NamespaceDeclaration:
                    namespaceName = DirectiveName(child);
                    _nodeContexts[child] = (namespaceName, usings.ToImmutable());
                    break;
                case SyntaxKind.NamespaceBlockDeclaration:
                    CollectNamespaceBlock(tree, child, namespaceName, usings.ToImmutable());
                    break;
                default:
                    AddScopedDeclaration(tree, child, namespaceName, usings.ToImmutable());
                    break;
            }
        }
        _contexts.Add(tree, (namespaceName, usings.ToImmutable()));
    }

    private void CollectNamespaceBlock(SyntaxTree tree, SyntaxNode namespaceBlock,
        string parentNamespace, ImmutableArray<string> inheritedUsings)
    {
        var namespaceName = QualifyNamespace(parentNamespace, DirectiveName(namespaceBlock));
        var usings = inheritedUsings.ToBuilder();
        foreach (var child in namespaceBlock.Children.OfType<SyntaxNode>())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            switch (child.Kind)
            {
                case SyntaxKind.UsingDirective:
                    usings.Add(DirectiveName(child));
                    break;
                case SyntaxKind.NamespaceBlockDeclaration:
                    CollectNamespaceBlock(tree, child, namespaceName, usings.ToImmutable());
                    break;
                case SyntaxKind.NamespaceDeclaration:
                    _nodeContexts[child] = (namespaceName, usings.ToImmutable());
                    break;
                default:
                    AddScopedDeclaration(tree, child, namespaceName, usings.ToImmutable());
                    break;
            }
        }
    }

    private void AddScopedDeclaration(SyntaxTree tree, SyntaxNode declaration,
        string namespaceName, ImmutableArray<string> usings)
    {
        var context = (namespaceName, usings);
        var pending = new Stack<SyntaxNode>();
        pending.Push(declaration);
        while (pending.TryPop(out var node))
        {
            _nodeContexts[node] = context;
            foreach (var child in node.Children.OfType<SyntaxNode>())
            {
                pending.Push(child);
            }
        }
        _scopedDeclarations.Add((tree, declaration));
    }

    private static string QualifyNamespace(string parent, string name)
        => string.IsNullOrEmpty(parent) ? name : parent + "." + name;

    internal BoundProgram? Bind()
    {
        if (_reference is not null && _reference.RuntimePolicy != _runtimePolicy)
        {
            Report("TRM3022", "The referenced module requires a different checked-failure runtime policy.", _trees[0].Root.Span);
            return null;
        }
        _usesNativeBuffer = _reference?.Functions.Any(static function =>
            function.ReturnType == PrimitiveType.OwnedInt32Buffer
            || function.Variables.Any(static variable => variable.Type == PrimitiveType.OwnedInt32Buffer)) == true
            || _reference?.Structures.Any(static structure =>
                structure.Fields.Any(static field => field.Type == PrimitiveType.OwnedInt32Buffer)) == true;
        // Parser depth limits do not bound left-associated flat expression trees.
        var pending = new Stack<(SyntaxTree Tree, SyntaxNode Node, int Depth)>();
        if (_reference is not null)
        {
            _nativeFunctionPointerSignatures.AddRange(_reference.NativeFunctionPointerSignatures);
            _nativePointerTypes.AddRange(_reference.NativePointerTypes);
            foreach (var structure in _reference.Structures)
            {
                _structNames.Add(structure.Name, (PrimitiveType)(100 + _structs.Count));
                _structs.Add(structure);
                _ = _laidOut.Add(_structs.Count - 1);
            }
            foreach (var function in _reference.Functions)
            {
                _functions.Add(function.Name, _signatures.Count);
                var parameters = ImmutableArray.CreateBuilder<Parameter>();
                var offset = function.IsConstructor || (function.DeclaringStruct is not null && !function.IsStatic) ? 1 : 0;
                for (var index = offset; index < function.Variables.Length; index++)
                {
                    var parameter = function.Variables[index];
                    parameters.Add(new Parameter(parameter.Name, parameter.Type, default, parameter.IsOut,
                        parameter.IsInit, parameter.IsImplicitRef));
                }
                _signatures.Add(new Signature(function.Name, function.ReturnType, parameters.ToImmutable(),
                    null, null, _trees[0], function.DeclaringStruct, function.IsConstructor,
                    false, false, false, function.IsStatic, function.IsNoThrow, function.DoesNotReturn, function) {
                    IsCleanup = function.IsCleanup,
                    IsOperator = function.IsOperator,
                    PropertyName = function.PropertyName,
                    RequiresUnsafeCaller = function.RequiresUnsafeCaller,
                    NativeFunctionPointerType = function.NativeFunctionPointerType,
                });
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

            var namesBuffer = item.Node.Kind == SyntaxKind.TypeName
                && NameText(item.Node) == NativeBufferIntrinsics.TypeName;
            var callsBuffer = item.Node.Kind == SyntaxKind.MemberAccessExpression
                    && item.Node.Children[0] is SyntaxNode receiver
                    && receiver.Kind == SyntaxKind.NameExpression
                    && NameText(receiver) == NativeBufferIntrinsics.TypeName;
            if (namesBuffer || callsBuffer)
            {
                _usesNativeBuffer = true;
            }
            if ((item.Node.Kind == SyntaxKind.TypeName
                    && NameText(item.Node) is NativeManagedNodeIntrinsics.TypeName or "ManagedNode?"
                        or "writeManagedNode" or "writeManagedNode?")
                || (item.Node.Kind == SyntaxKind.MemberAccessExpression
                    && item.Node.Children[0] is SyntaxNode managedReceiver
                    && managedReceiver.Kind == SyntaxKind.NameExpression
                    && NameText(managedReceiver) is NativeManagedNodeIntrinsics.TypeName
                        or NativeManagedNodeIntrinsics.HeapName))
            {
                _usesManagedNode = true;
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
            CollectTreeContexts(tree);
        }
        DeclareProtocols();
        foreach (var (tree, child) in _scopedDeclarations)
        {
            SetContext(tree, child);
            if (child.Kind == SyntaxKind.ClassDeclaration)
            {
                DeclareClassName(tree, child);
            }
            if (child.Kind == SyntaxKind.StructDeclaration)
            {
                var structure = child;
                if (HasGenericTypeParameters(structure))
                {
                    DeclareGenericStructTemplate(tree, structure);
                    continue;
                }
                var name = StructName(structure).Name;
                var qualified = string.IsNullOrEmpty(_namespace) ? name : _namespace + "." + name;
                if (TryPrimitiveType(name, out var primitive))
                {
                    if (!_coreLibrary || _namespace.Length != 0)
                    {
                        Report("TRM3003", $"The primitive type '{name}' can only be declared by the Core library.", structure.Span);
                    }
                    else if (!_primitiveDeclarations.Add(name))
                    {
                        Report("TRM3003", $"Duplicate primitive declaration '{name}'.", structure.Span);
                    }
                    else
                    {
                        _ = _acceptedPrimitiveDeclarations.Add(structure);
                        _structNames.Add(name, primitive);
                        ValidatePrimitiveDeclaration(structure, name);
                    }
                    continue;
                }
                if (qualified == DirectNativeImportContract.AttributeFullyQualifiedName
                    || name == "Span" || qualified is NativeBufferIntrinsics.TypeName or "ReadonlyInt32BufferView"
                    or "MutableInt32BufferView"
                    || name is NativeManagedNodeIntrinsics.TypeName or NativeManagedNodeIntrinsics.HeapName)
                {
                    Report("TRM3003", "A compiler-owned native buffer or view type cannot be redeclared.", structure.Span);
                    continue;
                }
                var index = _structs.Count;
                if (index >= SemanticVerifier.MaximumVariables)
                {
                    Report("TRM3016", "Source exceeds the supported number of value layouts.", structure.Span);
                    return null;
                }
                if (_classNames.ContainsKey(qualified) || _genericStructTemplates.ContainsKey(qualified)
                    || !_structNames.TryAdd(qualified, (PrimitiveType)(100 + index)))
                {
                    Report("TRM3003", $"Duplicate struct '{name}'.", structure.Span);
                    continue;
                }
                var isPublic = structure.Children.OfType<SyntaxTokenElement>()
                    .Any(static element => element.Token.Kind == TokenKind.PublicKeyword);
                _structs.Add(new BoundStruct(qualified, [], 0) {
                    IsInternal = !isPublic,
                });
                _structDeclarations.Add(index, (tree, structure));
            }
        }

        if (_usesNativeBuffer)
        {
            _ = _structNames.TryAdd(NativeBufferIntrinsics.TypeName, PrimitiveType.OwnedInt32Buffer);
        }
        _ = _structNames.TryAdd("ReadonlyInt32BufferView", PrimitiveType.ReadonlyInt32BufferView);
        _ = _structNames.TryAdd("MutableInt32BufferView", PrimitiveType.MutableInt32BufferView);
        if (_usesManagedNode)
        {
            _ = _structNames.TryAdd(NativeManagedNodeIntrinsics.TypeName, PrimitiveType.ManagedNode);
        }
        DeclareAliases();
        foreach (var index in _structDeclarations.Keys.ToArray())
        {
            DeclareStruct(index);
        }
        foreach (var index in _classDeclarations.Keys)
        {
            DeclareClass(index);
        }
        DeclareConformances();
        foreach (var (tree, node) in _scopedDeclarations)
        {
            SetContext(tree, node);
            if (node.Kind is SyntaxKind.UsingDirective or SyntaxKind.NamespaceDeclaration
                or SyntaxKind.NamespaceBlockDeclaration)
            {
                continue;
            }
            if (node.Kind is SyntaxKind.StructDeclaration or SyntaxKind.ClassDeclaration)
            {
                if (HasGenericTypeParameters(node))
                {
                    continue;
                }
                var structName = StructName(node).Name;
                if (TryPrimitiveType(structName, out _)
                    && (node.Kind != SyntaxKind.StructDeclaration
                        || !_acceptedPrimitiveDeclarations.Contains(node)))
                {
                    continue;
                }
                foreach (var member in node.Children)
                {
                    if (member is SyntaxNode declaration && declaration.Kind is SyntaxKind.FunctionDeclaration
                        or SyntaxKind.ConstructorDeclaration or SyntaxKind.OperatorDeclaration
                        or SyntaxKind.PropertyDeclaration)
                    {
                        if (TryPrimitiveType(structName, out _)
                            && declaration.Kind is SyntaxKind.ConstructorDeclaration
                                or SyntaxKind.OperatorDeclaration)
                        {
                            Report("TRM3001", "Core primitive declarations cannot define constructors or operators.",
                                declaration.Span);
                            continue;
                        }
                        Declare(declaration, structName);
                    }
                }
                continue;
            }
            if (node.Kind is SyntaxKind.InterfaceDeclaration or SyntaxKind.AliasDeclaration)
            {
                continue;
            }
            if (node.Kind != SyntaxKind.FunctionDeclaration)
            {
                Report("TRM3001", "Only top-level function declarations are supported.", node.Span);
                continue;
            }
            if (node.Children.OfType<SyntaxNode>()
                .FirstOrDefault(static child => child.Kind == SyntaxKind.TypeParameterList)
                is SyntaxNode typeParameters)
            {
                DeclareGeneric(node, typeParameters);
            }
            else
            {
                Declare(node, null);
            }
        }

        if (_usesNativeBuffer)
        {
            foreach (var intrinsic in NativeBufferIntrinsics.Functions)
            {
                if (!_functions.TryAdd(intrinsic.Name, _signatures.Count))
                {
                    Report("TRM3003", $"The compiler-owned operation '{intrinsic.Name}' cannot be redeclared.", _trees[0].Root.Span);
                    return null;
                }
                var offset = intrinsic.IsStatic ? 0 : 1;
                var parameters = intrinsic.Variables.Skip(offset)
                    .Select(static variable => new Parameter(variable.Name, variable.Type, default, variable.IsOut));
                _signatures.Add(new Signature(intrinsic.Name, intrinsic.ReturnType, [.. parameters],
                    null, null, _trees[0], NativeBufferIntrinsics.TypeName, false,
                    false, true, intrinsic.Variables[0].IsMutable && !intrinsic.IsStatic,
                    intrinsic.IsStatic, intrinsic.IsNoThrow, false, intrinsic));
            }
        }
        if (_usesManagedNode)
        {
            foreach (var intrinsic in NativeManagedNodeIntrinsics.Functions)
            {
                if (!_functions.TryAdd(intrinsic.Name, _signatures.Count))
                {
                    Report("TRM3003", $"The compiler-owned operation '{intrinsic.Name}' cannot be redeclared.", _trees[0].Root.Span);
                    return null;
                }
                var offset = intrinsic.IsStatic ? 0 : 1;
                var parameters = intrinsic.Variables.Skip(offset)
                    .Select(static variable => new Parameter(variable.Name, variable.Type, default, variable.IsOut));
                _signatures.Add(new Signature(intrinsic.Name, intrinsic.ReturnType, [.. parameters],
                    null, null, _trees[0], intrinsic.DeclaringStruct, false,
                    false, true, false, intrinsic.IsStatic, intrinsic.IsNoThrow, false, intrinsic));
            }
        }
        DeclareClassFactories();
        foreach (var template in _templates.Values)
        {
            if (_functions.ContainsKey(template.Name))
            {
                SetContext(template.Tree, template.Declaration);
                Report("TRM3003", $"Generic and concrete functions cannot share '{template.Name}'.",
                    template.Declaration.Span);
            }
        }
        ValidateConformances();
        if (DiagnosticCount == 0 && !ValidateGenericTemplates())
        {
            return null;
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
                    SetContext(signature.Tree, declaration);
                    Report("TRM3002", "Multiple parameterless Main functions make the entry point ambiguous.", declaration.Span);
                }
                else
                {
                    entryPoint = index;
                    if (signature.RequiresUnsafeCaller)
                    {
                        SetContext(signature.Tree, declaration);
                        Report("TRM3028", "An entry point cannot require an unsafe caller.", declaration.Span);
                    }
                }
            }
        }

        if (!_isLibrary && entryPoint < 0)
        {
            Report("TRM3002", "A parameterless int32 or void Main function is required.", _tree.Root.Span);
        }

        var bound = ImmutableArray.CreateBuilder<BoundFunction>(_signatures.Count);
        for (var signatureIndex = 0; signatureIndex < _signatures.Count; signatureIndex++)
        {
            var signature = _signatures[signatureIndex];
            _cancellationToken.ThrowIfCancellationRequested();
            if (signature.External is BoundFunction external)
            {
                bound.Add(external);
                continue;
            }
            if (signature.ClassConstructor >= 0)
            {
                bound.Add(CreateClassFactory(signature));
                continue;
            }
            var declaration = signature.Declaration ?? throw new InvalidOperationException("Source declaration is missing.");
            var syntaxBody = signature.Body ?? throw new InvalidOperationException("Source body is missing.");
            SetContext(signature.Tree, declaration);
            _variables.Clear();
            _scopes.Clear();
            _unresolvedVacantLocals.Clear();
            _constLocals.Clear();
            _constScalarValues.Clear();
            _scopes.Add(new Dictionary<string, int>(StringComparer.Ordinal));
            _returnType = signature.ReturnType;
            _currentStruct = signature.StructName;
            _constructor = signature.IsConstructor || signature.IsClassConstructor;
            _hasReceiver = signature.StructName is not null && !signature.IsStatic;

            if (signature.StructName is not null && _hasReceiver)
            {
                var receiverType = ResolveDeclaredType(signature.StructName);
                if (IsClass(receiverType)
                    && _classes[ManagedClassTypes.ClassIndex(receiverType)].IsMutable
                    && (signature.IsMutable || signature.IsClassConstructor))
                {
                    receiverType = ManagedClassTypes.Writable(receiverType);
                }
                _ = AddVariable("this", receiverType,
                    signature.IsConstructor || signature.IsClassConstructor
                        || (signature.IsMutable && !IsClass(receiverType)), declaration.Span);
            }

            foreach (var parameter in signature.Parameters)
            {
                var slot = AddVariable(parameter.Name, parameter.Type, parameter.IsOut,
                    parameter.Span, parameter.IsEscapedThis);
                if (parameter.IsOut || parameter.IsImplicitRef)
                {
                    _variables[slot] = _variables[slot] with {
                        IsOut = parameter.IsOut,
                        IsInit = parameter.IsInit,
                        IsImplicitRef = parameter.IsImplicitRef,
                    };
                }
            }

            _localStart = _variables.Count;
            if (signature.GenericOwnerTypeArguments is { } ownerTypeArguments)
            {
                _aliasTypeArguments.Add(ownerTypeArguments);
            }
            BoundBlock body;
            try
            {
                body = signature.Generic is GenericInstantiation generic
                    ? BindGenericBody(generic) : BindCallableBody(syntaxBody);
            }
            finally
            {
                if (signature.GenericOwnerTypeArguments is not null)
                {
                    _aliasTypeArguments.RemoveAt(_aliasTypeArguments.Count - 1);
                }
            }
            if (signature.IsConstructor && !signature.DoesNotReturn)
            {
                var end = new TextSpan(syntaxBody.Span.End, 0);
                body = body with {
                    Statements = body.Statements.Add(new BoundReturn(
                    new BoundRead(0, _returnType, end), end))
                };
                if (IsStruct(_returnType)
                    && _structs[(int)_returnType - 100].Fields.Any(field => IsUnique(field.Type)))
                {
                    var acquisitions = ConstructorAcquisitions(body, _returnType);
                    body = new BoundBlock([new BoundTryFinally(body,
                        new BoundBlock([new BoundDropConstructedFields(0, acquisitions, syntaxBody.Span)],
                            syntaxBody.Span), syntaxBody.Span)], syntaxBody.Span);
                }
            }
            else if (_returnType == PrimitiveType.Void && !signature.DoesNotReturn)
            {
                var end = new TextSpan(syntaxBody.Span.End, 0);
                body = body with { Statements = body.Statements.Add(new BoundReturn(null, end)) };
            }
            for (var parameter = signature.Parameters.Length - 1; parameter >= 0; parameter--)
            {
                if (!IsUnique(signature.Parameters[parameter].Type))
                {
                    continue;
                }
                var slot = parameter + (signature.IsConstructor ? 1 : 0)
                    + (signature.StructName is not null && !signature.IsStatic && !signature.IsConstructor ? 1 : 0);
                var method = CleanupMethod(signature.Parameters[parameter].Type);
                var cleanupFunction = method is not null && _functions.TryGetValue(method, out var found) ? found : -1;
                body = new BoundBlock([new BoundTryFinally(body,
                    new BoundBlock([new BoundDrop(slot, cleanupFunction, syntaxBody.Span) {
                        IsOutput = signature.Parameters[parameter].IsOut,
                    }], syntaxBody.Span),
                    syntaxBody.Span)], syntaxBody.Span);
            }
            bound.Add(new BoundFunction(signature.Name, signature.ReturnType, [.. _variables],
                signature.Parameters.Length + (signature.StructName is not null && !signature.IsStatic && !signature.IsConstructor ? 1 : 0),
                body, declaration.Span) {
                IsConstructor = signature.IsConstructor,
                IsPrivate = signature.IsPrivate,
                IsInternal = signature.IsInternal,
                IsStatic = signature.IsStatic,
                IsOperator = signature.IsOperator,
                IsNoThrow = signature.IsNoThrow,
                RequiresUnsafeCaller = signature.RequiresUnsafeCaller,
                NativeFunctionPointerType = signature.NativeFunctionPointerType,
                IsClassConstructor = signature.IsClassConstructor,
                IsCleanup = signature.IsCleanup,
                DoesNotReturn = signature.DoesNotReturn,
                DeclaringStruct = signature.StructName is null ? null
                   : (string.IsNullOrEmpty(_namespace) ? "" : _namespace + ".") + signature.StructName,
                PropertyName = signature.PropertyName,
                CapturedParameters = signature.CapturedParameters,
                ReturnsForwardedBacking = signature.ReturnsForwardedBacking,
                ResultDependencyParameters = signature.ResultDependencyParameters,
                ResultDependencyIncludesReceiver = signature.ResultDependencyIncludesReceiver,
                DependencyContracts = signature.DependencyContracts,
            });
        }

        if (DiagnosticCount != 0)
        {
            return null;
        }
        if (!ResolveRequiredConstants(bound))
        {
            return null;
        }
        var program = new BoundProgram(bound.ToImmutable(), entryPoint) {
            Structures = [.. _structs],
            NativeFunctionPointerSignatures = [.. _nativeFunctionPointerSignatures],
            NativePointerTypes = [.. _nativePointerTypes],
            Classes = [.. _classes],
            Buffers = _buffers,
            RequiresSourceGrant = _buffers.Any(static buffer => buffer.IsInvocationScoped)
                || _reference?.RequiresSourceGrant == true,
            InvocationProfileId = _buffers.FirstOrDefault(static buffer => buffer.InvocationProfileId is not null)
                ?.InvocationProfileId ?? _reference?.InvocationProfileId,
            NativeUtf16ZLiterals = [.. _nativeUtf16ZLiterals],
            NativeUtf8ZLiterals = [.. _nativeUtf8ZLiterals],
            RuntimePolicy = _runtimePolicy,
        };
        if (!SemanticVerifier.TryVerifySource(program, out var functionIndex, out var span, out var exceededWorkLimit,
            out var invalidEffect))
        {
            _tree = _signatures.Count == 0 ? _trees[0] : _signatures[functionIndex].Tree;
            var (code, message) = invalidEffect switch {
                "NoThrow" => ("TRM3017", "NoThrow declarations cannot perform potentially throwing operations or call functions without a NoThrow contract."),
                "NativeCallable" => ("TRM3030", "NativeCallable bodies must not allow Terrum failures to propagate across a native boundary."),
                "DoesNotReturn" => ("TRM3018", "DoesNotReturn declarations must have no reachable normal return or fallthrough."),
                "NormalReturn" => ("TRM3012", "Not all normally completing paths return a value."),
                "FinallyReturn" => ("TRM3019", "A finally block cannot return or transfer control out of cleanup."),
                "LoopTransfer" => ("TRM3008", "Loop transfer requires an enclosing loop."),
                "FinallyNoReturn" => ("TRM3020", "Potentially nonreturning finally cleanup is unsupported in this bounded profile."),
                "UnsafeRecovery" => ("TRM3021", "Checked failure recovery requires an unsafe block."),
                "CheckedPolicy" => ("TRM3022", "Recovery requires the checked-exceptions runtime policy."),
                "Ownership" => ("TRM3023", "A unique value must be moved from a live named owner; copies, vacant reads, and escaping receiver loans are not allowed."),
                "NativeImport" => ("TRM3030",
                    $"The source native import does not match its selected compiler-owned catalog in '{_signatures[functionIndex].Name}'."),
                _ => ("TRM3016", exceededWorkLimit
                    ? "Source exceeds the semantic verification work limit."
                    : $"Source violates definite assignment, initialization, or executable type rules. {invalidEffect}"),
            };
            Report(code, message, span);
            return null;
        }
        return program;
    }

    private static string DirectiveName(SyntaxNode directive)
    {
        var builder = new StringBuilder();
        for (var index = 1; index < directive.Children.Length; index++)
        {
            if (directive.Children[index] is not SyntaxTokenElement tokenElement
                || tokenElement.Token.Kind is TokenKind.OpenBrace or TokenKind.Semicolon)
            {
                break;
            }
            _ = builder.Append(tokenElement.Token.Name);
        }
        return builder.ToString();
    }

    private static string NameText(SyntaxNode node)
    {
        var builder = new StringBuilder();
        foreach (var token in node.DescendantTokens())
        {
            _ = builder.Append(token.Name);
        }
        return builder.ToString();
    }

    private static string AttributeName(SyntaxNode attribute)
    {
        var builder = new StringBuilder();
        var start = AttributeNameStart(attribute);
        for (var index = start; index < attribute.Children.Length; index++)
        {
            if (attribute.Children[index] is not SyntaxTokenElement token)
            {
                break;
            }
            if (token.Token.Kind == TokenKind.Identifier)
            {
                _ = builder.Append(token.Token.Name);
            }
            else if (token.Token.Kind == TokenKind.Dot)
            {
                _ = builder.Append('.');
            }
            else
            {
                break;
            }
        }
        return builder.ToString();
    }

    private static int AttributeNameStart(SyntaxNode attribute)
    {
        if (attribute.Children is [
            SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket },
            SyntaxTokenElement { Token.Kind: TokenKind.ReturnKeyword },
            SyntaxTokenElement { Token.Kind: TokenKind.Colon },
            ..
        ])
        {
            return 3;
        }
        if (attribute.Children is [
            SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket },
            (SyntaxTokenElement { Token.Kind: TokenKind.Identifier, Token.Text: "this" }
                or SyntaxTokenElement { Token.Name: "method" }),
            SyntaxTokenElement { Token.Kind: TokenKind.Colon },
            ..
        ])
        {
            return 3;
        }
        if (attribute.Children is [
            SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket },
            SyntaxTokenElement { Token.Name: "param" },
            SyntaxTokenElement { Token.Kind: TokenKind.OpenParen },
            SyntaxTokenElement,
            SyntaxTokenElement { Token.Kind: TokenKind.CloseParen },
            SyntaxTokenElement { Token.Kind: TokenKind.Colon },
            ..
        ])
        {
            return 6;
        }
        return 1;
    }

    private static bool TryDecodeDirectNativeImportAttribute(SyntaxNode attribute,
        out string libraryName, out string exportName)
    {
        libraryName = "";
        exportName = "";
        var children = attribute.Children;
        if (children.Length != 12
            || children[0] is not SyntaxTokenElement open || open.Token.Kind != TokenKind.OpenBracket
            || children[1] is not SyntaxTokenElement { Token.Name: "Terrum" }
            || children[2] is not SyntaxTokenElement { Token.Kind: TokenKind.Dot }
            || children[3] is not SyntaxTokenElement { Token.Name: "Interop" }
            || children[4] is not SyntaxTokenElement { Token.Kind: TokenKind.Dot }
            || children[5] is not SyntaxTokenElement { Token.Name: "NativeImport" }
            || children[6] is not SyntaxTokenElement { Token.Kind: TokenKind.OpenParen }
            || children[7] is not SyntaxTokenElement library
            || library.Token.Kind != TokenKind.StringLiteral
            || children[8] is not SyntaxTokenElement { Token.Kind: TokenKind.Comma }
            || children[9] is not SyntaxTokenElement export
            || export.Token.Kind != TokenKind.StringLiteral
            || children[10] is not SyntaxTokenElement { Token.Kind: TokenKind.CloseParen }
            || children[11] is not SyntaxTokenElement { Token.Kind: TokenKind.CloseBracket }
            || !StringLiteralDecoder.TryDecode(library.Token.Text.AsSpan(), out libraryName)
            || !StringLiteralDecoder.TryDecode(export.Token.Text.AsSpan(), out exportName))
        {
            return false;
        }

        return DirectNativeImportContract.IsValidLibraryName(libraryName)
            && DirectNativeImportContract.IsValidExportName(exportName);
    }

    private static bool TryGetParameterAttributeTarget(SyntaxNode attribute, out string parameterName)
    {
        parameterName = "";
        if (attribute.Children is not [
                SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket },
                SyntaxTokenElement { Token.Name: "param" },
                SyntaxTokenElement { Token.Kind: TokenKind.OpenParen },
                SyntaxTokenElement { Token.Kind: TokenKind.Identifier } parameter,
                SyntaxTokenElement { Token.Kind: TokenKind.CloseParen },
                SyntaxTokenElement { Token.Kind: TokenKind.Colon },
                ..
            ])
        {
            return false;
        }

        parameterName = parameter.Token.Name;
        return true;
    }

    private static DependencyAttributeTarget GetDependencyAttributeTarget(
        SyntaxNode attribute)
    {
        if (attribute.Children is [
            SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket },
            SyntaxTokenElement { Token.Kind: TokenKind.ReturnKeyword },
            SyntaxTokenElement { Token.Kind: TokenKind.Colon },
            ..
        ])
        {
            return new DependencyAttributeTarget(
                DependencyAttributeTargetKind.Return, null);
        }
        if (attribute.Children is [
            SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket },
            SyntaxTokenElement { Token.Kind: TokenKind.Identifier, Token.Text: "this" },
            SyntaxTokenElement { Token.Kind: TokenKind.Colon },
            ..
        ])
        {
            return new DependencyAttributeTarget(
                DependencyAttributeTargetKind.Receiver, null);
        }
        if (attribute.Children is [
            SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket },
            SyntaxTokenElement { Token.Name: "method" },
            SyntaxTokenElement { Token.Kind: TokenKind.Colon },
            ..
        ])
        {
            return new DependencyAttributeTarget(
                DependencyAttributeTargetKind.Operation, null);
        }
        return TryGetParameterAttributeTarget(attribute, out var parameterName)
            ? new DependencyAttributeTarget(
                DependencyAttributeTargetKind.Parameter, parameterName)
            : new DependencyAttributeTarget(DependencyAttributeTargetKind.Default, null);
    }

    private static bool TryGetDependencyRelation(string attributeName,
        out BoundDependencyRelation relation)
    {
        relation = attributeName switch
        {
            "DependsOn" or "Terrum.CodeAnalysis.DependsOn"
                => BoundDependencyRelation.DependsOn,
            "Captures" or "Terrum.CodeAnalysis.Captures"
                => BoundDependencyRelation.Captures,
            "ForwardsBacking" or "Terrum.CodeAnalysis.ForwardsBacking"
                => BoundDependencyRelation.ForwardsBacking,
            _ => 0,
        };
        return relation != 0;
    }

    private static bool TryDecodeDirectNativeOutputAttribute(SyntaxNode attribute,
        out bool nonNegative)
    {
        nonNegative = false;
        var children = attribute.Children.OfType<SyntaxTokenElement>().ToArray();
        var argumentStart = Array.FindLastIndex(children,
            static child => child.Token.Kind == TokenKind.OpenParen);
        if (argumentStart < 0 || argumentStart + 4 >= children.Length
            || children[argumentStart + 1].Token.Name != "When"
            || children[argumentStart + 2].Token.Kind != TokenKind.Colon
            || children[^2].Token.Kind != TokenKind.CloseParen
            || children[^1].Token.Kind != TokenKind.CloseBracket
            || children.Skip(argumentStart + 3).Take(children.Length - argumentStart - 5)
                .Any(static child => child.Token.Kind is not (TokenKind.Identifier or TokenKind.Dot)))
        {
            return false;
        }

        var conditionName = string.Concat(children.Skip(argumentStart + 3)
            .Take(children.Length - argumentStart - 5)
            .Select(static child => child.Token.Kind == TokenKind.Dot ? "." : child.Token.Name));
        if (conditionName is not ("NativeOutputCondition.NonZero"
            or "NativeOutputCondition.NonNegative"
            or "Terrum.Interop.NativeOutputCondition.NonZero"
            or "Terrum.Interop.NativeOutputCondition.NonNegative"))
        {
            return false;
        }

        nonNegative = conditionName.EndsWith(".NonNegative", StringComparison.Ordinal);
        return true;
    }

    private static bool IsNativeOutputAttribute(string name) =>
        name is "NativeOutput" or "Terrum.Interop.NativeOutput";

    private bool TryBindDirectNativePointerType(SyntaxNode node, out PrimitiveType pointeeType,
        out bool isWritable)
    {
        pointeeType = PrimitiveType.Int32;
        isWritable = false;
        var typeArguments = node.Children.OfType<SyntaxNode>().ToArray();
        if (TypeNameIdentifier(node) != "ptr" || typeArguments.Length != 1
            || node.Children[0] is not SyntaxTokenElement firstToken
            || firstToken.Token.Kind is not (TokenKind.Identifier or TokenKind.WriteKeyword)
            || node.Children[^1] is not SyntaxTokenElement { Token.Kind: TokenKind.Greater })
        {
            return false;
        }

        isWritable = firstToken.Token.Kind == TokenKind.WriteKeyword;
        var pointee = BindType(typeArguments[0], allowVoid: true);
        if (isWritable
            ? !DirectNativeImportContract.IsOutputPointee(pointee, _structs)
            : !DirectNativeImportContract.IsInputPointee(pointee, _structs))
        {
            return false;
        }
        pointeeType = pointee;
        return true;
    }

    internal static bool TryPrimitiveType(string name, out PrimitiveType type)
    {
        type = name switch
        {
            "bool" => PrimitiveType.Bool,
            "char8" => PrimitiveType.Char8,
            "char16" => PrimitiveType.Char16,
            "char32" => PrimitiveType.Char32,
            "rune" => PrimitiveType.Rune,
            "int8" => PrimitiveType.Int8,
            "int16" => PrimitiveType.Int16,
            "int32" => PrimitiveType.Int32,
            "uint32" => PrimitiveType.UInt32,
            "uint8" => PrimitiveType.UInt8,
            "uint16" => PrimitiveType.UInt16,
            "int64" => PrimitiveType.Int64,
            "uint64" => PrimitiveType.UInt64,
            "int128" => PrimitiveType.Int128,
            "uint128" => PrimitiveType.UInt128,
            "float16" => PrimitiveType.Float16,
            "bfloat16" => PrimitiveType.BFloat16,
            "float32" => PrimitiveType.Float32,
            "float64" => PrimitiveType.Float64,
            "float128" => PrimitiveType.Float128,
            "decimal32" => PrimitiveType.Decimal32,
            "decimal64" => PrimitiveType.Decimal64,
            "decimal128" => PrimitiveType.Decimal128,
            _ => PrimitiveType.Void,
        };
        return type != PrimitiveType.Void;
    }

    private void ValidatePrimitiveDeclaration(SyntaxNode declaration, string name)
    {
        var fields = 0;
        foreach (var member in declaration.Children)
        {
            if (member is SyntaxNode { Kind: SyntaxKind.FieldDeclaration } field)
            {
                fields++;
                if (field.Children[^2] is SyntaxTokenElement { Token.Kind: TokenKind.Identifier } backing)
                {
                    _primitiveBackingFields[name] = backing.Token.Name;
                }
                if (field.Children[^2] is not SyntaxTokenElement { Token.Kind: TokenKind.Identifier }
                    || NameText((SyntaxNode)field.Children[^3]) != name
                    || field.Children.OfType<SyntaxTokenElement>().Any(static item =>
                        item.Token.Kind is TokenKind.StaticKeyword or TokenKind.PublicKeyword
                            or TokenKind.InternalKeyword or TokenKind.MutKeyword or TokenKind.UnsafeKeyword
                            or TokenKind.ExternKeyword))
                {
                    Report("TRM3005", $"The Core primitive '{name}' needs a private, uninitialized backing field of its own type.",
                        field.Span);
                }
            }
            else if (member is SyntaxNode { Kind: SyntaxKind.ConformanceList })
            {
                Report("TRM3001", "Core primitive conformances are not yet supported by the bounded compiler.",
                    member.Span);
            }
            else if (member is SyntaxNode { Kind: SyntaxKind.Attribute }
                or SyntaxTokenElement {
                    Token.Kind: TokenKind.UniqueKeyword
                    or TokenKind.MutKeyword or TokenKind.InternalKeyword or TokenKind.UnsafeKeyword
                })
            {
                Report("TRM3001", "Core primitive declarations have compiler-owned visibility and layout.",
                    member.Span);
            }
        }
        if (fields != 1)
        {
            Report("TRM3005", $"The Core primitive '{name}' requires exactly one recursive backing field.",
                declaration.Span);
        }
    }

    private void DeclareStruct(int index)
    {
        if (_laidOut.Contains(index))
        {
            return;
        }
        var (tree, declaration) = _structDeclarations[index];
        SetContext(tree, declaration);
        if (_layingOut.Count >= MaximumSemanticDepth || !_layingOut.Add(index))
        {
            Report("TRM3005", "Value layouts must be acyclic and within the semantic nesting limit.", declaration.Span);
            return;
        }
        var qualified = _structs[index].Name;
        var isPublic = !_structs[index].IsInternal;
        var isUnique = false;
        var isTransparent = false;
        var isSequential = false;
        var isBlittable = false;
        var layoutAttributeSeen = false;
        var visibilityCount = 0;
        foreach (var element in declaration.Children)
        {
            if (element is SyntaxNode { Kind: SyntaxKind.Attribute } attribute)
            {
                var attributeName = AttributeName(attribute);
                if (attributeName == "Blittable")
                {
                    if (isBlittable || !IsNoArgumentBlittableAttribute(attribute))
                    {
                        Report("TRM3005", "Blittable is a nonrepeatable, parameterless source layout contract.",
                            attribute.Span);
                    }
                    else
                    {
                        isBlittable = true;
                    }
                }
                else if (attributeName == "Layout")
                {
                    if (layoutAttributeSeen)
                    {
                        Report("TRM3005", "Layout is a nonrepeatable source layout contract.", attribute.Span);
                    }
                    else
                    {
                        layoutAttributeSeen = true;
                        if (!TryDecodeLayoutAttribute(attribute, out var layoutKind, out var hasControls))
                        {
                            Report("TRM3005", hasControls
                                ? "Packing and Alignment controls are not supported by the current fixed-layout source profile."
                                : "Layout requires a supported LayoutKind value.", attribute.Span);
                        }
                        else
                        {
                            switch (layoutKind)
                            {
                                case "Auto":
                                    break;
                                case "Sequential":
                                    isSequential = true;
                                    break;
                                case "Transparent":
                                    isTransparent = true;
                                    break;
                                case "Union":
                                    Report("TRM3005",
                                        "Union layout is not supported by the current source layout profile.",
                                        attribute.Span);
                                    break;
                                default:
                                    Report("TRM3005", "LayoutKind does not name a supported layout form.",
                                        attribute.Span);
                                    break;
                            }
                        }
                    }
                }
                else
                {
                    Report("TRM3001", "Only one [Layout(Transparent)] or [Layout(Sequential)] attribute is supported on a struct.", attribute.Span);
                }
            }
            if (element is SyntaxTokenElement modifier)
            {
                isUnique |= modifier.Token.Kind == TokenKind.UniqueKeyword;
                visibilityCount += modifier.Token.Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword or TokenKind.PrivateKeyword ? 1 : 0;
                if (modifier.Token.Kind is TokenKind.PrivateKeyword or TokenKind.StaticKeyword
                    or TokenKind.MutKeyword or TokenKind.UnsafeKeyword or TokenKind.ExternKeyword)
                {
                    Report("TRM3001", "Top-level structs support only public or internal visibility.", modifier.Span);
                }
            }
        }
        if (visibilityCount > 1)
        {
            Report("TRM3001", "A struct can declare only one visibility modifier.", declaration.Span);
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
            if (type is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView
                || ManagedClassTypes.IsManaged(type))
            {
                Report("TRM3023", "A native buffer view or managed handle cannot be stored in an inline struct.", field.Span);
            }
            if ((type == PrimitiveType.TokenSpan && (qualified != TokenSpanContract.CursorName || !isUnique))
                || TokenSpanContract.IsCursor(type, _structs))
            {
                Report("TRM3023", "Stored span dependencies are limited to the compilation-local TokenCursor field.", field.Span);
            }
            if (IsStruct(type) && !_laidOut.Contains((int)type - 100))
            {
                var previousContext = CaptureContext();
                DeclareStruct((int)type - 100);
                RestoreContext(previousContext);
            }
            if (IsUnique(type) && !isUnique)
            {
                Report("TRM3023", "Copy structs cannot store unique owning fields.", field.Span);
            }
            var width = ValueLayout.Size(type, _structs);
            var fieldAlignment = width == 0 ? 1 : ValueAlignment(type);
            var duplicate = false;
            foreach (var previous in fields)
            {
                duplicate |= previous.Name == fieldName.Name;
            }
            var offset = (size + fieldAlignment - 1) & -fieldAlignment;
            if (width == 0 || offset + width > ValueLayout.MaximumStructureSize || duplicate
                || fields.Count >= ValueLayout.MaximumSize)
            {
                Report("TRM3005", "Structs require distinct, initialized value fields within the bounded storage profile.",
                    field.Span);
                continue;
            }
            var fieldIsPublic = false;
            var fieldIsInternal = false;
            var mutableModifiers = 0;
            var unsafeModifiers = 0;
            var accessModifiers = 0;
            foreach (var child in field.Children)
            {
                if (child is SyntaxNode { Kind: SyntaxKind.Attribute } attribute)
                {
                    Report("TRM3001", "Attributes on fields are not supported.", attribute.Span);
                }
                if (child is SyntaxTokenElement modifier)
                {
                    fieldIsPublic |= modifier.Token.Kind == TokenKind.PublicKeyword;
                    fieldIsInternal |= modifier.Token.Kind == TokenKind.InternalKeyword;
                    accessModifiers += modifier.Token.Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword or TokenKind.PrivateKeyword ? 1 : 0;
                    mutableModifiers += modifier.Token.Kind == TokenKind.MutKeyword ? 1 : 0;
                    if (modifier.Token.Kind == TokenKind.StaticKeyword)
                    {
                        Report("TRM3001", "Static struct fields are not supported.", modifier.Span);
                    }
                    else if (modifier.Token.Kind == TokenKind.ExternKeyword)
                    {
                        Report("TRM3030", "Extern storage fields are outside the scalar callable import profile.",
                            modifier.Span);
                    }
                    else if (modifier.Token.Kind == TokenKind.UnsafeKeyword)
                    {
                        unsafeModifiers++;
                    }
                }
            }
            if (accessModifiers > 1)
            {
                Report("TRM3001", "A field can declare only one visibility modifier.", field.Span);
            }
            if (mutableModifiers > 1)
            {
                Report("TRM3001", "A field can declare only one mut modifier.", field.Span);
            }
            if (unsafeModifiers > 1)
            {
                Report("TRM3001", "A field can declare only one unsafe modifier.", field.Span);
            }
            fields.Add(new BoundStructField(fieldName.Name, type, offset, width) {
                IsMutable = mutableModifiers != 0,
                IsPrivate = !fieldIsPublic && !fieldIsInternal,
                IsInternal = fieldIsInternal,
                RequiresUnsafeCaller = unsafeModifiers != 0,
            });
            size = offset + width;
            alignment = Math.Max(alignment, fieldAlignment);
        }
        if (fields.Count == 0)
        {
            Report("TRM3005", "A struct requires at least one field.", declaration.Span);
        }
        if (isTransparent && (isUnique || fields.Count != 1
            || (isBlittable
                ? !IsBlittableLayoutFieldType(fields[0].Type)
                : PrimitiveTypeFacts.ScalarWidth(fields[0].Type) <= 0)))
        {
            Report("TRM3005", "Transparent Copy structs require exactly one scalar value field.", declaration.Span);
        }
        var fieldLayout = fields.ToImmutable();
        var hasPublicFields = fieldLayout.Any(static field => !field.IsPrivate && !field.IsInternal);
        if (isPublic && fieldLayout.Any(field => ContainsGenericStructSpecialization(field.Type)))
        {
            Report("TRM3005",
                "Public structs cannot expose compilation-local generic struct specializations.",
                declaration.Span);
        }
        if (isPublic && hasPublicFields && !isBlittable)
        {
            Report("TRM3005", "Public instance fields require an explicit Blittable layout commitment.",
                declaration.Span);
        }
        if (isBlittable && (!layoutAttributeSeen || (!isSequential && !isTransparent)))
        {
            Report("TRM3005",
                "The current Blittable source profile requires an explicit Layout(LayoutKind.Sequential) commitment.",
                declaration.Span);
        }
        size = (size + alignment - 1) & -alignment;
        var structureType = (PrimitiveType)(100 + index);
        _structs[index] = new BoundStruct(qualified, fieldLayout, size) {
            IsInternal = _structs[index].IsInternal,
            IsUnique = isUnique,
            IsTransparent = isTransparent,
            BlittableTargetRequirement = isBlittable
                ? FixedLayoutStorage.WindowsX64TargetRequirement : 0,
            IsSequential = isSequential,
        };
        _structureAlignments[structureType] = alignment;
        if (isSequential && (isPublic || isUnique || fields.Any(static field => !field.IsPrivate))
            && !isBlittable)
        {
            Report("TRM3005", "Sequential layout supports only nonpublic Copy structs with private fields.", declaration.Span);
        }
        if (size is < 1 or > ValueLayout.MaximumStructureSize
            || (size > ValueLayout.MaximumSize && !isBlittable))
        {
            Report("TRM3005",
                $"A struct must fit the fixed-storage limit, and aggregates larger than {ValueLayout.MaximumSize} bytes require a Blittable commitment.",
                declaration.Span);
        }
        _ = _layingOut.Remove(index);
        _ = _laidOut.Add(index);
    }

    private static bool IsNoArgumentBlittableAttribute(SyntaxNode attribute) =>
        attribute.Children is [
            SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket },
            SyntaxTokenElement { Token.Kind: TokenKind.Identifier, Token.Text: "Blittable" },
            SyntaxTokenElement { Token.Kind: TokenKind.CloseBracket },
        ];

    private static bool TryDecodeLayoutAttribute(SyntaxNode attribute, out string layoutKind,
        out bool hasUnsupportedControls)
    {
        layoutKind = "";
        var tokens = attribute.Children.OfType<SyntaxTokenElement>().ToArray();
        hasUnsupportedControls = tokens.Any(static token =>
            token.Token.Text is "Packing" or "Alignment");

        if (hasUnsupportedControls)
        {
            return false;
        }
        if (tokens is [
            { Token.Kind: TokenKind.OpenBracket },
            { Token.Kind: TokenKind.Identifier, Token.Text: "Layout" },
            { Token.Kind: TokenKind.OpenParen },
            { Token.Kind: TokenKind.Identifier } simpleKind,
            { Token.Kind: TokenKind.CloseParen },
            { Token.Kind: TokenKind.CloseBracket },
        ])
        {
            layoutKind = simpleKind.Token.Text;
            return true;
        }
        if (tokens is [
            { Token.Kind: TokenKind.OpenBracket },
            { Token.Kind: TokenKind.Identifier, Token.Text: "Layout" },
            { Token.Kind: TokenKind.OpenParen },
            { Token.Kind: TokenKind.Identifier, Token.Text: "LayoutKind" },
            { Token.Kind: TokenKind.Dot },
            { Token.Kind: TokenKind.Identifier } qualifiedKind,
            { Token.Kind: TokenKind.CloseParen },
            { Token.Kind: TokenKind.CloseBracket },
        ])
        {
            layoutKind = qualifiedKind.Token.Text;
            return true;
        }
        if (tokens is [
            { Token.Kind: TokenKind.OpenBracket },
            { Token.Kind: TokenKind.Identifier, Token.Text: "Layout" },
            { Token.Kind: TokenKind.OpenParen },
            { Token.Kind: TokenKind.Identifier, Token.Text: "Kind" },
            { Token.Kind: TokenKind.Colon },
            { Token.Kind: TokenKind.Identifier, Token.Text: "LayoutKind" },
            { Token.Kind: TokenKind.Dot },
            { Token.Kind: TokenKind.Identifier } namedKind,
            { Token.Kind: TokenKind.CloseParen },
            { Token.Kind: TokenKind.CloseBracket },
        ])
        {
            layoutKind = namedKind.Token.Text;
            return true;
        }

        return false;
    }

    private static bool IsBlittableLayoutFieldType(PrimitiveType type)
    {
        if (PrimitiveTypeFacts.ScalarWidth(type) > 0
            || type is PrimitiveType.NativeRawPointer
            || NativePointerTypes.IsType(type) || NativeFunctionPointerTypes.IsType(type))
        {
            return true;
        }

        return false;
    }

    private int ValueAlignment(PrimitiveType type)
    {
        if (!IsStruct(type))
        {
            return ValueLayout.Alignment(type, _structs);
        }
        if (_structureAlignments.TryGetValue(type, out var alignment))
        {
            return alignment;
        }
        alignment = ValueLayout.Alignment(type, _structs);
        if (_laidOut.Contains((int)type - 100))
        {
            _structureAlignments.Add(type, alignment);
        }
        return alignment;
    }

    private static SyntaxToken StructName(SyntaxNode declaration)
    {
        for (var index = 0; index + 1 < declaration.Children.Length; index++)
        {
            if (declaration.Children[index] is SyntaxTokenElement token
                && token.Token.Kind is TokenKind.StructKeyword or TokenKind.ClassKeyword)
            {
                return ((SyntaxTokenElement)declaration.Children[index + 1]).Token;
            }
        }
        throw new InvalidOperationException();
    }

    private static bool HasGenericTypeParameters(SyntaxNode declaration) =>
        declaration.Children.OfType<SyntaxNode>()
            .Any(static child => child.Kind == SyntaxKind.TypeParameterList);

    private void DeclareGenericStructTemplate(SyntaxTree tree, SyntaxNode declaration)
    {
        var name = StructName(declaration);
        var qualifiedName = Qualify(name.Name);
        if (TryPrimitiveType(name.Name, out _)
            || qualifiedName == DirectNativeImportContract.AttributeFullyQualifiedName
            || qualifiedName is NativeBufferIntrinsics.TypeName or "ReadonlyInt32BufferView"
                or "MutableInt32BufferView" or NativeManagedNodeIntrinsics.TypeName
                or NativeManagedNodeIntrinsics.HeapName
            || _classNames.ContainsKey(qualifiedName) || _structNames.ContainsKey(qualifiedName)
            || _genericStructTemplates.ContainsKey(qualifiedName) || _protocols.ContainsKey(qualifiedName))
        {
            Report("TRM3003", $"Duplicate or reserved generic struct '{qualifiedName}'.", declaration.Span);
            return;
        }

        var parameterList = declaration.Children.OfType<SyntaxNode>()
            .First(static child => child.Kind == SyntaxKind.TypeParameterList);
        var typeParameters = parameterList.Children.OfType<SyntaxTokenElement>()
            .Where(static child => child.Token.Kind == TokenKind.Identifier)
            .Select(static child => child.Token.Name)
            .ToImmutableArray();
        if (typeParameters.IsEmpty
            || typeParameters.Distinct(StringComparer.Ordinal).Count() != typeParameters.Length)
        {
            Report("TRM3005", "Generic structs require distinct type parameters.", parameterList.Span);
            return;
        }

        if (declaration.Children.OfType<SyntaxTokenElement>()
            .Any(static child => child.Token.Kind == TokenKind.PublicKeyword))
        {
            Report("TRM3005",
                "Public generic structs require generic definition metadata, which is not supported by this profile.",
                declaration.Span);
            return;
        }

        if (declaration.Children.OfType<SyntaxNode>().Any(static child =>
                child.Kind is SyntaxKind.ConformanceList or SyntaxKind.ConstraintClause))
        {
            Report("TRM3005",
                "Generic struct conformances and constraints require nominal witness support.",
                declaration.Span);
            return;
        }

        var unsupportedMember = declaration.Children.OfType<SyntaxNode>().FirstOrDefault(static child =>
            (child.Kind is SyntaxKind.OperatorDeclaration or SyntaxKind.PropertyDeclaration)
            || ((child.Kind is SyntaxKind.FunctionDeclaration or SyntaxKind.ConstructorDeclaration)
                && HasGenericTypeParameters(child)));
        if (unsupportedMember is not null)
        {
            Report("TRM3005",
                "Generic struct specializations do not support operators, properties, or generic methods.",
                unsupportedMember.Span);
            return;
        }

        if (declaration.Children.OfType<SyntaxTokenElement>().Any(static child =>
                child.Token.Kind is TokenKind.PrivateKeyword or TokenKind.StaticKeyword
                    or TokenKind.MutKeyword or TokenKind.UnsafeKeyword or TokenKind.ExternKeyword))
        {
            Report("TRM3001", "Top-level structs support only public or internal visibility.", declaration.Span);
            return;
        }

        var visibilityCount = declaration.Children.OfType<SyntaxTokenElement>()
            .Count(static child => child.Token.Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword);
        if (visibilityCount > 1)
        {
            Report("TRM3001", "A struct can declare only one visibility modifier.", declaration.Span);
            return;
        }

        _genericStructTemplates.Add(qualifiedName,
            new GenericStructTemplate(name.Name, qualifiedName, typeParameters, tree, declaration));
    }

    private PrimitiveType InstantiateGenericStruct(GenericStructTemplate template,
        ImmutableArray<PrimitiveType> typeArguments, TextSpan useSite)
    {
        var key = template.QualifiedName + "\u001F" + string.Join(",",
            typeArguments.Select(static type => ((int)type).ToString(
                System.Globalization.CultureInfo.InvariantCulture)));
        if (_genericStructSpecializations.TryGetValue(key, out var existing))
        {
            return existing;
        }
        if (_structs.Count >= SemanticVerifier.MaximumVariables)
        {
            Report("TRM3016", "Source exceeds the supported number of value layouts.", useSite);
            return PrimitiveType.Int32;
        }

        var displayArguments = string.Join(", ", typeArguments.Select(TypeName));
        var specializationName = template.Name + "<" + displayArguments + ">";
        var qualifiedName = template.QualifiedName + "<" + displayArguments + ">";
        var index = _structs.Count;
        var type = (PrimitiveType)(100 + index);
        var arguments = ImmutableDictionary.CreateBuilder<string, PrimitiveType>(StringComparer.Ordinal);
        for (var parameterIndex = 0; parameterIndex < typeArguments.Length; parameterIndex++)
        {
            arguments.Add(template.TypeParameters[parameterIndex], typeArguments[parameterIndex]);
        }
        var typeMap = arguments.ToImmutable();

        _genericStructSpecializations.Add(key, type);
        _ = _genericStructSpecializationTypes.Add(type);
        _structNames.Add(qualifiedName, type);
        _structs.Add(new BoundStruct(qualifiedName, [], 0) {
            IsInternal = true,
        });
        _structDeclarations.Add(index, (template.Tree, template.Declaration));

        var previousContext = CaptureContext();
        var previousGenericOwnerTypeArguments = _genericOwnerTypeArguments;
        SetContext(template.Tree, template.Declaration);
        _aliasTypeArguments.Add(typeMap);
        _genericOwnerTypeArguments = typeMap;
        try
        {
            DeclareStruct(index);
            foreach (var member in template.Declaration.Children.OfType<SyntaxNode>())
            {
                if (member.Kind is SyntaxKind.FunctionDeclaration or SyntaxKind.ConstructorDeclaration)
                {
                    Declare(member, specializationName);
                }
            }
        }
        finally
        {
            _genericOwnerTypeArguments = previousGenericOwnerTypeArguments;
            _aliasTypeArguments.RemoveAt(_aliasTypeArguments.Count - 1);
            RestoreContext(previousContext);
        }
        return type;
    }

    private void DeclareAliases()
    {
        foreach (var (tree, declaration) in _scopedDeclarations
            .Where(static item => item.Declaration.Kind == SyntaxKind.AliasDeclaration))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SetContext(tree, declaration);
            {
                SyntaxToken? nameToken = null;
                SyntaxNode? target = null;
                SyntaxNode? typeParameterList = null;
                var isPublic = false;
                var visibilityCount = 0;
                var hasPrivateVisibility = false;
                var hasInvalidModifier = false;
                var hasAttribute = false;
                foreach (var child in declaration.Children)
                {
                    if (child is SyntaxTokenElement tokenElement)
                    {
                        var token = tokenElement.Token;
                        if (nameToken is null && token.Kind == TokenKind.Identifier)
                        {
                            nameToken = token;
                        }
                        if (token.Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword
                            or TokenKind.PrivateKeyword)
                        {
                            visibilityCount++;
                            isPublic |= token.Kind == TokenKind.PublicKeyword;
                            hasPrivateVisibility |= token.Kind == TokenKind.PrivateKeyword;
                        }
                        hasInvalidModifier |= token.Kind is TokenKind.StaticKeyword or TokenKind.MutKeyword
                            or TokenKind.UniqueKeyword or TokenKind.UnsafeKeyword or TokenKind.ExternKeyword;
                    }
                    else if (child is SyntaxNode node)
                    {
                        if (target is null && node.Kind == SyntaxKind.TypeName)
                        {
                            target = node;
                        }
                        if (typeParameterList is null && node.Kind == SyntaxKind.TypeParameterList)
                        {
                            typeParameterList = node;
                        }
                        hasAttribute |= node.Kind == SyntaxKind.Attribute;
                    }
                }
                if (nameToken is not SyntaxToken name || target is null)
                {
                    continue;
                }
                var typeParameterBuilder = ImmutableArray.CreateBuilder<string>();
                HashSet<string>? seenTypeParameters = null;
                var hasDuplicateTypeParameter = false;
                if (typeParameterList is not null)
                {
                    foreach (var child in typeParameterList.Children)
                    {
                        if (child is not SyntaxTokenElement { Token.Kind: TokenKind.Identifier } element)
                        {
                            continue;
                        }
                        var typeParameter = element.Token.Name;
                        typeParameterBuilder.Add(typeParameter);
                        if (seenTypeParameters is null && typeParameterBuilder.Count > 1)
                        {
                            seenTypeParameters = new HashSet<string>(StringComparer.Ordinal) {
                                typeParameterBuilder[0],
                            };
                        }
                        if (seenTypeParameters is not null && !seenTypeParameters.Add(typeParameter))
                        {
                            hasDuplicateTypeParameter = true;
                        }
                    }
                }
                var typeParameters = typeParameterBuilder.ToImmutable();
                if (hasDuplicateTypeParameter)
                {
                    Report("TRM3001", "A type alias cannot repeat a type parameter.", declaration.Span);
                    continue;
                }
                if (visibilityCount > 1 || hasPrivateVisibility || hasInvalidModifier || hasAttribute)
                {
                    Report("TRM3001", "Type aliases permit at most one public or internal modifier and no attributes.",
                        declaration.Span);
                }
                if (_isLibrary && isPublic)
                {
                    Report("TRM3001", "Public type aliases cannot be emitted until alias metadata is defined.",
                        declaration.Span);
                }
                var qualifiedName = Qualify(name.Name);
                if (TryPrimitiveType(name.Name, out _)
                    || qualifiedName == DirectNativeImportContract.AttributeFullyQualifiedName
                    || qualifiedName is NativeBufferIntrinsics.TypeName
                        or "ReadonlyInt32BufferView" or "MutableInt32BufferView"
                    || name.Name is NativeManagedNodeIntrinsics.TypeName or NativeManagedNodeIntrinsics.HeapName
                    || _structNames.ContainsKey(qualifiedName)
                    || _genericStructTemplates.ContainsKey(qualifiedName)
                    || _classNames.ContainsKey(qualifiedName)
                    || _protocols.ContainsKey(qualifiedName)
                    || !_aliases.TryAdd(qualifiedName,
                        new AliasDefinition(tree, declaration, target, typeParameters, qualifiedName, isPublic)))
                {
                    Report("TRM3003", $"Duplicate or reserved type alias '{qualifiedName}'.", declaration.Span);
                }
            }
        }
        ValidateAliasCycles();
        foreach (var alias in _aliases.Values)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (alias.TypeParameters.IsEmpty)
            {
                _ = ResolveAlias(alias, out _);
            }
        }
    }

    private void ValidateAliasCycles()
    {
        var dependencies = new Dictionary<AliasDefinition, HashSet<AliasDefinition>>();
        foreach (var alias in _aliases.Values)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var previousContext = CaptureContext();
            SetContext(alias.Tree, alias.Declaration);
            var edges = new HashSet<AliasDefinition>();
            foreach (var typeName in DescendantTypeNames(alias.Target))
            {
                var referencedName = TypeNameIdentifier(typeName);
                if (!alias.TypeParameters.Contains(referencedName, StringComparer.Ordinal)
                    && FindAliasDefinition(referencedName) is AliasDefinition referenced)
                {
                    _ = edges.Add(referenced);
                }
            }
            dependencies.Add(alias, edges);
            RestoreContext(previousContext);
        }

        var states = new Dictionary<AliasDefinition, byte>();
        var stack = new List<AliasDefinition>();
        foreach (var alias in _aliases.Values)
        {
            Visit(alias);
        }

        void Visit(AliasDefinition alias)
        {
            if (states.TryGetValue(alias, out var state))
            {
                if (state == 1)
                {
                    var first = stack.IndexOf(alias);
                    var cycle = stack.Skip(Math.Max(first, 0)).Append(alias).ToArray();
                    foreach (var member in cycle)
                    {
                        member.IsInvalid = true;
                    }
                    Report("TRM3005", $"Recursive type alias cycle: {string.Join(" -> ", cycle.Select(static item => item.QualifiedName))}.",
                        alias.Declaration.Span);
                }
                return;
            }

            states.Add(alias, 1);
            stack.Add(alias);
            foreach (var dependency in dependencies[alias])
            {
                Visit(dependency);
            }
            stack.RemoveAt(stack.Count - 1);
            states[alias] = 2;
        }
    }

    private AliasDefinition? FindAliasDefinition(string name)
    {
        if (name.Contains('.', StringComparison.Ordinal))
        {
            return _aliases.TryGetValue(name, out var alias) ? alias : null;
        }

        string? candidateName = null;
        AliasDefinition? candidate = null;
        if (!TryAddAliasCandidate(name, ref candidateName, ref candidate)
            || !TryAddAliasCandidate(Qualify(name), ref candidateName, ref candidate))
        {
            return null;
        }
        var parent = _namespace;
        while (parent.Contains('.', StringComparison.Ordinal))
        {
            parent = parent[..parent.LastIndexOf('.', StringComparison.Ordinal)];
            if (!TryAddAliasCandidate(parent + "." + name, ref candidateName, ref candidate))
            {
                return null;
            }
        }
        foreach (var imported in _usings)
        {
            if (!TryAddAliasCandidate(imported + "." + name, ref candidateName, ref candidate))
            {
                return null;
            }
        }
        return candidate;
    }

    private bool TryAddAliasCandidate(string qualified, ref string? candidateName, ref AliasDefinition? candidate)
    {
        if (!_aliases.TryGetValue(qualified, out var alias))
        {
            return true;
        }
        if (candidateName is not null && candidateName != qualified)
        {
            return false;
        }
        candidateName = qualified;
        candidate = alias;
        return true;
    }

    private static IEnumerable<SyntaxNode> DescendantTypeNames(SyntaxNode node)
    {
        if (node.Kind == SyntaxKind.TypeName)
        {
            yield return node;
        }
        foreach (var child in node.Children.OfType<SyntaxNode>())
        {
            foreach (var descendant in DescendantTypeNames(child))
            {
                yield return descendant;
            }
        }
    }

    private DeclaredTypeResolution ResolveAlias(AliasDefinition alias, out PrimitiveType type) =>
        ResolveAlias(alias, null, alias.Declaration.Span, out type);

    private DeclaredTypeResolution ResolveAlias(AliasDefinition alias,
        ImmutableArray<PrimitiveType>? typeArguments, TextSpan useSite, out PrimitiveType type)
    {
        if (alias.IsResolved && typeArguments is null)
        {
            type = alias.Type;
            return DeclaredTypeResolution.Unique;
        }
        if (alias.IsInvalid)
        {
            type = default;
            return DeclaredTypeResolution.Invalid;
        }
        if (typeArguments is null && !alias.TypeParameters.IsEmpty)
        {
            Report("TRM3005", $"Generic type alias '{alias.QualifiedName}' requires type arguments.", useSite);
            type = default;
            return DeclaredTypeResolution.Invalid;
        }
        if (typeArguments is ImmutableArray<PrimitiveType> arguments
            && arguments.Length != alias.TypeParameters.Length)
        {
            Report("TRM3005",
                $"Type alias '{alias.QualifiedName}' expects {alias.TypeParameters.Length} type arguments but received {arguments.Length}.",
                useSite);
            type = default;
            return DeclaredTypeResolution.Invalid;
        }
        if (alias.IsResolving)
        {
            var first = _aliasResolutionStack.IndexOf(alias);
            var cycle = _aliasResolutionStack.Skip(Math.Max(first, 0))
                .Select(static item => item.QualifiedName).Append(alias.QualifiedName);
            Report("TRM3005", $"Recursive type alias cycle: {string.Join(" -> ", cycle)}.", useSite);
            alias.IsInvalid = true;
            type = default;
            return DeclaredTypeResolution.Invalid;
        }
        if (_aliasResolutionStack.Count >= MaximumSemanticDepth)
        {
            Report("TRM3015", $"Type alias expansion exceeds {MaximumSemanticDepth} declarations.",
                alias.Declaration.Span);
            alias.IsInvalid = true;
            type = default;
            return DeclaredTypeResolution.Invalid;
        }

        var previousContext = CaptureContext();
        SetContext(alias.Tree, alias.Declaration);
        var diagnosticCount = DiagnosticCount;
        if (typeArguments is ImmutableArray<PrimitiveType> resolvedArguments)
        {
            var parameters = new Dictionary<string, PrimitiveType>(StringComparer.Ordinal);
            for (var index = 0; index < resolvedArguments.Length; index++)
            {
                parameters.Add(alias.TypeParameters[index], resolvedArguments[index]);
            }
            _aliasTypeArguments.Add(parameters);
        }
        alias.IsResolving = true;
        _aliasResolutionStack.Add(alias);
        try
        {
            var resolved = BindType(alias.Target, allowVoid: false);
            if (DiagnosticCount != diagnosticCount)
            {
                if (alias.TypeParameters.IsEmpty)
                {
                    alias.IsInvalid = true;
                }
                type = default;
                return DeclaredTypeResolution.Invalid;
            }
            if (alias.IsPublic && !IsPublicType(resolved))
            {
                Report("TRM3005", $"Public type alias '{alias.QualifiedName}' exposes a non-public target.",
                    useSite);
                if (alias.TypeParameters.IsEmpty)
                {
                    alias.IsInvalid = true;
                }
                type = default;
                return DeclaredTypeResolution.Invalid;
            }
            if (alias.TypeParameters.IsEmpty)
            {
                alias.Type = resolved;
                alias.IsResolved = true;
            }
            type = resolved;
            return DeclaredTypeResolution.Unique;
        }
        finally
        {
            _aliasResolutionStack.RemoveAt(_aliasResolutionStack.Count - 1);
            alias.IsResolving = false;
            if (typeArguments is not null)
            {
                _aliasTypeArguments.RemoveAt(_aliasTypeArguments.Count - 1);
            }
            RestoreContext(previousContext);
        }
    }

    private bool IsPublicType(PrimitiveType type)
    {
        if (IsStruct(type))
        {
            var index = (int)type - 100;
            return !_structDeclarations.ContainsKey(index) || !_structs[index].IsInternal;
        }
        if (IsClass(type) || (ManagedClassTypes.IsOptional(type) && (int)type >= ManagedClassTypes.First))
        {
            var index = ManagedClassTypes.ClassIndex(type);
            return !_classDeclarations.ContainsKey(index) || !_classes[index].IsInternal;
        }
        return true;
    }

    private void Declare(SyntaxNode declaration, string? containingType)
    {
        var constructor = declaration.Kind == SyntaxKind.ConstructorDeclaration;
        var operatorDeclaration = declaration.Kind == SyntaxKind.OperatorDeclaration;
        var property = declaration.Kind == SyntaxKind.PropertyDeclaration;
        var directGetter = property && declaration.Children[^1] is SyntaxNode { Kind: SyntaxKind.ExpressionBody };
        var modifierCount = declaration.Children.Length - (property ? directGetter ? 3 : 5
            : constructor ? 3 : operatorDeclaration ? 5 : 4);
        var isMutable = false;
        var isPrivate = false;
        var isInternal = false;
        var isStatic = false;
        var isExternal = false;
        var requiresUnsafeCaller = false;
        var isNativeCallable = false;
        var isNativeVTableMethod = false;
        var isCleanup = false;
        var isConstEval = false;
        var doesNotReturn = false;
        SyntaxNode? nativeImportAttribute = null;
        SyntaxNode? nativeVTableMethodAttribute = null;
        var nativeVTableSlot = -1;
        string? nativeLibraryName = null;
        string? nativeExportName = null;
        var nativeOutputConditionsByName = new Dictionary<string, bool>(StringComparer.Ordinal);
        var nativeParameters = new Dictionary<int, NativeParameterDefinition>();
        var dependencyAttributes = new List<(SyntaxNode Attribute,
            int? InlineParameterOrdinal)>();
        var accessModifiers = 0;
        for (var index = 0; index < modifierCount; index++)
        {
            if (declaration.Children[index] is SyntaxNode { Kind: SyntaxKind.Attribute } attribute)
            {
                var returnTarget = attribute.Children.Length > 2
                    && attribute.Children[1] is SyntaxTokenElement { Token.Kind: TokenKind.ReturnKeyword }
                    && attribute.Children[2] is SyntaxTokenElement { Token.Kind: TokenKind.Colon };
                var attributeName = AttributeName(attribute);
                if (attributeName.Contains('.', StringComparison.Ordinal)
                    && attributeName != DirectNativeImportContract.AttributeFullyQualifiedName
                    && attributeName != "Terrum.Interop.NativeOutput"
                    && attributeName != "Terrum.Interop.NativeParameter"
                    && attributeName != "Terrum.Interop.NativeCallable"
                    && attributeName != "Terrum.Interop.NativeVTableMethod"
                    && !TryGetDependencyRelation(attributeName, out _))
                {
                    Report("TRM3001",
                        "Only the supported qualified interop attributes are accepted.",
                        attribute.Span);
                    continue;
                }
                if (attributeName == DirectNativeImportContract.AttributeFullyQualifiedName)
                {
                    if (nativeImportAttribute is not null)
                    {
                        Report("TRM3030", "NativeImport is a nonrepeatable compiler-owned attribute.", attribute.Span);
                    }
                    else if (TryDecodeDirectNativeImportAttribute(attribute,
                        out var decodedLibraryName, out var decodedExportName))
                    {
                        nativeLibraryName = decodedLibraryName;
                        nativeExportName = decodedExportName;
                    }
                    else
                    {
                        Report("TRM3030",
                            "NativeImport requires a library and export string literal.",
                            attribute.Span);
                    }
                    nativeImportAttribute = attribute;
                    continue;
                }
                if (attributeName == "Terrum.Interop.NativeParameter")
                {
                    if (returnTarget
                        || !TryDecodeNativeParameterAttribute(attribute, out var nativeParameter)
                        || !nativeParameters.TryAdd(nativeParameter.ParameterIndex, nativeParameter))
                    {
                        Report("TRM3030",
                            "NativeParameter requires a unique ordinal, access, retention, and nonnegative extent.",
                            attribute.Span);
                    }
                    continue;
                }
                if (attributeName == "Terrum.Interop.NativeVTableMethod")
                {
                    if (returnTarget || isNativeVTableMethod
                        || !TryDecodeNativeVTableMethodAttribute(attribute, out nativeVTableSlot))
                    {
                        Report("TRM3030",
                            "NativeVTableMethod requires one nonnegative vtable slot.",
                            attribute.Span);
                    }
                    else
                    {
                        isNativeVTableMethod = true;
                        nativeVTableMethodAttribute = attribute;
                    }
                    continue;
                }
                if (IsNativeOutputAttribute(attributeName))
                {
                    if (returnTarget || !TryGetParameterAttributeTarget(attribute, out var outputParameterName)
                        || !TryDecodeDirectNativeOutputAttribute(attribute, out var nonNegative)
                        || !nativeOutputConditionsByName.TryAdd(outputParameterName, nonNegative))
                    {
                        Report("TRM3030",
                            "NativeOutput must target one distinct parameter with a supported When condition.",
                            attribute.Span);
                    }
                    continue;
                }
                if (attributeName == "Terrum.Interop.NativeCallable")
                {
                    if (returnTarget || isNativeCallable)
                    {
                        Report("TRM3030", "NativeCallable is a nonrepeatable function attribute.", attribute.Span);
                    }
                    else if (attribute.Children.OfType<SyntaxTokenElement>()
                        .Any(static token => token.Token.Kind == TokenKind.OpenParen))
                    {
                        Report("TRM3030", "NativeCallable does not accept arguments.", attribute.Span);
                    }
                    else
                    {
                        isNativeCallable = true;
                    }
                    continue;
                }
                if (TryGetDependencyRelation(attributeName, out _))
                {
                    if (property)
                    {
                        Report("TRM3023",
                            "Dependency annotations on a property must select an exact accessor.",
                            attribute.Span);
                    }
                    else
                    {
                        dependencyAttributes.Add((attribute, null));
                    }
                }
                else if (property)
                {
                    Report("TRM3001", "Getter effects belong on the get accessor.", attribute.Span);
                }
                else if (!returnTarget && attributeName == "NoThrow")
                {
                    Report("TRM3017", "The general NoThrow source contract is retired.", attribute.Span);
                }
                else if (!returnTarget && attributeName == "DoesNotReturn" && !doesNotReturn)
                {
                    doesNotReturn = true;
                }
                else if (!returnTarget && attributeName == "Cleanup" && !isCleanup)
                {
                    isCleanup = true;
                }
                else if (!returnTarget && attribute.Children is [
                    _, SyntaxTokenElement { Token.Text: "ConstEval" }, _] && !isConstEval)
                {
                    isConstEval = true;
                }
                else
                {
                    Report("TRM3001", "Only distinct parameterless DoesNotReturn, Cleanup, and ConstEval contracts are supported.", attribute.Span);
                }
                continue;
            }
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
            if (declaration.Children[index] is SyntaxTokenElement external
                && external.Token.Kind == TokenKind.ExternKeyword)
            {
                if (isExternal)
                {
                    Report("TRM3030", "A callable can declare only one extern modifier.", external.Span);
                }
                isExternal = true;
                continue;
            }
            if (declaration.Children[index] is SyntaxTokenElement unsafeModifier
                && unsafeModifier.Token.Kind == TokenKind.UnsafeKeyword)
            {
                if (requiresUnsafeCaller)
                {
                    Report("TRM3001", "A callable can declare only one unsafe modifier.", unsafeModifier.Span);
                }
                requiresUnsafeCaller = true;
                continue;
            }
            Report("TRM3001", "Function modifier is not supported.", declaration.Children[index].Span);
        }
        if ((constructor && (isStatic || isMutable)) || ((containingType is null) && isPrivate))
        {
            Report("TRM3001", "Constructor and function modifiers conflict with their declaration kind.", declaration.Span);
        }
        if (containingType is not null && _primitiveDeclarations.Contains(containingType)
            && isMutable)
        {
            Report("TRM3001", "Core primitive methods cannot mutate their intrinsic backing value.",
                declaration.Span);
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
        if (containingType is not null && isMutable && !constructor
            && (isStatic || (TryResolveClass(Qualify(containingType), out var mutableOwner)
                && !_classes[ManagedClassTypes.ClassIndex(mutableOwner)].IsMutable)))
        {
            Report("TRM3001", "A mut method requires a writable class or struct receiver.", declaration.Span);
        }

        var returnNode = constructor ? null : (SyntaxNode)declaration.Children[property && directGetter ? ^3
            : property || operatorDeclaration ? ^5 : ^4];
        var parameters = property ? null : (SyntaxNode)declaration.Children[^2];
        var nameToken = ((SyntaxTokenElement)declaration.Children[property ? directGetter ? ^2 : ^4 : ^3]).Token;
        var operatorKey = new StaticOperatorKey(nameToken.Kind, parameters?.Children.OfType<SyntaxNode>().Count() ?? 0);
        var validOperator = (operatorKey.Arity == 1 && IsStaticUnaryOperator(nameToken.Kind))
            || (operatorKey.Arity == 2 && IsStaticBinaryOperator(nameToken.Kind));
        var nameText = operatorDeclaration && validOperator ? OperatorName(operatorKey) : nameToken.Name;
        var getter = property && !directGetter ? (SyntaxNode)declaration.Children[^2] : null;
        var body = getter is SyntaxNode accessor ? (SyntaxNode)accessor.Children[^1]
            : (SyntaxNode)declaration.Children[^1];
        var hasExternalBody = body.Kind == SyntaxKind.ExternalBody;
        if (isExternal != hasExternalBody)
        {
            Report("TRM3030", "Only an extern callable may omit its body, and extern callables must end with ';'.",
                declaration.Span);
        }
        if (nativeImportAttribute is not null && !isExternal)
        {
            Report("TRM3030", "NativeImport may appear only on an extern callable.", nativeImportAttribute.Span);
        }
        if (isNativeVTableMethod
            && (!isExternal || containingType is not null || nativeImportAttribute is not null
                || nativeLibraryName is not null
                || constructor || property || operatorDeclaration || isCleanup || doesNotReturn
                || isNativeCallable
                || declaration.Children.OfType<SyntaxNode>().Any(static child =>
                    child.Kind is SyntaxKind.TypeParameterList or SyntaxKind.ConstraintClause)))
        {
            Report("TRM3030",
                "NativeVTableMethod requires a top-level, non-generic extern function without a direct import or unrelated output effects.",
                nativeVTableMethodAttribute?.Span ?? declaration.Span);
        }
        else if (nativeVTableMethodAttribute is not null && !isNativeVTableMethod)
        {
            Report("TRM3030", "NativeVTableMethod is a nonrepeatable function attribute.",
                nativeVTableMethodAttribute.Span);
        }
        if (isNativeVTableMethod && !requiresUnsafeCaller)
        {
            Report("TRM3030", "A NativeVTableMethod declaration must be marked unsafe.",
                nativeVTableMethodAttribute?.Span ?? declaration.Span);
        }
        if (isNativeVTableMethod)
        {
            requiresUnsafeCaller = true;
        }
        if (isNativeCallable)
        {
            if (isExternal || constructor || property || operatorDeclaration || isCleanup || doesNotReturn
                || containingType is not null
                || declaration.Children.OfType<SyntaxNode>().Any(static child =>
                    child.Kind is SyntaxKind.TypeParameterList or SyntaxKind.ConstraintClause))
            {
                Report("TRM3030",
                    "NativeCallable requires a top-level, non-generic function with no native receiver or output effects.",
                    declaration.Span);
            }
            requiresUnsafeCaller = true;
        }
        if (nativeOutputConditionsByName.Count != 0 && !isExternal)
        {
            Report("TRM3030", "NativeOutput may appear only on an extern callable.", declaration.Span);
        }
        if (nativeParameters.Count != 0 && (!isExternal
            || (nativeLibraryName is null && !isNativeVTableMethod)))
        {
            Report("TRM3030", "NativeParameter requires a direct NativeImport or NativeVTableMethod declaration.",
                nativeImportAttribute?.Span ?? declaration.Span);
        }
        if (isExternal && !isNativeVTableMethod)
        {
            if (nativeImportAttribute is null)
            {
                Report("TRM3030", "An extern callable requires the compiler-owned NativeImport attribute.",
                    declaration.Span);
            }
            if (constructor || property || operatorDeclaration || (containingType is not null && !isStatic)
                || declaration.Children.OfType<SyntaxNode>().Any(static child =>
                    child.Kind is SyntaxKind.TypeParameterList or SyntaxKind.ConstraintClause))
            {
                Report("TRM3030", "The scalar native-import profile supports only top-level or static non-generic functions.",
                    declaration.Span);
            }
            if (isMutable)
            {
                Report("TRM3030", "The scalar native-import profile does not support mutable receiver contracts.",
                    declaration.Span);
            }
        }
        if (isExternal && isNativeVTableMethod && isNativeCallable)
        {
            Report("TRM3030", "NativeVTableMethod cannot be combined with NativeCallable.", declaration.Span);
        }
        if (property)
        {
            if (getter is not null)
            {
                foreach (var child in getter.Children)
                {
                    if (child is SyntaxNode { Kind: SyntaxKind.Attribute } attribute)
                    {
                        if (TryGetDependencyRelation(AttributeName(attribute), out _))
                        {
                            dependencyAttributes.Add((attribute, null));
                        }
                        else if (attribute.Children[1] is SyntaxTokenElement { Token.Text: "NoThrow" })
                        {
                            Report("TRM3017", "The general NoThrow source contract is retired.", attribute.Span);
                        }
                        else
                        {
                            Report("TRM3001", "Getter-local attributes are not supported.", attribute.Span);
                        }
                    }
                    else if (child is SyntaxTokenElement { Token.Kind: TokenKind.UnsafeKeyword } unsafeAccessor)
                    {
                        if (requiresUnsafeCaller)
                        {
                            Report("TRM3001", "A callable can declare only one unsafe modifier.", unsafeAccessor.Span);
                        }
                        requiresUnsafeCaller = true;
                    }
                    else if (child is SyntaxTokenElement modifier && modifier.Token.Text != "get")
                    {
                        Report("TRM3001", "Getter-local visibility and mut modifiers are not supported.", modifier.Span);
                    }
                }
            }
            if (containingType is null || isMutable || isCleanup || doesNotReturn
                || (isStatic && (!TryResolveStruct(Qualify(containingType), out var staticOwner)
                    || IsUnique(staticOwner)))
                || (!TryResolveStruct(Qualify(containingType), out var propertyOwner)
                    && !TryResolveClass(Qualify(containingType), out propertyOwner)
                    && !_primitiveDeclarations.Contains(containingType))
                || (IsStruct(propertyOwner) && IsUnique(propertyOwner))
                || TryFindField(propertyOwner, nameToken.Name, out _)
                || (containingType is not null
                    && _primitiveBackingFields.TryGetValue(containingType, out var backing)
                    && backing == nameToken.Name))
            {
                Report("TRM3001", "Getter-only properties require a distinct member on a Copy struct, source class or Core primitive with a readonly receiver.",
                    declaration.Span);
            }
        }
        if (operatorDeclaration && (containingType is null || !isStatic
            || body.Kind is not (SyntaxKind.Block or SyntaxKind.ExpressionBody)
            || !validOperator))
        {
            Report("TRM3001", "A static operator implementation requires a supported arity and a struct body.", declaration.Span);
        }
        var classConstructor = constructor && TryResolveClass(Qualify(containingType!), out _);
        PrimitiveType returnType;
        if (constructor)
        {
            returnType = ResolveDeclaredType(containingType!);
        }
        else
        {
            returnType = BindType(returnNode!, allowVoid: true);
        }
        if (property && (returnType == PrimitiveType.Void
            || (isStatic ? !IsSupportedStaticGetterType(returnType) : !IsCopyGenericType(returnType))
            || IsUnique(returnType)))
        {
            Report("TRM3007", "A getter must return a supported by-value Copy type.", declaration.Span);
        }
        var hasResultDependency = dependencyAttributes.Any(dependency =>
            dependency.InlineParameterOrdinal is null
            && TryGetDependencyRelation(AttributeName(dependency.Attribute),
                out var relation)
            && relation is BoundDependencyRelation.DependsOn
                or BoundDependencyRelation.ForwardsBacking
            && GetDependencyAttributeTarget(dependency.Attribute).Kind
                is DependencyAttributeTargetKind.Default
                    or DependencyAttributeTargetKind.Return);
        if (isCleanup)
        {
            var structureIndex = containingType is not null && !classConstructor
                && _structNames.TryGetValue(Qualify(containingType), out var structType)
                && IsStruct(structType) ? (int)structType - 100 : -1;
            var structure = structureIndex < 0 ? null : _structs[structureIndex];
            if (structure is null || !structure.IsUnique || isStatic || constructor || isMutable
                || requiresUnsafeCaller || returnType != PrimitiveType.Void || parameters?.Children.Length != 2
                || structure.CleanupMethod is not null || isPrivate || isInternal)
            {
                Report("TRM3023", "Cleanup requires one public, nonmutating, parameterless instance method on a unique struct.", declaration.Span);
            }
            else
            {
                _structs[structureIndex] = structure with {
                    CleanupMethod = structure.Name + "." + nameText,
                };
            }
        }
        if (isConstEval && (containingType is not null || !isInternal || constructor || isCleanup
            || doesNotReturn || !ConstEvalScalar(returnType)))
        {
            Report("TRM3024", "ConstEval requires an internal scalar-returning top-level function.", declaration.Span);
        }
        var declared = ImmutableArray.CreateBuilder<Parameter>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var nativeOutputConditionsByIndex = new Dictionary<int, bool>();

        foreach (var child in parameters?.Children ?? [])
        {
            if (child is not SyntaxNode parameter)
            {
                continue;
            }

            var sourceChildren = parameter.Children
                .Where(static element => element is not SyntaxNode { Kind: SyntaxKind.Attribute })
                .ToArray();
            var typeNode = sourceChildren.OfType<SyntaxNode>().Single();
            var token = ((SyntaxTokenElement)sourceChildren[^1]).Token;
            var parameterIndex = declared.Count;
            bool? nativeOutputCondition = null;
            if (nativeOutputConditionsByName.Remove(token.Name, out var targetedCondition))
            {
                nativeOutputCondition = targetedCondition;
            }
            foreach (var attribute in parameter.Children.OfType<SyntaxNode>()
                .Where(static node => node.Kind == SyntaxKind.Attribute))
            {
                if (TryGetDependencyRelation(AttributeName(attribute), out _))
                {
                    if (GetDependencyAttributeTarget(attribute).Kind
                        != DependencyAttributeTargetKind.Default)
                    {
                        Report("TRM3023",
                            "A parameter-local dependency annotation cannot declare a second attribute target.",
                            attribute.Span);
                    }
                    else
                    {
                        dependencyAttributes.Add((attribute, parameterIndex));
                    }
                }
                else if (!IsNativeOutputAttribute(AttributeName(attribute))
                    || TryGetParameterAttributeTarget(attribute, out _)
                    || !TryDecodeDirectNativeOutputAttribute(attribute, out var inlineCondition)
                    || nativeOutputCondition.HasValue)
                {
                    Report("TRM3030",
                        "A native output parameter accepts one parameter-targeted NativeOutput attribute with a supported When condition.",
                        attribute.Span);
                }
                else
                {
                    nativeOutputCondition = inlineCondition;
                }
            }
            if (nativeOutputCondition is bool outputCondition)
            {
                nativeOutputConditionsByIndex.Add(declared.Count, outputCondition);
            }
            PrimitiveType? nativePointeeType = null;
            PrimitiveType type;
            var isWritableNativePointer = false;
            var pointeeType = PrimitiveType.Void;
            var hasDirectNativePointerType = (nativeLibraryName is not null || isNativeVTableMethod)
                && TryBindDirectNativePointerType(typeNode, out pointeeType,
                    out isWritableNativePointer);
            if (hasDirectNativePointerType)
            {
                nativePointeeType = pointeeType;
                if (nativeOutputCondition.HasValue && isWritableNativePointer)
                {
                    type = BindType(typeNode, allowVoid: false);
                }
                else if (nativeParameters.ContainsKey(parameterIndex + 1) && !isWritableNativePointer)
                {
                    type = PrimitiveType.NativeRawPointer;
                }
                else
                {
                    type = BindType(typeNode, allowVoid: false);
                }
            }
            else
            {
                type = BindType(typeNode, allowVoid: false);
            }
            if (type is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView)
            {
                Report("TRM3023", "A native buffer view cannot be a parameter.", parameter.Span);
            }

            var isInit = sourceChildren is [SyntaxTokenElement { Token.Text: "init" },
                SyntaxTokenElement { Token.Kind: TokenKind.RefKeyword },
                SyntaxTokenElement { Token.Kind: TokenKind.Less }, SyntaxNode,
                SyntaxTokenElement { Token.Kind: TokenKind.Greater }, SyntaxTokenElement];
            var isImplicitRef = sourceChildren is [SyntaxTokenElement { Token.Text: "implicit" },
                SyntaxTokenElement { Token.Kind: TokenKind.RefKeyword },
                SyntaxTokenElement { Token.Kind: TokenKind.Less }, SyntaxNode,
                SyntaxTokenElement { Token.Kind: TokenKind.Greater }, SyntaxTokenElement];
            var isOut = sourceChildren.Length == 3
                && sourceChildren[0] is SyntaxTokenElement passing
                && passing.Token.Kind == TokenKind.OutKeyword;
            if (sourceChildren.Length != 2 && !isOut && !isInit && !isImplicitRef)
            {
                Report("TRM3001", "Passing modes are not supported.", parameter.Span);
            }
            if (isImplicitRef && (!IsSupportedImplicitReferenceType(type) || isExternal || isConstEval))
            {
                Report("TRM3011",
                    "The provisional implicit ref implementation requires a supported scalar or non-unique struct parameter.",
                    parameter.Span);
            }
            if (isOut && returnType != PrimitiveType.Bool
                && type is not (PrimitiveType.Bool or PrimitiveType.Char16 or PrimitiveType.Int32
                    or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
                    or PrimitiveType.Float32 or PrimitiveType.Float64)
                && (!IsStruct(type) || IsUnique(type))
                && !(isNativeVTableMethod && NativeVTableCallContract.IsNativeValueType(type,
                    [.. _nativeFunctionPointerSignatures], [.. _nativePointerTypes])))
            {
                Report("TRM3011",
                    "A non-boolean output parameter requires a scalar or Copy struct type.", parameter.Span);
            }
            if (isInit && type is not (PrimitiveType.Bool or PrimitiveType.Char16 or PrimitiveType.Int32
                or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
                or PrimitiveType.Float32 or PrimitiveType.Float64)
                && !IsStruct(type) && !IsUnique(type) && !ManagedClassTypes.IsManaged(type))
            {
                Report("TRM3011", "Init ref parameters require supported scalar, Copy-struct, unique, or managed-handle outputs.",
                    parameter.Span);
            }
            if (isInit && ManagedClassTypes.IsManaged(type) && !isPrivate && !isInternal)
            {
                Report("TRM3011", "Managed-handle init ref parameters are compilation-private.", parameter.Span);
            }
            if ((isInit || isOut) && TokenSpanContract.IsDependent(type, _structs))
            {
                Report("TRM3023", "Dependency-bearing span and cursor outputs are compilation-local and unsupported.", parameter.Span);
            }
            if (isConstEval && (isOut || isInit || !ConstEvalScalar(type)))
            {
                Report("TRM3024", "ConstEval parameters must be non-output scalar values.", parameter.Span);
            }

            if (!names.Add(token.Name))
            {
                Report("TRM3004", $"Duplicate parameter '{token.Text}'.", token.Span);
            }

            declared.Add(new Parameter(token.Name, type, token.Span,
                isOut || isInit, isInit, isImplicitRef, nativePointeeType,
                isWritableNativePointer, token.Text == "@this"));
        }
        var dependencyContracts = BindDependencyContracts(
            dependencyAttributes, declared.ToImmutable(), returnType,
            containingType, isStatic, constructor, classConstructor,
            [.. nativeOutputConditionsByIndex.Keys], declaration.Span);
        var hasBoundResultDependency = dependencyContracts.Any(static contract =>
            contract.Destination == BoundDependencyDestination.Result
            && contract.Relation is BoundDependencyRelation.DependsOn
                or BoundDependencyRelation.ForwardsBacking);
        var spanResultCandidate = returnType == PrimitiveType.TokenSpan && !constructor
            && body is not null && isInternal
            && (hasResultDependency || hasBoundResultDependency);
        if (returnType is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView)
        {
            Report("TRM3023", "A native buffer view cannot be returned.", returnNode!.Span);
        }
        else if (!constructor && TokenSpanContract.IsDependent(returnType, _structs)
            && !spanResultCandidate)
        {
            Report("TRM3023",
                "A dependency-bearing result requires an explicit relationship or a single eligible default source.",
                returnNode!.Span);
        }
        var (capturedParameters, resultDependencyParameters,
            resultDependencyIncludesReceiver, returnsForwardedBacking) =
            ProjectTokenSpanDependencyMetadata(dependencyContracts, returnType,
                constructor);
        if (constructor && TokenSpanContract.IsCursor(returnType, _structs)
            && capturedParameters.IsEmpty)
        {
            Report("TRM3023",
                "TokenCursor construction must declare its checked backing dependency capture.",
                declaration.Span);
        }
        foreach (var unknownTarget in nativeOutputConditionsByName.Keys)
        {
            Report("TRM3030",
                $"NativeOutput targets unknown parameter '{unknownTarget}'.",
                declaration.Span);
        }
        if (nativeOutputConditionsByIndex.Count != 0 && !isExternal)
        {
            Report("TRM3030", "NativeOutput may appear only on an extern callable.", declaration.Span);
        }

        PrimitiveType? nativeFunctionPointerType = null;
        if (isNativeCallable)
        {
            var nativeSignature = new NativeFunctionPointerSignatureDefinition(NativeCallingConvention.Native,
                returnType, [.. declared.Select(static parameter => parameter.Type)]);
            if (declared.Any(static parameter => parameter.IsOut || parameter.IsInit || parameter.IsImplicitRef
                    || parameter.IsWritableNativePointer)
                || !NativeFunctionPointerContract.IsValidSignature(nativeSignature,
                    [.. _nativeFunctionPointerSignatures]))
            {
                Report("TRM3030",
                    "NativeCallable signatures require ordinary scalar, raw pointer, or nested function pointer values.",
                    declaration.Span);
            }
            else
            {
                nativeFunctionPointerType = GetNativeFunctionPointerType(nativeSignature);
            }
        }

        DirectNativeImport? directNativeImport = null;
        var nativeOutputContracts = ImmutableArray.CreateBuilder<NativeOutputContract>();
        var nativeOutputContractFacts = ImmutableArray<NativeOutputContract>.Empty;
        if (nativeLibraryName is not null && nativeExportName is not null)
        {
            var nativeOutputs = ImmutableArray.CreateBuilder<DirectNativeOutput>();
            var directNativeParameters = ImmutableArray.CreateBuilder<DirectNativeParameter>();
            foreach (var (outputIndex, nonNegative) in nativeOutputConditionsByIndex.OrderBy(
                static pair => pair.Key))
            {
                if (outputIndex >= declared.Count
                    || !declared[outputIndex].IsWritableNativePointer
                    || declared[outputIndex].NativePointeeType is not PrimitiveType outputType
                    || !DirectNativeImportContract.IsNativeOutputCellType(outputType, _structs))
                {
                    Report("TRM3030",
                        "NativeOutput requires a write ptr<T> parameter in the direct native signature.",
                        nativeImportAttribute?.Span ?? declaration.Span);
                    continue;
                }
                var resultCondition = DirectNativeImportContract.ResolveResultCondition(
                    returnType, nonNegative
                        ? DirectNativeImportContract.Int32NonNegativeResultCondition
                        : DirectNativeImportContract.Int32NonZeroResultCondition);
                if (!DirectNativeImportContract.IsValidResultCondition(returnType, resultCondition))
                {
                    Report("TRM3030",
                        "NativeOutput result conditions must be compatible with the callable result type.",
                        nativeImportAttribute?.Span ?? declaration.Span);
                }
                nativeOutputs.Add(new DirectNativeOutput(outputIndex, outputType, resultCondition, 1));
                var (successSelector, complementSelector) = resultCondition switch
                {
                    DirectNativeImportContract.Int32NonZeroResultCondition
                        or DirectNativeImportContract.UInt32NonZeroResultCondition
                        => (NativeOutputSelector.NonZero, NativeOutputSelector.Zero),
                    DirectNativeImportContract.Int32NonNegativeResultCondition
                        => (NativeOutputSelector.NonNegative, NativeOutputSelector.Negative),
                    _ => throw new InvalidOperationException("Validated native output condition is unassigned."),
                };
                nativeOutputContracts.Add(new NativeOutputContract(outputIndex, outputType,
                    NativeOutputFormation.VacantWholeObject,
                    NativeOutputPublicationPhase.NormalCompletion,
                    successSelector, NativeOutputState.Live,
                    complementSelector, NativeOutputState.Vacant,
                    NativeOutputContractValidator.CreateDependencies(
                        dependencyContracts, outputIndex)));
            }
            foreach (var (ordinal, nativeParameter) in nativeParameters.OrderBy(
                static pair => pair.Key))
            {
                var parameterIndex = ordinal - 1;
                if (parameterIndex < 0 || parameterIndex >= declared.Count
                    || declared[parameterIndex].Type != PrimitiveType.NativeRawPointer
                    || declared[parameterIndex].NativePointeeType is not PrimitiveType pointeeType
                    || nativeOutputConditionsByIndex.ContainsKey(parameterIndex))
                {
                    Report("TRM3030",
                        "NativeParameter must identify a non-output raw pointer parameter.",
                        nativeImportAttribute?.Span ?? declaration.Span);
                    continue;
                }
                directNativeParameters.Add(new DirectNativeParameter(parameterIndex, pointeeType,
                    nativeParameter.Access, nativeParameter.Retention, nativeParameter.Extent));
            }
            for (var index = 0; index < declared.Count; index++)
            {
                var parameter = declared[index];
                if (parameter.NativePointeeType is null
                    && parameter.Type != PrimitiveType.NativeStorageAddress
                    && !DirectNativeImportContract.IsValueParameter(parameter.Type,
                        [.. _nativeFunctionPointerSignatures], [.. _nativePointerTypes]))
                {
                    Report("TRM3030",
                        "A direct native import value parameter requires a supported scalar or explicit NativeParameter pointer contract.",
                        parameter.Span);
                }
            }
            if (nativeOutputs.Count > 0
                && returnType is not (PrimitiveType.Int32 or PrimitiveType.UInt32))
            {
                Report("TRM3030",
                    "NativeOutput requires an int32 or uint32 result.",
                    declaration.Span);
            }
            if (nativeOutputConditionsByIndex.Count != nativeOutputs.Count)
            {
                Report("TRM3030", "Every NativeOutput must resolve to one supported pointer parameter.",
                    declaration.Span);
            }
            if (returnType is not (PrimitiveType.Void or PrimitiveType.NativeRawPointer)
                && !DirectNativeImportContract.IsValueParameter(returnType,
                    [.. _nativeFunctionPointerSignatures], [.. _nativePointerTypes]))
            {
                Report("TRM3030",
                    "A direct native import result must have a directly supported scalar, pointer, or function-pointer representation.",
                    declaration.Span);
            }
            requiresUnsafeCaller = true;
            directNativeImport = new DirectNativeImport(nativeLibraryName, nativeExportName,
                nativeOutputs.ToImmutable()) {
                Parameters = directNativeParameters.ToImmutable(),
            };
            nativeOutputContractFacts = nativeOutputContracts.ToImmutable();
        }
        NativeVTableMethodDefinition? nativeVTableMethod = null;
        if (isNativeVTableMethod)
        {
            var nativeVTableParameters = ImmutableArray.CreateBuilder<DirectNativeParameter>();
            if (nativeOutputConditionsByIndex.Count != 0)
            {
                Report("TRM3030",
                    "NativeOutput requires a direct import or an independently admitted foreign interface method.",
                    nativeVTableMethodAttribute?.Span ?? declaration.Span);
            }
            if (nativeLibraryName is not null)
            {
                Report("TRM3030", "NativeVTableMethod cannot also declare a direct NativeImport.",
                    nativeImportAttribute?.Span ?? declaration.Span);
            }
            if (declared.Count == 0 || declared[0].IsOut || declared[0].IsInit
                || declared[0].IsImplicitRef
                || (declared[0].Type is not (PrimitiveType.NativeRawPointer
                    or PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer)
                    && !NativePointerTypeContract.IsValidType(declared[0].Type, [.. _nativePointerTypes])))
            {
                Report("TRM3030",
                    "NativeVTableMethod requires a direct native receiver pointer as its first explicit parameter.",
                    nativeVTableMethodAttribute?.Span ?? declaration.Span);
            }
            foreach (var (ordinal, nativeParameter) in nativeParameters.OrderBy(static pair => pair.Key))
            {
                var parameterIndex = ordinal - 1;
                if (parameterIndex <= 0 || parameterIndex >= declared.Count
                    || declared[parameterIndex].Type != PrimitiveType.NativeRawPointer
                    || declared[parameterIndex].IsOut
                    || nativeOutputConditionsByIndex.ContainsKey(parameterIndex)
                    || declared[parameterIndex].NativePointeeType is not PrimitiveType pointeeType
                    || !DirectNativeImportContract.IsInputPointee(pointeeType, _structs))
                {
                    Report("TRM3030",
                        "NativeParameter must identify a non-receiver raw pointer parameter with a supported pointee type.",
                        nativeVTableMethodAttribute?.Span ?? declaration.Span);
                    continue;
                }
                nativeVTableParameters.Add(new DirectNativeParameter(parameterIndex, pointeeType,
                    nativeParameter.Access, nativeParameter.Retention, nativeParameter.Extent));
            }
            if (returnType != PrimitiveType.Void
                && !DirectNativeImportContract.IsValueParameter(returnType,
                    [.. _nativeFunctionPointerSignatures], [.. _nativePointerTypes]))
            {
                Report("TRM3030",
                    "A NativeVTableMethod result must have a direct native scalar, pointer, or function-pointer representation.",
                    nativeVTableMethodAttribute?.Span ?? declaration.Span);
            }
            for (var index = 0; index < declared.Count; index++)
            {
                var parameter = declared[index];
                if (parameter.IsWritableNativePointer)
                {
                    Report("TRM3030",
                        "NativeOutput is not supported on NativeVTableMethod without an admitted interface method contract.",
                        parameter.Span);
                }
                else if (parameter.IsInit || parameter.IsImplicitRef
                    || (parameter.IsOut
                        ? !DirectNativeImportContract.IsValueParameter(parameter.Type,
                            [.. _nativeFunctionPointerSignatures], [.. _nativePointerTypes])
                        : parameter.Type != PrimitiveType.NativeRawPointer
                            && !DirectNativeImportContract.IsValueParameter(parameter.Type,
                                [.. _nativeFunctionPointerSignatures], [.. _nativePointerTypes])))
                {
                    Report("TRM3030",
                        "NativeVTableMethod parameters require direct native scalar or pointer representations.",
                        parameter.Span);
                }
            }
            nativeVTableMethod = new NativeVTableMethodDefinition(nativeVTableSlot,
                nativeVTableParameters.ToImmutable());
        }
        else if (nativeLibraryName is null && nativeOutputConditionsByIndex.Count != 0)
        {
            Report("TRM3030", "NativeOutput requires a direct import or an independently admitted foreign interface method.",
                declaration.Span);
        }
        if (operatorDeclaration && operatorKey.Arity == 1
            && (containingType is null || !TryResolveStruct(Qualify(containingType), out var ownerType)
                || !IsCopyGenericType(ownerType) || IsUnique(ownerType)
                || declared.Count != 1 || declared[0].Type != ownerType || declared[0].IsOut
                || !IsCopyGenericType(returnType) || IsUnique(returnType)
                || (operatorKey.Operator is TokenKind.PlusPlus or TokenKind.MinusMinus
                    && returnType != ownerType)))
        {
            Report("TRM3007", "A unary static operator requires one Copy operand of its containing struct and a Copy result; update operators must return that exact struct.",
                declaration.Span);
        }

        var qualifiedName = (string.IsNullOrEmpty(_namespace) ? "" : _namespace + ".")
            + (containingType is null ? "" : containingType + ".")
            + (constructor ? "#ctor" : property ? "#get:" + nameText : nameText);
        BoundFunction? externalFunction = null;
        if (hasExternalBody)
        {
            var boundParameters = declared.ToImmutable();
            var declaringType = containingType is null ? null
                : (string.IsNullOrEmpty(_namespace) ? "" : _namespace + ".") + containingType;
            externalFunction = new BoundFunction(qualifiedName, classConstructor ? PrimitiveType.Void : returnType,
                [.. boundParameters.Select(static parameter => new BoundVariable(
                    parameter.Name, parameter.Type, false, parameter.Span) {
                    IsOut = parameter.IsOut,
                    IsInit = parameter.IsInit,
                    IsImplicitRef = parameter.IsImplicitRef,
                })], boundParameters.Length, new BoundBlock([], declaration.Span), declaration.Span) {
                IsExternal = !isNativeVTableMethod,
                IsPrivate = isPrivate,
                IsInternal = isInternal,
                IsStatic = isStatic,
                IsOperator = operatorDeclaration,
                RequiresUnsafeCaller = requiresUnsafeCaller,
                DoesNotReturn = doesNotReturn,
                IsConstructor = constructor,
                IsClassConstructor = classConstructor,
                IsCleanup = isCleanup,
                DeclaringStruct = declaringType,
                DirectNativeImport = directNativeImport,
                NativeOutputRole = nativeOutputContractFacts.IsEmpty
                    ? null : NativeOutputDeclarationRole.DirectImport,
                NativeOutputContracts = nativeOutputContractFacts,
                NativeVTableMethod = nativeVTableMethod,
                Intrinsic = isNativeVTableMethod ? BoundIntrinsic.NativeVTableCall : BoundIntrinsic.None,
                CapturedParameters = capturedParameters,
                ReturnsForwardedBacking = returnsForwardedBacking,
                ResultDependencyParameters = resultDependencyParameters,
                ResultDependencyIncludesReceiver = resultDependencyIncludesReceiver,
                DependencyContracts = dependencyContracts,
            };
        }
        var callableIsPublic = !isPrivate && !isInternal
            && (containingType is null
                || (_structNames.TryGetValue(Qualify(containingType), out var containingStruct)
                    && IsPublicType(containingStruct))
                || (_classNames.TryGetValue(Qualify(containingType), out var containingClass)
                    && IsPublicType(containingClass)));
        if (callableIsPublic
            && (ContainsGenericStructSpecialization(returnType)
                || declared.Any(parameter => ContainsGenericStructSpecialization(parameter.Type))))
        {
            Report("TRM3005",
                "Public callables cannot expose a compilation-local generic struct specialization.",
                declaration.Span);
        }
        if (containingType is not null && Qualify(containingType) == TokenSpanContract.SnapshotName
            && nameText == "AsSpan")
        {
            Report("TRM3003", "TokenSnapshot.AsSpan is a compiler-owned compilation-local projection.", declaration.Span);
        }
        if (!_functions.TryAdd(qualifiedName, _signatures.Count))
        {
            Report("TRM3003", $"Duplicate function '{nameToken.Text}'; overloads are not supported.", nameToken.Span);
        }

        _signatures.Add(new Signature(qualifiedName, classConstructor ? PrimitiveType.Void : returnType,
            declared.ToImmutable(), body, declaration, _tree,
            containingType, constructor && !classConstructor, isPrivate, isInternal, isMutable, isStatic,
            false, doesNotReturn, externalFunction) {
            GenericOwnerTypeArguments = _genericOwnerTypeArguments,
            IsInternal = isInternal || _genericOwnerTypeArguments is not null,
            IsCleanup = isCleanup,
            IsOperator = operatorDeclaration,
            IsClassConstructor = classConstructor,
            PropertyName = property ? nameText : null,
            IsConstEval = isConstEval,
            RequiresUnsafeCaller = requiresUnsafeCaller,
            NativeFunctionPointerType = nativeFunctionPointerType,
            CapturedParameters = capturedParameters,
            ReturnsForwardedBacking = returnsForwardedBacking,
            ResultDependencyParameters = resultDependencyParameters,
            ResultDependencyIncludesReceiver = resultDependencyIncludesReceiver,
            DependencyContracts = dependencyContracts,
        });
    }

    private ImmutableArray<BoundDependencyContract> BindDependencyContracts(
        IReadOnlyList<(SyntaxNode Attribute, int? InlineParameterOrdinal)> attributes,
        ImmutableArray<Parameter> parameters, PrimitiveType returnType,
        string? containingType, bool isStatic, bool constructor, bool classConstructor,
        HashSet<int> nativeOutputParameters, TextSpan declarationSpan)
    {
        var contracts = ImmutableArray.CreateBuilder<BoundDependencyContract>();
        var contractSpans = new List<TextSpan>();
        var seenTargets = new HashSet<(BoundDependencyRelation,
            BoundDependencyDestination, int?)>();
        foreach (var (attribute, inlineParameterOrdinal) in attributes)
        {
            if (!TryGetDependencyRelation(AttributeName(attribute), out var relation))
            {
                continue;
            }

            var target = GetDependencyAttributeTarget(attribute);
            if (inlineParameterOrdinal is int inlineParameter)
            {
                if (target.Kind != DependencyAttributeTargetKind.Default)
                {
                    Report("TRM3023",
                        "A parameter-local dependency annotation cannot declare a second target.",
                        attribute.Span);
                    continue;
                }

                if ((uint)inlineParameter >= (uint)parameters.Length)
                {
                    Report("TRM3023", "Dependency annotation targets an invalid parameter.",
                        attribute.Span);
                    continue;
                }
                target = new DependencyAttributeTarget(
                    DependencyAttributeTargetKind.Parameter,
                    parameters[inlineParameter].Name);
            }

            if (!TryResolveDependencyDestination(relation, target,
                inlineParameterOrdinal.HasValue, parameters, returnType,
                containingType, isStatic, constructor, classConstructor,
                nativeOutputParameters,
                attribute.Span, out var destination, out var destinationOrdinal))
            {
                continue;
            }

            var hasReceiver = containingType is not null && !isStatic
                && !constructor && !classConstructor;
            if (!TryBindDependencySources(attribute, parameters, hasReceiver,
                out var sources))
            {
                continue;
            }
            if (sources.Any(source => source.Kind == BoundDependencySourceKind.Formal
                && nativeOutputParameters.Contains(source.FormalOrdinal!.Value)))
            {
                Report("TRM3023",
                    "A native output formal cannot be used as a dependency source.",
                    attribute.Span);
                continue;
            }
            if (relation == BoundDependencyRelation.ForwardsBacking && sources.IsEmpty)
            {
                Report("TRM3023", "ForwardsBacking requires at least one source.",
                    attribute.Span);
                continue;
            }
            if (!seenTargets.Add((relation, destination, destinationOrdinal)))
            {
                Report("TRM3023",
                    "A dependency schema cannot repeat the same effective target.",
                    attribute.Span);
                continue;
            }

            contracts.Add(new BoundDependencyContract(relation, destination,
                destinationOrdinal, sources));
            contractSpans.Add(attribute.Span);
        }

        var hasDeclaredResultDependency = attributes.Any(dependency =>
            dependency.InlineParameterOrdinal is null
            && TryGetDependencyRelation(AttributeName(dependency.Attribute),
                out var relation)
            && relation == BoundDependencyRelation.DependsOn
            && GetDependencyAttributeTarget(dependency.Attribute).Kind
                is DependencyAttributeTargetKind.Default
                    or DependencyAttributeTargetKind.Return);
        if (returnType == PrimitiveType.TokenSpan && !constructor
            && !hasDeclaredResultDependency)
        {
            var eligibleSources = ImmutableArray.CreateBuilder<BoundDependencySource>();
            if (containingType is not null && !isStatic && !classConstructor)
            {
                eligibleSources.Add(new BoundDependencySource(
                    BoundDependencySourceKind.Receiver, null));
            }
            for (var parameter = 0; parameter < parameters.Length; parameter++)
            {
                if (IsEligibleDependencySource(parameters[parameter]))
                {
                    eligibleSources.Add(new BoundDependencySource(
                        BoundDependencySourceKind.Formal, parameter));
                }
            }

            if (eligibleSources.Count == 1)
            {
                contracts.Add(new BoundDependencyContract(
                    BoundDependencyRelation.DependsOn,
                    BoundDependencyDestination.Result, null,
                    eligibleSources.ToImmutable()));
                contractSpans.Add(declarationSpan);
            }
            else if (eligibleSources.Count > 1)
            {
                Report("TRM3023",
                    "A view result with multiple eligible sources requires an explicit DependsOn set.",
                    declarationSpan);
            }
        }

        var boundContracts = contracts.ToImmutable();
        for (var contractIndex = 0; contractIndex < boundContracts.Length; contractIndex++)
        {
            var forwarding = boundContracts[contractIndex];
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
            if (!hasRelationship || forwarding.Sources.Any(
                source => !permittedSources.Contains(source)))
            {
                Report("TRM3023",
                    "ForwardsBacking requires an existing dependency or capture relationship and cannot add sources.",
                    contractSpans[contractIndex]);
            }
        }

        return boundContracts;
    }

    private bool TryResolveDependencyDestination(
        BoundDependencyRelation relation, DependencyAttributeTarget target,
        bool inlineParameter, ImmutableArray<Parameter> parameters,
        PrimitiveType returnType, string? containingType, bool isStatic,
        bool constructor, bool classConstructor,
        HashSet<int> nativeOutputParameters, TextSpan span,
        out BoundDependencyDestination destination, out int? destinationOrdinal)
    {
        destination = default;
        destinationOrdinal = null;
        var hasReceiver = containingType is not null && !isStatic
            && !classConstructor;
        var hasResult = !constructor && !classConstructor
            && returnType != PrimitiveType.Void;

        if (inlineParameter)
        {
            var parameterOrdinal = FindParameterOrdinal(target.ParameterName, parameters);
            if (parameterOrdinal < 0
                || relation is not (BoundDependencyRelation.DependsOn
                    or BoundDependencyRelation.ForwardsBacking)
                || (!parameters[parameterOrdinal].IsInit
                    && !nativeOutputParameters.Contains(parameterOrdinal)))
            {
                Report("TRM3023",
                    "An inline DependsOn or ForwardsBacking annotation must target an init parameter.",
                    span);
                return false;
            }
            destination = BoundDependencyDestination.InitializedOutput;
            destinationOrdinal = parameterOrdinal;
            return true;
        }

        switch (target.Kind)
        {
            case DependencyAttributeTargetKind.Default:
                if (relation == BoundDependencyRelation.DependsOn)
                {
                    if (!hasResult)
                    {
                        Report("TRM3023", "DependsOn requires a result or init-output target.",
                            span);
                        return false;
                    }
                    destination = BoundDependencyDestination.Result;
                    return true;
                }
                if (relation == BoundDependencyRelation.Captures)
                {
                    if ((constructor && !classConstructor) || hasReceiver)
                    {
                        destination = BoundDependencyDestination.Receiver;
                        return true;
                    }
                    Report("TRM3023",
                        "Captures requires an explicit result or retained-formal target on a static or top-level callable.",
                        span);
                    return false;
                }
                if (constructor && !classConstructor)
                {
                    destination = BoundDependencyDestination.Receiver;
                    return true;
                }
                if (hasResult)
                {
                    destination = BoundDependencyDestination.Result;
                    return true;
                }
                Report("TRM3023",
                    "ForwardsBacking requires a result, constructor receiver or explicit destination.",
                    span);
                return false;

            case DependencyAttributeTargetKind.Return:
                if (relation == BoundDependencyRelation.Captures
                    ? !hasResult || !CanCaptureValue(returnType)
                    : !hasResult)
                {
                    Report("TRM3023",
                        "The return target is incompatible with this dependency schema or result type.",
                        span);
                    return false;
                }
                destination = BoundDependencyDestination.Result;
                return true;

            case DependencyAttributeTargetKind.Receiver:
                if (relation == BoundDependencyRelation.DependsOn
                    || !((constructor && !classConstructor) || hasReceiver))
                {
                    Report("TRM3023",
                        "The receiver target is incompatible with this declaration or dependency schema.",
                        span);
                    return false;
                }
                destination = BoundDependencyDestination.Receiver;
                return true;

            case DependencyAttributeTargetKind.Parameter:
                var formalOrdinal = FindParameterOrdinal(target.ParameterName, parameters);
                if (formalOrdinal < 0)
                {
                    Report("TRM3023",
                        $"Dependency annotation targets unknown parameter '{target.ParameterName}'.",
                        span);
                    return false;
                }
                var formal = parameters[formalOrdinal];
                if (relation == BoundDependencyRelation.DependsOn)
                {
                    if (!formal.IsInit && !nativeOutputParameters.Contains(formalOrdinal))
                    {
                        Report("TRM3023",
                            "A parameter-targeted DependsOn annotation requires an init or native output.",
                            span);
                        return false;
                    }
                    destination = BoundDependencyDestination.InitializedOutput;
                }
                else if (relation == BoundDependencyRelation.Captures)
                {
                    if (nativeOutputParameters.Contains(formalOrdinal))
                    {
                        Report("TRM3023",
                            "NativeOutput formals are not retained holder destinations.",
                            span);
                        return false;
                    }
                    if (formal.IsInit)
                    {
                        destination = BoundDependencyDestination.InitializedOutput;
                    }
                    else if (!CanCaptureParameter(formal))
                    {
                        Report("TRM3023",
                            "Captures requires a retained formal destination.",
                            span);
                        return false;
                    }
                    else
                    {
                        destination = BoundDependencyDestination.RetainedFormal;
                    }
                }
                else if (formal.IsInit || nativeOutputParameters.Contains(formalOrdinal))
                {
                    destination = BoundDependencyDestination.InitializedOutput;
                }
                else if (CanCaptureParameter(formal))
                {
                    destination = BoundDependencyDestination.RetainedFormal;
                }
                else
                {
                    Report("TRM3023",
                        "ForwardsBacking requires an initialized output or retained formal destination.",
                        span);
                    return false;
                }
                destinationOrdinal = formalOrdinal;
                return true;

            case DependencyAttributeTargetKind.Operation:
                Report("TRM3023",
                    "The method target requires an admitted completion-operation relationship.",
                    span);
                return false;

            default:
                Report("TRM3023", "Dependency annotation target is unsupported.", span);
                return false;
        }
    }

    private bool TryBindDependencySources(SyntaxNode attribute,
        ImmutableArray<Parameter> parameters, bool hasReceiver,
        out ImmutableArray<BoundDependencySource> sources)
    {
        sources = [];
        var children = attribute.Children;
        var nameStart = AttributeNameStart(attribute);
        var argumentOpen = -1;
        for (var index = nameStart; index < children.Length; index++)
        {
            if (children[index] is SyntaxTokenElement
                { Token.Kind: TokenKind.OpenParen })
            {
                argumentOpen = index;
                break;
            }
        }
        if (argumentOpen < 0 || children.Length < 2
            || children[^2] is not SyntaxTokenElement
                { Token.Kind: TokenKind.CloseParen, Token.IsMissing: false }
            || children[^1] is not SyntaxTokenElement
                { Token.Kind: TokenKind.CloseBracket, Token.IsMissing: false })
        {
            Report("TRM3023",
                "A dependency schema requires a parenthesized flat source list.",
                attribute.Span);
            return false;
        }

        var indexInList = argumentOpen + 1;
        var end = children.Length - 2;
        if (indexInList == end)
        {
            return true;
        }

        var result = ImmutableArray.CreateBuilder<BoundDependencySource>();
        var seen = new HashSet<BoundDependencySource>();
        while (indexInList < end)
        {
            BoundDependencySource source;
            var sourceSpan = attribute.Span;
            if (children[indexInList] is SyntaxTokenElement
                { Token.Kind: TokenKind.Identifier, Token.Text: "this" } receiver)
            {
                sourceSpan = receiver.Token.Span;
                if (!hasReceiver)
                {
                    Report("TRM3023",
                        "The direct this source requires an instance receiver.",
                        sourceSpan);
                    return false;
                }
                source = new BoundDependencySource(
                    BoundDependencySourceKind.Receiver, null);
                indexInList++;
            }
            else if (children[indexInList] is SyntaxTokenElement
                { Token.Kind: TokenKind.StringLiteral } literal)
            {
                sourceSpan = literal.Token.Span;
                if (!StringLiteralDecoder.TryDecode(literal.Token.Text.AsSpan(),
                    out var parameterName))
                {
                    Report("TRM3023",
                        "A dependency source literal must decode to one exact formal name.",
                        sourceSpan);
                    return false;
                }
                var formalOrdinal = FindParameterOrdinal(parameterName, parameters);
                if (formalOrdinal < 0)
                {
                    Report("TRM3023",
                        $"Dependency annotation names unknown parameter '{parameterName}'.",
                        sourceSpan);
                    return false;
                }
                source = new BoundDependencySource(
                    BoundDependencySourceKind.Formal, formalOrdinal);
                indexInList++;
            }
            else if (children[indexInList] is SyntaxTokenElement
                    { Token.Kind: TokenKind.Identifier, Token.Text: "nameof" }
                && indexInList + 3 < end
                && children[indexInList + 1] is SyntaxTokenElement
                    { Token.Kind: TokenKind.OpenParen }
                && children[indexInList + 2] is SyntaxTokenElement
                    { Token.Kind: TokenKind.Identifier } selector
                && children[indexInList + 3] is SyntaxTokenElement
                    { Token.Kind: TokenKind.CloseParen })
            {
                sourceSpan = selector.Token.Span;
                var formalOrdinal = FindParameterOrdinal(
                    selector.Token.Name, parameters);
                if (formalOrdinal < 0)
                {
                    Report("TRM3023",
                        $"Dependency annotation names unknown parameter '{selector.Token.Name}'.",
                        sourceSpan);
                    return false;
                }
                source = new BoundDependencySource(
                    BoundDependencySourceKind.Formal, formalOrdinal);
                indexInList += 4;
            }
            else
            {
                Report("TRM3023",
                    "A dependency source must be this, nameof(parameter), or a literal formal name.",
                    sourceSpan);
                return false;
            }

            if (!seen.Add(source))
            {
                Report("TRM3023",
                    "A dependency source cannot be listed more than once.",
                    sourceSpan);
                return false;
            }
            result.Add(source);

            if (indexInList < end)
            {
                if (children[indexInList] is not SyntaxTokenElement
                    { Token.Kind: TokenKind.Comma })
                {
                    Report("TRM3023",
                        "Dependency source recipes must be separated by commas.",
                        attribute.Span);
                    return false;
                }
                indexInList++;
                if (indexInList == end)
                {
                    Report("TRM3023",
                        "A dependency source list cannot end with a comma.",
                        attribute.Span);
                    return false;
                }
            }
        }

        sources = result.ToImmutable();
        return true;
    }

    private bool CanCaptureParameter(Parameter parameter)
    {
        if (parameter.IsOut || parameter.IsImplicitRef
            || IsUnique(parameter.Type) || ManagedClassTypes.IsManaged(parameter.Type))
        {
            return true;
        }
        if (!NativePointerTypes.IsType(parameter.Type))
        {
            return false;
        }
        var pointerIndex = NativePointerTypes.Index(parameter.Type);
        if ((uint)pointerIndex >= (uint)_nativePointerTypes.Count)
        {
            return false;
        }
        var pointer = _nativePointerTypes[pointerIndex];
        if (!pointer.IsWritable)
        {
            return false;
        }

        var pending = new Stack<PrimitiveType>();
        var visited = new HashSet<PrimitiveType>();
        pending.Push(pointer.PointeeType);
        while (pending.TryPop(out var current))
        {
            if (current == PrimitiveType.NativeRawPointer || NativePointerTypes.IsType(current))
            {
                return true;
            }
            var structureIndex = (int)current - 100;
            if (structureIndex < 0 || structureIndex >= _structs.Count
                || !visited.Add(current))
            {
                continue;
            }
            foreach (var field in _structs[structureIndex].Fields)
            {
                pending.Push(field.Type);
            }
        }
        return false;
    }

    private bool IsEligibleDependencySource(Parameter parameter)
        => !parameter.IsOut && !parameter.IsInit
            && (parameter.IsImplicitRef || CarriesDependencySource(parameter.Type));

    private bool CarriesDependencySource(PrimitiveType type)
    {
        if (CarriesDependencySourceDirectly(type))
        {
            return true;
        }
        if ((int)type < 100 || (int)type - 100 >= _structs.Count)
        {
            return false;
        }

        var pending = new Stack<PrimitiveType>();
        var seen = new HashSet<PrimitiveType>();
        pending.Push(type);
        while (pending.TryPop(out var current))
        {
            if (!seen.Add(current))
            {
                continue;
            }

            var structureIndex = (int)current - 100;
            if ((uint)structureIndex >= (uint)_structs.Count)
            {
                continue;
            }
            foreach (var field in _structs[structureIndex].Fields)
            {
                if (CarriesDependencySourceDirectly(field.Type))
                {
                    return true;
                }
                if ((int)field.Type >= 100)
                {
                    pending.Push(field.Type);
                }
            }
        }
        return false;
    }

    private bool CarriesDependencySourceDirectly(PrimitiveType type)
        => type is PrimitiveType.ReadonlyInt32BufferView
                or PrimitiveType.MutableInt32BufferView
                or PrimitiveType.ManagedNode
                or PrimitiveType.OptionalManagedNode
                or PrimitiveType.WriteManagedNode
                or PrimitiveType.OptionalWriteManagedNode
            || TokenSpanContract.IsDependent(type, _structs)
            || ManagedClassTypes.IsManaged(type)
            || IsUnique(type);

    private bool CanCaptureValue(PrimitiveType type)
        => IsUnique(type) || ManagedClassTypes.IsManaged(type);

    private static int FindParameterOrdinal(string? parameterName,
        ImmutableArray<Parameter> parameters)
    {
        if (parameterName is null)
        {
            return -1;
        }

        for (var parameter = 0; parameter < parameters.Length; parameter++)
        {
            if (parameters[parameter].Name == parameterName)
            {
                return parameter;
            }
        }
        return -1;
    }

    private (ImmutableArray<int> CapturedParameters,
        ImmutableArray<int> ResultDependencyParameters,
        bool ResultDependencyIncludesReceiver, bool ReturnsForwardedBacking)
        ProjectTokenSpanDependencyMetadata(
            ImmutableArray<BoundDependencyContract> contracts,
            PrimitiveType returnType, bool constructor)
    {
        ImmutableArray<int> capturedParameters = [];
        if (constructor && TokenSpanContract.IsCursor(returnType, _structs))
        {
            var capture = contracts.FirstOrDefault(static contract =>
                contract.Relation == BoundDependencyRelation.Captures
                && contract.Destination == BoundDependencyDestination.Receiver);
            if (capture is not null)
            {
                capturedParameters = [
                    .. capture.Sources
                    .Where(static source => source.Kind == BoundDependencySourceKind.Formal)
                    .Select(static source => source.FormalOrdinal!.Value + 1),
                ];
            }
        }

        if (returnType != PrimitiveType.TokenSpan)
        {
            return (capturedParameters, [], false, false);
        }

        var resultDependency = contracts.FirstOrDefault(static contract =>
            contract.Relation == BoundDependencyRelation.DependsOn
            && contract.Destination == BoundDependencyDestination.Result)
            ?? contracts.FirstOrDefault(static contract =>
                contract.Relation == BoundDependencyRelation.ForwardsBacking
                && contract.Destination == BoundDependencyDestination.Result);
        if (resultDependency is null)
        {
            return (capturedParameters, [], false, false);
        }

        var parameters = resultDependency.Sources
            .Where(static source => source.Kind == BoundDependencySourceKind.Formal)
            .Select(static source => source.FormalOrdinal!.Value)
            .ToImmutableArray();
        var receiver = resultDependency.Sources.Any(static source =>
            source.Kind == BoundDependencySourceKind.Receiver);
        var forwards = returnType == PrimitiveType.TokenSpan
            && (parameters.Length != 0 || receiver);
        return (capturedParameters, parameters, receiver, forwards);
    }

    private PrimitiveType BindType(SyntaxNode node, bool allowVoid)
    {
        var name = TypeNameIdentifier(node);
        var writable = node.Children[0] is SyntaxTokenElement { Token.Kind: TokenKind.WriteKeyword };
        var optional = node.Children[^1] is SyntaxTokenElement { Token.Kind: TokenKind.Question };
        var typeArgumentNodes = node.Children.OfType<SyntaxNode>().ToArray();
        if (name == "fnptr")
        {
            if (typeArgumentNodes.Length < 2 || writable || optional
                || node.Children[^1] is SyntaxTokenElement { Token.Kind: TokenKind.QuestionQuestion }
                || TypeNameIdentifier(typeArgumentNodes[0]) is not ("NativeAbi" or "Terrum.Interop.NativeAbi")
                || typeArgumentNodes[0].Children.OfType<SyntaxNode>().Any())
            {
                Report("TRM3005",
                    "A native function pointer type requires fnptr<NativeAbi, parameter types..., return type>.",
                    node.Span);
                return PrimitiveType.Int32;
            }

            var diagnosticCount = DiagnosticCount;
            var parameterTypes = ImmutableArray.CreateBuilder<PrimitiveType>(typeArgumentNodes.Length - 2);
            for (var index = 1; index < typeArgumentNodes.Length - 1; index++)
            {
                parameterTypes.Add(BindType(typeArgumentNodes[index], allowVoid: false));
            }
            var returnType = BindType(typeArgumentNodes[^1], allowVoid: true);
            var signature = new NativeFunctionPointerSignatureDefinition(
                NativeCallingConvention.Native, returnType, parameterTypes.MoveToImmutable());
            if (DiagnosticCount != diagnosticCount
                || !NativeFunctionPointerContract.IsValidSignature(signature, [.. _nativeFunctionPointerSignatures]))
            {
                Report("TRM3005",
                    "Native function pointer signatures support only scalar, raw pointer, and nested function pointer values.",
                    node.Span);
                return PrimitiveType.Int32;
            }
            return GetNativeFunctionPointerType(signature);
        }
        if (name == "ptr")
        {
            if (typeArgumentNodes.Length != 1 || optional
                || node.Children[^1] is SyntaxTokenElement { Token.Kind: TokenKind.QuestionQuestion })
            {
                Report("TRM3005", "A native pointer type requires ptr<T> or write ptr<T>.", node.Span);
                return PrimitiveType.Int32;
            }
            if (TypeNameIdentifier(typeArgumentNodes[0]) == "void"
                && typeArgumentNodes[0].Children.Length == 1)
            {
                return writable ? GetNativePointerType(PrimitiveType.Void, isWritable: true, span: node.Span)
                    : PrimitiveType.NativeRawPointer;
            }
            var diagnosticCount = DiagnosticCount;
            var pointeeType = BindType(typeArgumentNodes[0], allowVoid: false);
            if (DiagnosticCount != diagnosticCount)
            {
                return PrimitiveType.Int32;
            }
            return GetNativePointerType(pointeeType, writable, node.Span);
        }
        if (name is "Span" or "Terrum.Span")
        {
            if (typeArgumentNodes.Length != 1 || writable || optional
                || node.Children[^1] is SyntaxTokenElement { Token.Kind: TokenKind.QuestionQuestion })
            {
                Report("TRM3005", "The compilation-local span profile requires readonly Span<Token>.", node.Span);
                return PrimitiveType.Int32;
            }
            var element = BindType(typeArgumentNodes[0], allowVoid: false);
            if (!IsStruct(element) || _structs[(int)element - 100].Name != TokenSpanContract.TokenName)
            {
                Report("TRM3005", "Only the closed Copy Token span is available in this provisional profile.", node.Span);
                return PrimitiveType.Int32;
            }
            var context = CaptureContext();
            DeclareStruct((int)element - 100);
            RestoreContext(context);
            if (!TokenSpanContract.ValidTokenLayout(_structs))
            {
                Report("TRM3005", "Token spans require the verified 0/4/8 field offsets, 12-byte stride and four-byte alignment.",
                    node.Span);
            }
            return PrimitiveType.TokenSpan;
        }
        PrimitiveType? type = null;
        var resolution = DeclaredTypeResolution.Missing;
        if (TryGetAliasTypeArgument(name, out var aliasType))
        {
            if (typeArgumentNodes.Length != 0)
            {
                Report("TRM3005", $"Type parameter '{name}' cannot take type arguments.", node.Span);
                return PrimitiveType.Int32;
            }
            type = aliasType;
            resolution = DeclaredTypeResolution.Unique;
        }
        else if (_genericContext is GenericInstantiation generic
            && generic.Template.ParameterIndex(name) is var parameterIndex
            && parameterIndex >= 0)
        {
            if (typeArgumentNodes.Length != 0)
            {
                Report("TRM3005", $"Type parameter '{name}' cannot take type arguments.", node.Span);
                return PrimitiveType.Int32;
            }
            type = generic.Types[parameterIndex];
            resolution = DeclaredTypeResolution.Unique;
        }
        else
        {
            ImmutableArray<PrimitiveType>? typeArguments = null;
            if (typeArgumentNodes.Length != 0)
            {
                var diagnosticCount = DiagnosticCount;
                var arguments = ImmutableArray.CreateBuilder<PrimitiveType>(typeArgumentNodes.Length);
                foreach (var argumentNode in typeArgumentNodes)
                {
                    arguments.Add(BindType(argumentNode, allowVoid: false));
                }
                if (DiagnosticCount != diagnosticCount)
                {
                    return PrimitiveType.Int32;
                }
                typeArguments = arguments.MoveToImmutable();
            }
            resolution = ResolveTypeName(name, typeArguments, node.Span, out var resolved);
            type = typeArguments is null && TryPrimitiveType(name, out var primitive) ? primitive : name switch {
                "ReadonlyInt32BufferView" when typeArguments is null => PrimitiveType.ReadonlyInt32BufferView,
                "MutableInt32BufferView" when typeArguments is null => PrimitiveType.MutableInt32BufferView,
                "ManagedNode" when typeArguments is null => PrimitiveType.ManagedNode,
                "void" when allowVoid && typeArguments is null => PrimitiveType.Void,
                _ => resolution == DeclaredTypeResolution.Unique ? resolved : (PrimitiveType?)null,
            };
        }

        if (type is null && resolution != DeclaredTypeResolution.Invalid)
        {
            Report("TRM3005", resolution == DeclaredTypeResolution.Ambiguous
                ? $"Type '{name}' is ambiguous across declared and imported types."
                : $"Type '{name}' is not supported here.", node.Span);
        }
        if (type == PrimitiveType.Void && !allowVoid)
        {
            Report("TRM3005", $"Type alias '{name}' cannot be used as a value type.", node.Span);
            return PrimitiveType.Int32;
        }
        if (writable && type is PrimitiveType resolvedType)
        {
            if (resolvedType == PrimitiveType.ManagedNode
                || (IsClass(resolvedType) && _classes[ManagedClassTypes.ClassIndex(resolvedType)].IsMutable))
            {
                type = ManagedClassTypes.Writable(resolvedType);
            }
            else
            {
                Report("TRM3005", "write requires a mutable managed class or ManagedNode handle.", node.Span);
            }
        }
        if (optional && type is PrimitiveType required)
        {
            if (!ManagedClassTypes.IsManaged(required) || ManagedClassTypes.IsOptional(required))
            {
                Report("TRM3005", "Only managed class and ManagedNode handles can be optional.", node.Span);
            }
            else
            {
                type = ManagedClassTypes.Optional(required);
            }
        }

        return type ?? PrimitiveType.Int32;
    }

    private PrimitiveType GetNativeFunctionPointerType(
        NativeFunctionPointerSignatureDefinition signature)
    {
        for (var index = 0; index < _nativeFunctionPointerSignatures.Count; index++)
        {
            var existing = _nativeFunctionPointerSignatures[index];
            if (existing.CallingConvention == signature.CallingConvention
                && existing.ReturnType == signature.ReturnType
                && existing.ParameterTypes.SequenceEqual(signature.ParameterTypes))
            {
                return NativeFunctionPointerTypes.Type(index);
            }
        }

        var type = NativeFunctionPointerTypes.Type(_nativeFunctionPointerSignatures.Count);
        _nativeFunctionPointerSignatures.Add(signature);
        return type;
    }

    private PrimitiveType GetNativePointerType(PrimitiveType pointeeType, bool isWritable, TextSpan span)
    {
        for (var index = 0; index < _nativePointerTypes.Count; index++)
        {
            var existing = _nativePointerTypes[index];
            if (existing.PointeeType == pointeeType && existing.IsWritable == isWritable)
            {
                return NativePointerTypes.Type(index);
            }
        }

        if (_nativePointerTypes.Count >= SemanticVerifier.MaximumAnalysisVisits)
        {
            Report("TRM3005", "The compilation exceeds the native pointer type limit.", span);
            return PrimitiveType.Int32;
        }
        var type = NativePointerTypes.Type(_nativePointerTypes.Count);
        _nativePointerTypes.Add(new NativePointerTypeDefinition(pointeeType, isWritable));
        return type;
    }

    private bool TryGetAliasTypeArgument(string name, out PrimitiveType type)
    {
        for (var index = _aliasTypeArguments.Count - 1; index >= 0; index--)
        {
            if (_aliasTypeArguments[index].TryGetValue(name, out type))
            {
                return true;
            }
        }
        type = default;
        return false;
    }

    private static string TypeNameIdentifier(SyntaxNode node)
    {
        var builder = new StringBuilder();
        foreach (var child in node.Children)
        {
            if (child is not SyntaxTokenElement element
                || element.Token.Kind is TokenKind.Less or TokenKind.Question or TokenKind.QuestionQuestion)
            {
                break;
            }
            if (element.Token.Kind is TokenKind.Identifier or TokenKind.VoidKeyword)
            {
                _ = builder.Append(element.Token.Name);
            }
            else if (element.Token.Kind == TokenKind.Dot)
            {
                _ = builder.Append('.');
            }
        }
        return builder.ToString();
    }

    private bool IsUnique(PrimitiveType type) =>
        type == PrimitiveType.OwnedInt32Buffer
            || ((int)type >= 100 && (int)type - 100 < _structs.Count
                && _structs[(int)type - 100].IsUnique);

    private string? CleanupMethod(PrimitiveType type) =>
        type == PrimitiveType.OwnedInt32Buffer ? null : _structs[(int)type - 100].CleanupMethod;

    private BoundExpression BindReceiverLoan(SyntaxNode receiver, PrimitiveType type)
    {
        while (receiver.Kind == SyntaxKind.ParenthesizedExpression)
        {
            receiver = (SyntaxNode)receiver.Children[1];
        }
        if (receiver.Kind == SyntaxKind.NameExpression
            && TryFindVariable(((SyntaxTokenElement)receiver.Children[0]).Token, out var slot)
            && _variables[slot].Type == type)
        {
            return new BoundReceiverLoan(slot, type, receiver.Span);
        }
        var place = BindExpression(receiver, type);
        if (place is BoundFieldRead field && IsUnique(field.Type))
        {
            return new BoundFieldLoan(field, type, receiver.Span);
        }
        Report("TRM3023", "A unique receiver loan requires a named owner or initialized field place.", receiver.Span);
        return ErrorExpression(receiver.Span);
    }

    private BoundExpression BindMutableReceiverLoan(SyntaxNode receiver, PrimitiveType type, TextSpan span,
        bool allowBufferField = false)
    {
        while (receiver.Kind == SyntaxKind.ParenthesizedExpression)
        {
            receiver = (SyntaxNode)receiver.Children[1];
        }
        if (receiver.Kind == SyntaxKind.NameExpression
            && TryFindVariable(((SyntaxTokenElement)receiver.Children[0]).Token, out var slot)
            && _variables[slot].Type == type && _variables[slot].IsMutable)
        {
            return new BoundMutableReceiverLoan(slot, type, span);
        }
        if (receiver.Kind is SyntaxKind.MemberAccessExpression or SyntaxKind.NameExpression)
        {
            var place = BindExpression(receiver, type);
            if (place is BoundFieldRead leaf && leaf.Type == type
                && (IsStruct(type) || (allowBufferField && type == PrimitiveType.OwnedInt32Buffer)))
            {
                BoundExpression current = leaf;
                while (current is BoundFieldRead field)
                {
                    var owner = field.Receiver.Type;
                    if (!IsStruct(owner) || !GetField(owner, field.Field).IsMutable)
                    {
                        break;
                    }
                    current = field.Receiver;
                }
                if (current is BoundRead root && root.Variable >= 0 && root.Variable < _variables.Count
                    && root.Type == _variables[root.Variable].Type && _variables[root.Variable].IsMutable)
                {
                    return new BoundMutableFieldLoan(leaf, type, span);
                }
            }
        }
        Report("TRM3023", "Mutation requires a named mutable receiver or mutable struct-field place.", span);
        return ErrorExpression(span);
    }

    private BoundViewScope BindViewScope(SyntaxToken name, bool isMutable, SyntaxNode initializer, TextSpan span,
        PrimitiveType viewType)
    {
        var writable = viewType == PrimitiveType.MutableInt32BufferView;
        if (initializer.Kind == SyntaxKind.IndexExpression
            && ((SyntaxNode)initializer.Children[2]).Kind == SyntaxKind.SliceRangeExpression)
        {
            return BindRangeViewScope(name, isMutable, initializer, span, viewType);
        }

        if (isMutable != writable || initializer.Kind != SyntaxKind.InvocationExpression
            || initializer.Children[0] is not SyntaxNode { Kind: SyntaxKind.MemberAccessExpression } callee
            || ((SyntaxTokenElement)callee.Children[2]).Token.Text != (writable ? "SliceMutable" : "Slice")
            || KnownType((SyntaxNode)callee.Children[0]) != PrimitiveType.OwnedInt32Buffer)
        {
            Report("TRM3023", writable
                ? "A mutable native buffer view requires a mut local initialized by owner.SliceMutable(start, length)."
                : "A readonly native buffer view requires a readonly local initialized by owner.Slice(start, length).", span);
            var invalid = AddVariable(name.Name, viewType, isMutable, name.Span);
            return new BoundViewScope(invalid, ErrorExpression(span), ErrorExpression(span), ErrorExpression(span),
                new BoundBlock([], span), span) { IsMutable = writable };
        }

        if (TryGetTwoPositionalArguments((SyntaxNode)initializer.Children[1]) is not { } arguments)
        {
            Report("TRM3011", "Slice requires two positional int32 arguments.", initializer.Span);
            var invalid = AddVariable(name.Name, viewType, isMutable, name.Span);
            return new BoundViewScope(invalid, ErrorExpression(span), ErrorExpression(span), ErrorExpression(span),
                new BoundBlock([], span), span) { IsMutable = writable };
        }

        var owner = writable ? BindMutableReceiverLoan((SyntaxNode)callee.Children[0],
                PrimitiveType.OwnedInt32Buffer, callee.Span, allowBufferField: true)
            : BindReceiverLoan((SyntaxNode)callee.Children[0], PrimitiveType.OwnedInt32Buffer);
        var start = BindExpression(arguments.First, PrimitiveType.Int32);
        var length = BindExpression(arguments.Second, PrimitiveType.Int32);
        var variable = AddVariable(name.Name, viewType, isMutable, name.Span);
        return new BoundViewScope(variable, owner, start, length, new BoundBlock([], span), span) {
            IsMutable = writable,
        };
    }

    private static (SyntaxNode First, SyntaxNode Second)? TryGetTwoPositionalArguments(SyntaxNode argumentList)
    {
        SyntaxNode? first = null;
        SyntaxNode? second = null;
        var count = 0;
        foreach (var child in argumentList.Children)
        {
            if (child is not SyntaxNode argument)
            {
                continue;
            }
            if (argument.Children is not [SyntaxNode expression])
            {
                return null;
            }
            if (count == 0)
            {
                first = expression;
            }
            else if (count == 1)
            {
                second = expression;
            }
            else
            {
                return null;
            }
            count++;
        }
        return count == 2 && first is not null && second is not null ? (first, second) : null;
    }

    private static SyntaxNode? TryGetSingleSyntaxArgument(SyntaxNode argumentList)
    {
        SyntaxNode? argument = null;
        foreach (var child in argumentList.Children)
        {
            if (child is not SyntaxNode syntaxNode)
            {
                continue;
            }
            if (argument is not null)
            {
                return null;
            }
            argument = syntaxNode;
        }
        return argument;
    }

    private BoundViewScope BindRangeViewScope(SyntaxToken name, bool isMutable, SyntaxNode initializer,
        TextSpan span, PrimitiveType viewType)
    {
        if (isMutable || viewType != PrimitiveType.ReadonlyInt32BufferView
            || KnownType((SyntaxNode)initializer.Children[0]) != PrimitiveType.OwnedInt32Buffer)
        {
            Report("TRM3023", "A native buffer range can only initialize a readonly lexical view local.", span);
            var invalid = AddVariable(name.Name, viewType, isMutable, name.Span);
            return new BoundViewScope(invalid, ErrorExpression(span), ErrorExpression(span), ErrorExpression(span),
                new BoundBlock([], span), span);
        }

        var range = (SyntaxNode)initializer.Children[2];
        var lower = (SyntaxNode)range.Children[0];
        var connector = ((SyntaxTokenElement)range.Children[1]).Token;
        var upper = (SyntaxNode)range.Children[2];
        var fromEndLower = lower.Kind == SyntaxKind.EndRelativeIndexBound;
        var owner = BindReceiverLoan((SyntaxNode)initializer.Children[0], PrimitiveType.OwnedInt32Buffer);
        var startNode = fromEndLower ? (SyntaxNode)lower.Children[1] : lower;
        var dynamicStart = lower.Kind == SyntaxKind.OmittedIndexBound
            ? new BoundConstant(0, PrimitiveType.Int32, lower.Span)
            : BindExpression(startNode, PrimitiveType.Int32);
        var fromEndUpper = upper.Kind == SyntaxKind.EndRelativeIndexBound
            || connector.Kind == TokenKind.DotDotCaret;
        var endNode = upper.Kind == SyntaxKind.EndRelativeIndexBound
            ? (SyntaxNode)upper.Children[1] : upper;
        var rangeEnd = upper.Kind == SyntaxKind.OmittedIndexBound
            ? BindBufferCount(owner, upper.Span)
            : BindExpression(endNode, PrimitiveType.Int32);
        var variable = AddVariable(name.Name, viewType, false, name.Span);
        return new BoundViewScope(variable, owner, dynamicStart,
            new BoundConstant(0, PrimitiveType.Int32, upper.Span), new BoundBlock([], span), span) {
            RangeEnd = rangeEnd,
            IsStartEndRelative = fromEndLower,
            IsRangeEndRelative = fromEndUpper,
        };
    }

    private bool TryGetConstInt32Value(SyntaxNode node, out int value)
    {
        while (node.Kind == SyntaxKind.ParenthesizedExpression)
        {
            node = (SyntaxNode)node.Children[1];
        }

        if (node.Kind == SyntaxKind.NameExpression
            && TryFindVariable(((SyntaxTokenElement)node.Children[0]).Token, out var slot)
            && _constScalarValues.TryGetValue(slot, out var constant)
            && constant.Type == PrimitiveType.Int32
            && constant.Value is >= int.MinValue and <= int.MaxValue)
        {
            value = (int)constant.Value;
            return true;
        }

        value = 0;
        return false;
    }

    private BoundExpression FoldSwitchConstantLocals(BoundExpression expression)
    {
        var rewritten = expression.RewriteNestedExpressions(FoldSwitchConstantLocals);
        if (rewritten is BoundRead read
            && _constScalarValues.TryGetValue(read.Variable, out var constant))
        {
            return constant with { Span = read.Span };
        }
        return FoldScalarConstant(rewritten);
    }

    private BoundExpression BindBufferCount(BoundExpression owner, TextSpan span)
    {
        if (!TryFindFunction(NativeBufferIntrinsics.TypeName + ".Count", out var count))
        {
            Report("TRM3011", "The native buffer count intrinsic is unavailable.", span);
            return ErrorExpression(span);
        }

        return new BoundCall(count, [owner], PrimitiveType.Int32, span);
    }

    private BoundExpression BindBufferRemaining(BoundExpression owner, int consumed, TextSpan span)
    {
        var count = BindBufferCount(owner, span);
        return consumed == 0 ? count
            : new BoundBinary(TokenKind.Minus, count, new BoundConstant(consumed, PrimitiveType.Int32, span),
                PrimitiveType.Int32, span);
    }

    private BoundExpression BindBufferIndex(SyntaxNode index, int buffer, out bool isEndRelative)
    {
        isEndRelative = false;
        if (index.Kind != SyntaxKind.EndRelativeIndexBound)
        {
            return BindExpression(index, PrimitiveType.Int32);
        }

        var offsetNode = (SyntaxNode)index.Children[1];
        if (!TryGetConstInt32Value(offsetNode, out var offsetValue))
        {
            var offset = BindExpression(offsetNode, PrimitiveType.Int32);
            if (offset is not BoundConstant { Type: PrimitiveType.Int32 } constant
                || constant.Value is < int.MinValue or > int.MaxValue)
            {
                isEndRelative = true;
                return offset;
            }
            offsetValue = (int)constant.Value;
        }

        if (_buffers[buffer].IsInvocationScoped)
        {
            isEndRelative = true;
            return new BoundConstant(offsetValue, PrimitiveType.Int32, index.Span);
        }

        var indexValue = offsetValue < 0
            ? -1
            : (long)_buffers[buffer].Values.Length - offsetValue;
        return new BoundConstant(indexValue, PrimitiveType.Int32, index.Span);
    }

    private int BindViewVariable(SyntaxNode receiver)
    {
        while (receiver.Kind == SyntaxKind.ParenthesizedExpression)
        {
            receiver = (SyntaxNode)receiver.Children[1];
        }
        if (receiver.Kind == SyntaxKind.NameExpression
            && TryFindVariable(((SyntaxTokenElement)receiver.Children[0]).Token, out var slot)
            && _variables[slot].Type is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView)
        {
            return slot;
        }
        Report("TRM3023", "A readonly native buffer view requires a named lexical local.", receiver.Span);
        return -1;
    }

    private bool TryResolveStruct(string name, out PrimitiveType type) =>
        ResolveTypeName(name, out type) == DeclaredTypeResolution.Unique && IsStruct(type);

    private BoundBlock BindCallableBody(SyntaxNode body)
    {
        if (body.Kind == SyntaxKind.Block)
        {
            return BindBlock(body);
        }

        var block = new SyntaxNode(SyntaxKind.Block, [body], body.Span.Start);
        return BindBlock(block);
    }

    private BoundBlock BindBlock(SyntaxNode block)
    {
        _scopes.Add(new Dictionary<string, int>(StringComparer.Ordinal));
        var statements = ImmutableArray.CreateBuilder<BoundStatement>();
        var outputOwners = new List<(int Statement, int Variable)>();

        foreach (var child in block.Children)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (child is SyntaxNode statement)
            {
                var firstVariable = _variables.Count;
                var boundStatement = BindStatement(statement);
                for (var slot = firstVariable; slot < _variables.Count; slot++)
                {
                    if (IsUnique(_variables[slot].Type)
                        && _scopes[^1].TryGetValue(_variables[slot].Name, out var scoped)
                        && scoped == slot && !(boundStatement is BoundLocal local && local.Variable == slot)
                        && !(boundStatement is BoundVacantLocal vacant && vacant.Variable == slot))
                    {
                        outputOwners.Add((statements.Count, slot));
                    }
                }
                statements.Add(boundStatement);
            }
        }

        return CompleteBlockScope(statements, outputOwners, block.Span);
    }

    private BoundBlock CompleteBlockScope(ImmutableArray<BoundStatement>.Builder statements,
        List<(int Statement, int Variable)> outputOwners, TextSpan blockSpan)
    {
        foreach (var slot in _scopes[^1].Values)
        {
            if (_unresolvedVacantLocals.Remove(slot))
            {
                Report("TRM3016", $"Cannot infer the type of vacant local '{_variables[slot].Name}' from a checked initialization.",
                    _variables[slot].Span);
            }
        }
        var managedLocals = _scopes[^1].Values
            .Where(slot => ManagedClassTypes.IsManaged(_variables[slot].Type))
            .Order().ToImmutableArray();
        _scopes.RemoveAt(_scopes.Count - 1);
        var bound = statements.ToImmutable();
        for (var index = bound.Length - 1; index >= 0; index--)
        {
            if (bound[index] is BoundViewScope view)
            {
                bound = bound[..index].Add(view with { Body = new BoundBlock(bound[(index + 1)..], blockSpan) });
            }
            if (bound[index] is BoundLocal local && IsUnique(local.Initializer.Type))
            {
                var method = CleanupMethod(local.Initializer.Type);
                var cleanupFunction = method is not null && _functions.TryGetValue(method, out var found) ? found : -1;
                bound = bound[..(index + 1)].Add(new BoundTryFinally(
                    new BoundBlock(bound[(index + 1)..], blockSpan),
                    new BoundBlock([new BoundDrop(local.Variable, cleanupFunction, blockSpan)], blockSpan),
                    blockSpan));
            }
            if (bound[index] is BoundLocal tokenSpan && tokenSpan.Initializer.Type == PrimitiveType.TokenSpan)
            {
                bound = bound[..(index + 1)].Add(new BoundTryFinally(
                    new BoundBlock(bound[(index + 1)..], blockSpan),
                    new BoundBlock([new BoundDropSpan(tokenSpan.Variable, blockSpan)], blockSpan), blockSpan));
            }
            if (bound[index] is BoundVacantLocal vacant && IsUnique(_variables[vacant.Variable].Type))
            {
                var method = CleanupMethod(_variables[vacant.Variable].Type);
                var cleanupFunction = method is not null && _functions.TryGetValue(method, out var found) ? found : -1;
                bound = bound[..(index + 1)].Add(new BoundTryFinally(
                    new BoundBlock(bound[(index + 1)..], blockSpan),
                    new BoundBlock([new BoundDrop(vacant.Variable, cleanupFunction, blockSpan)], blockSpan),
                    blockSpan));
            }
            for (var output = outputOwners.Count - 1; output >= 0; output--)
            {
                if (outputOwners[output].Statement != index)
                {
                    continue;
                }
                var slot = outputOwners[output].Variable;
                var method = CleanupMethod(_variables[slot].Type);
                var cleanupFunction = method is not null && _functions.TryGetValue(method, out var found) ? found : -1;
                bound = bound[..index]
                    .Add(new BoundOwnerDeclaration(slot, blockSpan))
                    .Add(new BoundTryFinally(new BoundBlock(bound[index..], blockSpan),
                        new BoundBlock([new BoundDrop(slot, cleanupFunction, blockSpan)], blockSpan),
                        blockSpan));
            }
        }
        return new BoundBlock(bound, blockSpan) {
            IsManagedLexicalScope = true,
            RetiredManagedLocals = managedLocals,
        };
    }

    private BoundViewSet? BindViewSetStatement(SyntaxNode expressionNode, TextSpan span)
    {
        if (expressionNode.Kind != SyntaxKind.InvocationExpression
            || expressionNode.Children[0] is not SyntaxNode { Kind: SyntaxKind.MemberAccessExpression } member
            || ((SyntaxTokenElement)member.Children[2]).Token.Text != "Set")
        {
            return null;
        }

        var receiver = (SyntaxNode)member.Children[0];
        var variableReceiver = receiver;
        while (variableReceiver.Kind == SyntaxKind.ParenthesizedExpression)
        {
            variableReceiver = (SyntaxNode)variableReceiver.Children[1];
        }
        var viewSlot = variableReceiver.Kind == SyntaxKind.NameExpression
            && TryFindVariable(((SyntaxTokenElement)variableReceiver.Children[0]).Token, out var slot)
            && !_unresolvedVacantLocals.Contains(slot)
            && _variables[slot].Type == PrimitiveType.MutableInt32BufferView
                ? slot : -1;
        if (viewSlot < 0 && KnownType(receiver) != PrimitiveType.MutableInt32BufferView)
        {
            return null;
        }

        if (TryGetTwoPositionalArguments((SyntaxNode)expressionNode.Children[1]) is { } arguments)
        {
            return new BoundViewSet(viewSlot >= 0 ? viewSlot : BindViewVariable(receiver),
                BindExpression(arguments.First, PrimitiveType.Int32),
                BindExpression(arguments.Second, PrimitiveType.Int32), span);
        }
        Report("TRM3011", "A mutable native buffer view supports Set(index, value) with two positional int32 arguments.",
            expressionNode.Span);
        return null;
    }

    private BoundBlock BindSwitchStatement(SyntaxNode node)
    {
        var outerScopeCount = _scopes.Count;
        _scopes.Add(new Dictionary<string, int>(StringComparer.Ordinal));
        var statements = ImmutableArray.CreateBuilder<BoundStatement>();
        var outputOwners = new List<(int Statement, int Variable)>();
        try
        {
            var selectorNode = (SyntaxNode)node.Children[2];
            var firstSelectorVariable = _variables.Count;
            var selector = BindExpression(selectorNode, null);
            for (var slot = firstSelectorVariable; slot < _variables.Count; slot++)
            {
                if (IsUnique(_variables[slot].Type)
                    && _scopes[^1].TryGetValue(_variables[slot].Name, out var scoped)
                    && scoped == slot)
                {
                    outputOwners.Add((0, slot));
                }
            }
            if (selector.Type is not (PrimitiveType.Bool or PrimitiveType.Char16
                or PrimitiveType.Int32 or PrimitiveType.UInt32
                or PrimitiveType.Int64 or PrimitiveType.UInt64))
            {
                Report("TRM3007", "Switch statements require a supported scalar selector.", selectorNode.Span);
                return CompleteBlockScope(statements, outputOwners, node.Span);
            }

            var selectorVariable = _variables.Count;
            _variables.Add(new BoundVariable($"<switch-selector-{selectorVariable}>",
                selector.Type, false, selectorNode.Span));
            statements.Add(new BoundLocal(selectorVariable, selector, node.Span));
            var selectorRead = new BoundRead(selectorVariable, selector.Type, selectorNode.Span);
            var armNodes = node.Children.OfType<SyntaxNode>()
                .Where(static child => child.Kind == SyntaxKind.SwitchArm).ToArray();
            var arms = new List<SwitchArmBinding>(armNodes.Length);
            var targets = ImmutableArray.CreateBuilder<SwitchTarget>();
            var priorUnguardedLabels = new List<BoundConstant>();
            var nextOrdinal = 0;
            var defaultArm = -1;
            var sawFallback = false;
            var hasIrrefutablePattern = false;

            for (var armIndex = 0; armIndex < armNodes.Length; armIndex++)
            {
                var armNode = armNodes[armIndex];
                var pattern = armNode.Children.OfType<SyntaxNode>().FirstOrDefault(static child =>
                    child.Kind is SyntaxKind.SwitchConstantPattern or SyntaxKind.SwitchWildcardPattern
                        or SyntaxKind.SwitchInferredBindingPattern
                        or SyntaxKind.SwitchParenthesizedPattern);
                while (pattern?.Kind == SyntaxKind.SwitchParenthesizedPattern
                    && pattern.Children.OfType<SyntaxNode>().FirstOrDefault() is SyntaxNode nestedPattern)
                {
                    pattern = nestedPattern;
                }
                var body = armNode.Children.OfType<SyntaxNode>()
                    .LastOrDefault(static child => child.Kind == SyntaxKind.Block);
                if (pattern is null || body is null)
                {
                    Report("TRM3001", "A switch arm requires a supported pattern and braced body.", armNode.Span);
                    continue;
                }
                var isWildcard = pattern.Kind == SyntaxKind.SwitchWildcardPattern;
                if (armNode.Children.OfType<SyntaxNode>()
                    .Any(static child => child.Kind == SyntaxKind.Attribute))
                {
                    Report("TRM3001", "Switch arm attributes are not supported by this compiler path.", armNode.Span);
                }
                var guardClause = armNode.Children.OfType<SyntaxNode>()
                    .FirstOrDefault(static child => child.Kind == SyntaxKind.SwitchWhenClause);
                BoundExpression? guard = null;
                BoundExpression? patternTest = null;
                var patternVariable = -1;
                string? patternName = null;
                var isIrrefutable = isWildcard;
                if (pattern.Kind == SyntaxKind.SwitchInferredBindingPattern)
                {
                    var patternTokens = pattern.Children.OfType<SyntaxTokenElement>().ToArray();
                    var nameToken = patternTokens[^1].Token;
                    isIrrefutable = true;
                    if (nameToken.Kind == TokenKind.Underscore)
                    {
                        patternTest = new BoundConstant(1, PrimitiveType.Bool, pattern.Span);
                    }
                    else
                    {
                        var isMutable = patternTokens.Any(static token =>
                            token.Token.Kind == TokenKind.MutKeyword);
                        var includeNull = patternTokens.Any(static token =>
                            token.Token.Kind == TokenKind.Question);
                        _scopes.Add(new Dictionary<string, int>(StringComparer.Ordinal));
                        try
                        {
                            patternVariable = AddVariable(nameToken.Name, selector.Type,
                                isMutable, nameToken.Span);
                        }
                        finally
                        {
                            _scopes.RemoveAt(_scopes.Count - 1);
                        }
                        patternName = nameToken.Name;
                        patternTest = new BoundIsVariablePattern(selectorRead, patternVariable,
                            includeNull, pattern.Span);
                    }
                }
                if (guardClause is not null)
                {
                    var guardExpression = guardClause.Children.OfType<SyntaxNode>().FirstOrDefault();
                    if (guardExpression is null)
                    {
                        Report("TRM3001", "A switch guard requires a Boolean expression.", guardClause.Span);
                    }
                    else
                    {
                        if (patternVariable >= 0)
                        {
                            _scopes.Add(new Dictionary<string, int>(StringComparer.Ordinal) {
                                [patternName!] = patternVariable,
                            });
                        }
                        var firstGuardVariable = _variables.Count;
                        try
                        {
                            guard = BindExpression(guardExpression, PrimitiveType.Bool);
                            if (_variables.Count != firstGuardVariable)
                            {
                                Report("TRM3007",
                                    "Switch guards cannot introduce output bindings in this compiler path.",
                                    guardExpression.Span);
                            }
                        }
                        finally
                        {
                            if (patternVariable >= 0)
                            {
                                _scopes.RemoveAt(_scopes.Count - 1);
                            }
                        }
                    }
                }

                BoundConstant? value = null;
                if (!isWildcard && pattern.Kind != SyntaxKind.SwitchInferredBindingPattern)
                {
                    var patternExpression = pattern.Children.OfType<SyntaxNode>().FirstOrDefault();
                    if (patternExpression is null)
                    {
                        Report("TRM3001", "A switch constant pattern requires an expression.", pattern.Span);
                    }
                    else
                    {
                        var expression = BindExpression(patternExpression, selector.Type);
                        if (FoldSwitchConstantLocals(expression) is BoundConstant constant
                            && constant.Type == selector.Type)
                        {
                            value = constant;
                        }
                        else
                        {
                            Report("TRM3007",
                                "A switch constant pattern must be a compile-time scalar of the selector type.",
                                pattern.Span);
                        }
                    }
                }

                var isFallback = isWildcard && guard is null;
                if (sawFallback)
                {
                    Report("TRM3007", "An unguarded wildcard switch arm must be the final arm.", armNode.Span);
                }
                var ordinal = nextOrdinal;
                if (isFallback)
                {
                    if (defaultArm >= 0)
                    {
                        Report("TRM3007", "A switch statement cannot contain multiple unguarded wildcard arms.",
                            armNode.Span);
                    }
                    defaultArm = ordinal;
                    sawFallback = true;
                    if (armIndex != armNodes.Length - 1)
                    {
                        Report("TRM3007", "An unguarded wildcard switch arm must be the final arm.", armNode.Span);
                    }
                }
                else
                {
                    nextOrdinal++;
                    if (value is BoundConstant label && guard is null)
                    {
                        var duplicates = priorUnguardedLabels.Any(prior =>
                            SwitchConstantsEqual(prior, label, armNode.Span));
                        if (duplicates)
                        {
                            Report("TRM3007", "An unguarded switch constant cannot be repeated.", armNode.Span);
                        }
                        else
                        {
                            priorUnguardedLabels.Add(label);
                            targets.Add(new SwitchTarget(label, ordinal));
                        }
                    }
                    else if (value is BoundConstant guardedLabel
                        && priorUnguardedLabels.Any(prior =>
                            SwitchConstantsEqual(prior, guardedLabel, armNode.Span)))
                    {
                        Report("TRM3007", "A guarded switch arm follows an unguarded matching arm.", armNode.Span);
                    }
                }
                hasIrrefutablePattern |= isIrrefutable && guard is null;
                arms.Add(new SwitchArmBinding(armNode, body, value, isWildcard, guard,
                    ordinal, isFallback, patternTest, patternVariable, patternName));
            }

            var boolTrue = arms.Any(static arm => !arm.IsWildcard && arm.Guard is null
                && arm.Value is { Type: PrimitiveType.Bool, Value: 1 });
            var boolFalse = arms.Any(static arm => !arm.IsWildcard && arm.Guard is null
                && arm.Value is { Type: PrimitiveType.Bool, Value: 0 });
            if (defaultArm < 0 && !hasIrrefutablePattern
                && !(selector.Type == PrimitiveType.Bool && boolTrue && boolFalse))
            {
                Report("TRM3007", "A switch statement requires complete scalar coverage or an unguarded wildcard.",
                    node.Span);
            }

            var needsDispatcher = arms.Any(static arm => ContainsSwitchTransfer(arm.BodySyntax));
            var fallbackOrdinal = defaultArm >= 0 ? defaultArm : nextOrdinal;
            BoundExpression initialArm = new BoundConstant(fallbackOrdinal, PrimitiveType.Int32, node.Span);
            for (var armIndex = arms.Count - 1; armIndex >= 0; armIndex--)
            {
                var arm = arms[armIndex];
                if (arm.IsFallback)
                {
                    continue;
                }
                initialArm = new BoundConditional(
                    SwitchArmCondition(selectorRead, arm),
                    new BoundConstant(arm.Ordinal, PrimitiveType.Int32, arm.Syntax.Span),
                    initialArm, PrimitiveType.Int32, arm.Syntax.Span);
            }

            BoundBlock[] boundBodies;
            var stateVariable = -1;
            if (needsDispatcher)
            {
                stateVariable = _variables.Count;
                _variables.Add(new BoundVariable("<switch-arm>", PrimitiveType.Int32,
                    true, node.Span));
                var dispatchLoopIndex = _loops.Count;
                var binding = new SwitchBinding(stateVariable, dispatchLoopIndex, selector.Type,
                    targets.ToImmutable(), defaultArm);
                _loops.Add(new LoopBinding { IsSwitchDispatcher = true });
                _switches.Add(binding);
                try
                {
                    boundBodies = [.. arms.Select(BindSwitchArmBody)];
                }
                finally
                {
                    _switches.RemoveAt(_switches.Count - 1);
                    _loops.RemoveAt(_loops.Count - 1);
                }

                var fallbackIndex = arms.FindIndex(static arm => arm.IsFallback);
                BoundStatement alternate = fallbackIndex >= 0
                    ? WithSwitchExit(boundBodies[fallbackIndex], arms[fallbackIndex].Syntax.Span)
                    : new BoundBlock([new BoundBreak(node.Span)], node.Span);
                for (var armIndex = arms.Count - 1; armIndex >= 0; armIndex--)
                {
                    var arm = arms[armIndex];
                    if (arm.IsFallback)
                    {
                        continue;
                    }
                    var stateMatch = new BoundBinary(TokenKind.EqualsEquals,
                        new BoundRead(stateVariable, PrimitiveType.Int32, arm.Syntax.Span),
                        new BoundConstant(arm.Ordinal, PrimitiveType.Int32, arm.Syntax.Span),
                        PrimitiveType.Bool, arm.Syntax.Span);
                    alternate = new BoundIf(stateMatch,
                        WithSwitchExit(boundBodies[armIndex], arm.Syntax.Span),
                        alternate, arm.Syntax.Span);
                }
                statements.Add(new BoundLocal(stateVariable, initialArm, node.Span));
                statements.Add(new BoundWhile(new BoundConstant(1, PrimitiveType.Bool, node.Span),
                    alternate, node.Span) { SwitchArmVariable = stateVariable });
            }
            else
            {
                boundBodies = [.. arms.Select(BindSwitchArmBody)];
                var fallbackIndex = arms.FindIndex(static arm => arm.IsFallback);
                BoundStatement alternate = fallbackIndex >= 0
                    ? boundBodies[fallbackIndex]
                    : new BoundBlock([], node.Span);
                for (var armIndex = arms.Count - 1; armIndex >= 0; armIndex--)
                {
                    var arm = arms[armIndex];
                    if (arm.IsFallback)
                    {
                        continue;
                    }
                    alternate = new BoundIf(SwitchArmCondition(selectorRead, arm),
                        boundBodies[armIndex], alternate, arm.Syntax.Span);
                }
                statements.Add(alternate);
            }
            return CompleteBlockScope(statements, outputOwners, node.Span);
        }
        finally
        {
            if (_scopes.Count > outerScopeCount)
            {
                _scopes.RemoveAt(_scopes.Count - 1);
            }
        }
    }

    private BoundBlock BindSwitchArmBody(SwitchArmBinding arm)
    {
        if (arm.PatternVariable < 0)
        {
            return BindBlock(arm.BodySyntax);
        }
        if (arm.PatternName is not string name)
        {
            throw new InvalidOperationException("A switch pattern variable has no source name.");
        }

        _scopes.Add(new Dictionary<string, int>(StringComparer.Ordinal) {
            [name] = arm.PatternVariable,
        });
        try
        {
            return BindBlock(arm.BodySyntax);
        }
        finally
        {
            _scopes.RemoveAt(_scopes.Count - 1);
        }
    }

    private BoundBlock BindSwitchTransfer(SyntaxNode node)
    {
        if (_switches.Count == 0)
        {
            Report("TRM3008", "A switch transfer requires an enclosing statement switch.", node.Span);
            return new BoundBlock([], node.Span);
        }
        var binding = _switches[^1];
        var targetNode = node.Children[1];
        var targetArm = -1;
        if (targetNode is SyntaxTokenElement { Token.Kind: TokenKind.Underscore })
        {
            targetArm = binding.DefaultArm;
        }
        else if (targetNode is SyntaxNode targetExpression)
        {
            var firstVariable = _variables.Count;
            var value = FoldSwitchConstantLocals(
                BindExpression(targetExpression, binding.SelectorType)) is BoundConstant constant
                    ? constant : null;
            if (_variables.Count != firstVariable)
            {
                Report("TRM3007", "A switch transfer target cannot introduce output bindings.", targetExpression.Span);
            }
            if (value is null || value.Type != binding.SelectorType)
            {
                Report("TRM3007", "A switch transfer target must be a constant of the selector type.",
                    targetExpression.Span);
            }
            else
            {
                foreach (var target in binding.Targets)
                {
                    if (SwitchConstantsEqual(target.Value, value, targetExpression.Span))
                    {
                        targetArm = target.ArmOrdinal;
                        break;
                    }
                }
            }
        }
        if (targetArm < 0)
        {
            Report("TRM3008",
                "A switch transfer must target an existing unguarded constant arm or wildcard arm.", node.Span);
            return new BoundBlock([], node.Span);
        }

        var depth = _loops.Count - binding.DispatchLoopIndex;
        if (depth <= 0)
        {
            Report("TRM3008", "A switch transfer has no active dispatcher.", node.Span);
            return new BoundBlock([], node.Span);
        }
        return new BoundBlock([
            new BoundAssignment(binding.StateVariable,
                new BoundConstant(targetArm, PrimitiveType.Int32, node.Span), node.Span),
            new BoundContinue(node.Span) { TargetDepth = depth },
        ], node.Span);
    }

    private static bool ContainsSwitchTransfer(SyntaxNode statement)
    {
        var pending = new Stack<SyntaxNode>();
        pending.Push(statement);
        while (pending.TryPop(out var current))
        {
            if (current.Kind == SyntaxKind.SwitchTransferStatement)
            {
                return true;
            }
            if (current.Kind == SyntaxKind.SwitchStatement)
            {
                continue;
            }
            foreach (var child in current.Children.OfType<SyntaxNode>())
            {
                pending.Push(child);
            }
        }
        return false;
    }

    private static BoundExpression SwitchArmCondition(BoundRead selector, SwitchArmBinding arm)
    {
        var match = arm.PatternTest ?? (arm.IsWildcard
            ? new BoundConstant(1, PrimitiveType.Bool, arm.Syntax.Span)
            : new BoundBinary(TokenKind.EqualsEquals, selector, arm.Value
                ?? new BoundConstant(0, selector.Type, arm.Syntax.Span),
                PrimitiveType.Bool, arm.Syntax.Span));
        return arm.Guard is null ? match
            : new BoundBinary(TokenKind.AmpersandAmpersand, match, arm.Guard,
                PrimitiveType.Bool, arm.Syntax.Span);
    }

    private static BoundBlock WithSwitchExit(BoundBlock body, TextSpan span)
        => body with { Statements = body.Statements.Add(new BoundBreak(span)) };

    private static bool SwitchConstantsEqual(BoundConstant left, BoundConstant right, TextSpan span)
        => FoldScalarConstant(new BoundBinary(TokenKind.EqualsEquals, left, right,
            PrimitiveType.Bool, span)) is BoundConstant { Type: PrimitiveType.Bool, Value: 1 };

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Unsupported syntax kinds are rejected by the default branch.")]
    private BoundStatement BindStatement(SyntaxNode node, string? loopLabel = null)
    {
        _cancellationToken.ThrowIfCancellationRequested();

        switch (node.Kind)
        {
            case SyntaxKind.ExpressionBody:
            {
                var statement = (SyntaxNode)node.Children[1];
                if (!_constructor && _returnType != PrimitiveType.Void)
                {
                    if (statement.Kind != SyntaxKind.ExpressionStatement)
                    {
                        Report("TRM3010", "A value-producing expression body requires one expression.", statement.Span);
                        return new BoundBlock([], node.Span);
                    }

                    var expression = (SyntaxNode)statement.Children[0];
                    return new BoundReturn(BindExpression(expression, _returnType), node.Span);
                }

                return BindStatement(statement);
            }
            case SyntaxKind.Block:
                return BindBlock(node);

            case SyntaxKind.TryFinallyStatement:
            {
                var body = BindBlock((SyntaxNode)node.Children[1]);
                var previousFloor = _finallyLoopDepth;
                _finallyLoopDepth = Math.Max(previousFloor, _loops.Count);
                var cleanup = BindBlock((SyntaxNode)node.Children[3]);
                _finallyLoopDepth = previousFloor;
                return new BoundTryFinally(body, cleanup, node.Span);
            }

            case SyntaxKind.UnsafeStatement:
            {
                _unsafeDepth++;
                var body = BindBlock((SyntaxNode)node.Children[1]);
                _unsafeDepth--;
                return new BoundUnsafe(body, node.Span);
            }

            case SyntaxKind.TryCatchStatement:
            {
                if (_unsafeDepth == 0)
                {
                    Report("TRM3021", "Checked failure recovery requires an unsafe block.", node.Span);
                }
                var body = BindBlock((SyntaxNode)node.Children[1]);
                var handler = BindBlock((SyntaxNode)node.Children[3]);
                BoundBlock? cleanup = null;
                if (node.Children.Length > 4)
                {
                    var previousFloor = _finallyLoopDepth;
                    _finallyLoopDepth = Math.Max(previousFloor, _loops.Count);
                    cleanup = BindBlock((SyntaxNode)node.Children[5]);
                    _finallyLoopDepth = previousFloor;
                }
                return new BoundTryCatch(body, handler, cleanup, node.Span);
            }

            case SyntaxKind.VariableDeclaration:
            {
                var isMutable = node.Children[0] is SyntaxTokenElement first && first.Token.Kind == TokenKind.MutKeyword;
                var offset = isMutable ? 1 : 0;
                var typeElement = node.Children[offset];
                var name = ((SyntaxTokenElement)node.Children[offset + 1]).Token;
                var initializerNode = (SyntaxNode)node.Children[offset + 3];
                if (typeElement is SyntaxNode { Kind: SyntaxKind.InlineArrayTypeName } fixedArraySyntax)
                {
                    if (!TryBindNativeFixedArrayType(fixedArraySyntax, out var fixedArrayType,
                        out var fixedElementType, out var fixedElementCount))
                    {
                        return new BoundExpressionStatement(BindExpression(initializerNode, null), node.Span);
                    }
                    var fixedArrayInitializer = BindNativeFixedArrayInitializer(initializerNode,
                        fixedArrayType, fixedElementType, fixedElementCount);
                    if (fixedArrayInitializer is BoundNativeFixedArrayInitialization fixedArray)
                    {
                        if (isMutable && !NativeFixedArrayContract.AllowsMutableLocal(fixedArray.Layout))
                        {
                            Report("TRM3030", "The selected fixed-array layout does not permit mutable locals.",
                                node.Span);
                        }
                        var fixedArrayVariable = AddVariable(name.Name, fixedArrayType, isMutable, name.Span);
                        _variables[fixedArrayVariable] = _variables[fixedArrayVariable] with {
                            NativeFixedArrayLayout = fixedArray.Layout,
                        };
                        return new BoundLocal(fixedArrayVariable, fixedArrayInitializer, node.Span);
                    }
                    return new BoundExpressionStatement(fixedArrayInitializer, node.Span);
                }
                var declaredType = typeElement is SyntaxNode typeNode ? BindType(typeNode, allowVoid: false) : (PrimitiveType?)null;
                if (declaredType is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView)
                {
                    return BindViewScope(name, isMutable, initializerNode, node.Span, declaredType.Value);
                }
                if (declaredType is null
                    && KnownType(initializerNode) == PrimitiveType.ReadonlyInt32BufferView)
                {
                    return BindViewScope(name, isMutable, initializerNode, node.Span,
                        PrimitiveType.ReadonlyInt32BufferView);
                }
                var initializer = BindExpression(initializerNode, declaredType);

                if (declaredType is null && initializer.Type == PrimitiveType.Void)
                {
                    Report("TRM3005", "Cannot infer a variable's type from a void expression.", initializerNode.Span);
                }

                // Locals enter scope only after their initializer, including in nested blocks.
                var slot = AddVariable(name.Name, declaredType ?? initializer.Type, isMutable, name.Span);
                return new BoundLocal(slot, initializer, node.Span);
            }

            case SyntaxKind.VacantLocalDeclaration:
            {
                var isMutable = node.Children[1] is SyntaxTokenElement modifier
                    && modifier.Token.Kind == TokenKind.MutKeyword;
                var typeIndex = isMutable ? 2 : 1;
                var typeElement = node.Children[typeIndex];
                var name = ((SyntaxTokenElement)node.Children[typeIndex + 1]).Token;
                var inferred = typeElement is SyntaxTokenElement;
                var type = inferred ? PrimitiveType.Void : BindType((SyntaxNode)typeElement, allowVoid: false);
                if (type is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView)
                {
                    Report("TRM3023", "Native buffer views require an initialized lexical owner loan.", node.Span);
                }
                if (TokenSpanContract.IsDependent(type, _structs))
                {
                    Report("TRM3023", "Span and cursor bindings require a complete initializer in this provisional profile.", node.Span);
                }
                var slot = AddVariable(name.Name, type, isMutable, name.Span);
                _variables[slot] = _variables[slot] with { IsInitializationLocal = true };
                if (inferred)
                {
                    _ = _unresolvedVacantLocals.Add(slot);
                }
                return new BoundVacantLocal(slot, node.Span);
            }

            case SyntaxKind.ConstDeclaration:
            {
                var declaration = (SyntaxNode)node.Children[1];
                if (declaration.Children[0] is not SyntaxNode type
                    || !ConstEvalScalar(BindType(type, allowVoid: false)))
                {
                    Report("TRM3024", "A const local requires an explicit scalar type.", node.Span);
                }
                var local = BindStatement(declaration);
                if (local is BoundLocal boundLocal)
                {
                    _ = _constRequired.Add(boundLocal);
                    _ = _constLocals.Add(boundLocal.Variable);
                    if (FoldSwitchConstantLocals(boundLocal.Initializer) is BoundConstant constant)
                    {
                        _constScalarValues[boundLocal.Variable] = constant;
                    }
                }
                return local;
            }

            case SyntaxKind.LifetimeEndStatement:
            {
                var target = (SyntaxNode)node.Children[0];
                if (target.Kind != SyntaxKind.NameExpression)
                {
                    Report("TRM3008", "Lifetime end requires a named local binding.", target.Span);
                    return new BoundBlock([], node.Span);
                }
                var token = ((SyntaxTokenElement)target.Children[0]).Token;
                if (!TryFindVariable(token.Name, out var slot))
                {
                    Report("TRM3006", $"Unknown or not-yet-declared local '{token.Text}'.", token.Span);
                    return new BoundBlock([], node.Span);
                }
                if (slot < _localStart
                    || _constLocals.Contains(slot)
                    || _unresolvedVacantLocals.Contains(slot))
                {
                    Report("TRM3008", "Lifetime end requires a resolved non-const local, not a parameter or receiver.", token.Span);
                    return new BoundBlock([], node.Span);
                }
                var method = IsUnique(_variables[slot].Type) ? CleanupMethod(_variables[slot].Type) : null;
                var cleanupFunction = method is not null && _functions.TryGetValue(method, out var found) ? found : -1;
                return new BoundLifetimeEnd(slot, cleanupFunction, node.Span);
            }

            case SyntaxKind.AssignmentStatement:
            {
                var target = (SyntaxNode)node.Children[0];
                var valueNode = (SyntaxNode)node.Children[2];
                if (((SyntaxTokenElement)node.Children[1]).Token.Kind == TokenKind.QuestionQuestionEquals)
                {
                    return BindOptionalAssignment(target, valueNode, node.Span);
                }
                while (target.Kind == SyntaxKind.ParenthesizedExpression)
                {
                    target = (SyntaxNode)target.Children[1];
                }

                if (TryFindInitReferenceProjection(target, out var destination))
                {
                    return new BoundAssignment(destination,
                        BindExpression(valueNode, _variables[destination].Type), node.Span);
                }

                if (target.Kind == SyntaxKind.UnaryExpression
                    && ((SyntaxTokenElement)target.Children[0]).Token.Kind == TokenKind.Star)
                {
                    var pointer = BindExpression((SyntaxNode)target.Children[1], null);
                    var pointerType = pointer.Type;
                    if (!NativePointerTypeContract.IsValidType(pointerType, [.. _nativePointerTypes]))
                    {
                        Report("TRM3030", "A pointer assignment requires a typed native pointer.", target.Span);
                        _ = BindExpression(valueNode, null);
                        return new BoundBlock([], node.Span);
                    }

                    var pointerDefinition = _nativePointerTypes[NativePointerTypes.Index(pointerType)];
                    if (!pointerDefinition.IsWritable || pointerDefinition.PointeeType == PrimitiveType.Void)
                    {
                        Report("TRM3030", "A pointer assignment requires a writable pointer to a value type.",
                            target.Span);
                    }
                    if (!DirectNativeImportContract.IsCopyablePointee(pointerDefinition.PointeeType, _structs))
                    {
                        Report("TRM3023",
                            "A raw pointer cannot load or store an owning value without preserving its ownership and cleanup.",
                            target.Span);
                    }
                    if (_unsafeDepth == 0)
                    {
                        Report("TRM3028", "A pointer write requires an explicit unsafe context.", target.Span);
                    }
                    var pointerValue = BindExpression(valueNode, pointerDefinition.PointeeType);
                    return new BoundExpressionStatement(
                        new BoundNativePointerWrite(pointer, pointerValue, node.Span) {
                            IsUnsafeAuthorized = _unsafeDepth != 0,
                        },
                        node.Span);
                }

                if (target.Kind == SyntaxKind.IndexExpression
                    && TryFindNativeFixedArrayLocal((SyntaxNode)target.Children[0], out var fixedArray)
                    && _variables[fixedArray].NativeFixedArrayLayout is BoundNativeFixedArrayLayout layout)
                {
                    if (!_variables[fixedArray].IsMutable
                        || !NativeFixedArrayContract.AllowsElementWrites(layout))
                    {
                        Report("TRM3008", "The selected fixed-array local does not permit element writes.",
                            target.Span);
                    }
                    var index = BindNativeFixedArrayIndex((SyntaxNode)target.Children[2], layout);
                    if (index.Type != PrimitiveType.Int32
                        && !(index.Type == PrimitiveType.UInt32
                            && NativeFixedArrayContract.AllowsUInt32Indices(layout)))
                    {
                        Report("TRM3030", "The selected fixed-array layout does not permit this index type.",
                            index.Span);
                    }
                    var fixedArrayValue = BindExpression(valueNode, layout.ElementType);
                    return new BoundExpressionStatement(
                        new BoundNativeFixedArrayWrite(fixedArray, layout, index, fixedArrayValue, node.Span),
                        node.Span);
                }

                if (target.Kind == SyntaxKind.IndexExpression
                    && TryFindBuffer((SyntaxNode)target.Children[0], out var buffer))
                {
                    if (((SyntaxNode)target.Children[2]).Kind == SyntaxKind.SliceRangeExpression)
                    {
                        Report("TRM3008", "A slice range is not an assignment destination.", target.Span);
                        _ = BindExpression(valueNode, null);
                        return new BoundBlock([], node.Span);
                    }
                    if (!_buffers[buffer].IsWritable)
                    {
                        Report("TRM3008", "A read-only host buffer cannot be modified.", target.Span);
                    }
                    var indexExpression = BindBufferIndex((SyntaxNode)target.Children[2], buffer,
                        out var isEndRelative);
                    return new BoundBufferAssignment(buffer, indexExpression,
                        BindExpression(valueNode, _buffers[buffer].ElementType), node.Span)
                    {
                        IsEndRelativeOffset = isEndRelative,
                    };
                }
                if (target.Kind == SyntaxKind.MemberAccessExpression)
                {
                    return BindFieldAssignment(target, valueNode, node.Span);
                }
                if (target.Kind != SyntaxKind.NameExpression)
                {
                    Report("TRM3008", "Assignment requires a mutable local variable.", target.Span);
                    _ = BindExpression(valueNode, null);
                    return new BoundBlock([], node.Span);
                }

                var token = ((SyntaxTokenElement)target.Children[0]).Token;
                if (!TryFindVariable(token.Name, out var slot))
                {
                    if (_hasReceiver && _currentStruct is not null
                        && TryFindField(ResolveDeclaredType(_currentStruct), token.Name, out var ownField))
                    {
                        if (!_variables[0].IsMutable && !IsClass(_variables[0].Type))
                        {
                            Report("TRM3008", "An immutable receiver cannot be mutated.", target.Span);
                        }
                        var isUnsafeAuthorized = CheckFieldAccess(_variables[0].Type, ownField, target.Span);
                        CheckFieldMutation(_variables[0].Type, ownField, _constructor, target.Span);
                        return new BoundFieldAssignment(0, ownField,
                            BindExpression(valueNode, GetField(ResolveDeclaredType(_currentStruct), ownField).Type), node.Span) {
                            IsUnsafeAuthorized = isUnsafeAuthorized,
                        };
                    }
                    Report("TRM3006", $"Unknown or not-yet-declared variable '{token.Text}'.", token.Span);
                    _ = BindExpression(valueNode, null);
                    return new BoundBlock([], node.Span);
                }

                if (slot == 0 && _hasReceiver && !_constructor)
                {
                    Report("TRM3008", "A method cannot rebind its receiver.", token.Span);
                }
                else if (!_variables[slot].IsMutable && !_variables[slot].IsInitializationLocal)
                {
                    Report("TRM3008", $"Variable '{token.Text}' is immutable.", token.Span);
                }

                var unresolved = _unresolvedVacantLocals.Contains(slot);
                var value = BindExpression(valueNode, unresolved ? null : _variables[slot].Type);
                if (unresolved && value.Type != PrimitiveType.Void)
                {
                    _variables[slot] = _variables[slot] with { Type = value.Type };
                    _ = _unresolvedVacantLocals.Remove(slot);
                }
                return new BoundAssignment(slot, value, node.Span);
            }

            case SyntaxKind.CompoundAssignmentStatement:
            {
                var target = (SyntaxNode)node.Children[0];
                while (target.Kind == SyntaxKind.ParenthesizedExpression)
                {
                    target = (SyntaxNode)target.Children[1];
                }
                var op = ((SyntaxTokenElement)node.Children[1]).Token;
                var place = BindExpression(target, null);
                var compoundOperation = CompoundOperators.Binary(op.Kind);
                var floatingCompound = PrimitiveTypeFacts.IsFloating(place.Type)
                    && compoundOperation is TokenKind.Plus or TokenKind.Minus or TokenKind.Star
                        or TokenKind.Slash or TokenKind.Percent;
                if (!PrimitiveTypeFacts.IsIntegerOrWide(place.Type) && !floatingCompound)
                {
                    Report("TRM3007", "Built-in compound assignment requires a supported integer or floating place; nominal compound operators are unsupported.", op.Span);
                }
                CheckWritableUpdatePlace(place, target.Span);
                var isShift = op.Kind is TokenKind.ShiftLeftEquals or TokenKind.ShiftRightEquals
                    or TokenKind.ShiftRightUnsignedEquals;
                var rightNode = (SyntaxNode)node.Children[2];
                var rightKnown = KnownType(rightNode);
                var rightContext = isShift
                    ? rightKnown is null ? PrimitiveType.Int32 : (PrimitiveType?)null
                    : place.Type;
                var right = BindExpression(rightNode, rightContext);
                if ((!isShift && right.Type != place.Type)
                    || (isShift && !PrimitiveTypeFacts.IsIntegerOrWide(right.Type)))
                {
                    Report("TRM3007", "Compound assignment requires an exact operand type.", op.Span);
                }
                return new BoundExpressionStatement(new BoundCompoundAssignment(op.Kind, place, right,
                    place.Type, node.Span) {
                    OverflowBehavior = SourceOverflowBehavior(CompoundOperators.Binary(op.Kind), place.Type),
                }, node.Span);
            }

            case SyntaxKind.ExpressionStatement:
            {
                var expressionNode = (SyntaxNode)node.Children[0];
                var viewSet = BindViewSetStatement(expressionNode, node.Span);
                if (viewSet is not null)
                {
                    return viewSet;
                }
                var isDirectCall = expressionNode.Kind is SyntaxKind.InvocationExpression or SyntaxKind.UpdateExpression;
                if (expressionNode.Kind == SyntaxKind.UnsafeExpression)
                {
                    isDirectCall = ((SyntaxNode)expressionNode.Children[2]).Kind
                        is SyntaxKind.InvocationExpression or SyntaxKind.UpdateExpression;
                }
                var boundExpression = BindExpression(expressionNode, null);
                var isNonReturningConstruction = !isDirectCall
                    && boundExpression is BoundCall call
                    && (uint)call.Function < (uint)_signatures.Count
                    && _signatures[call.Function].IsConstructor
                    && _signatures[call.Function].DoesNotReturn;
                if (!isDirectCall && !isNonReturningConstruction)
                {
                    Report("TRM3009", "Only direct calls and updates may be used as expression statements.", expressionNode.Span);
                }

                return new BoundExpressionStatement(boundExpression, node.Span);
            }

            case SyntaxKind.DiscardStatement:
            {
                var expressionNode = (SyntaxNode)node.Children[2];
                var actionNode = expressionNode;
                while (actionNode.Kind == SyntaxKind.ParenthesizedExpression)
                {
                    actionNode = (SyntaxNode)actionNode.Children[1];
                }
                var viewSet = BindViewSetStatement(actionNode, node.Span);
                if (viewSet is not null)
                {
                    return viewSet;
                }
                var value = BindExpression(expressionNode, null);
                if (value.Type == PrimitiveType.Void)
                {
                    if (value is BoundCall or BoundUnsafeExpression { Expression: BoundCall })
                    {
                        return new BoundExpressionStatement(value, node.Span);
                    }
                    if (value is BoundConstant)
                    {
                        return new BoundBlock([], node.Span);
                    }
                    Report("TRM3009", "This void expression cannot be discarded.", node.Span);
                    return new BoundBlock([], node.Span);
                }

                var slot = _variables.Count;
                _variables.Add(new BoundVariable("<discard>", value.Type, false, node.Span));
                var statements = ImmutableArray.CreateBuilder<BoundStatement>();
                statements.Add(new BoundLocal(slot, value, node.Span));
                if (IsUnique(value.Type))
                {
                    var method = CleanupMethod(value.Type);
                    var cleanupFunction = method is not null && _functions.TryGetValue(method, out var found) ? found : -1;
                    statements.Add(new BoundTryFinally(new BoundBlock([], node.Span),
                        new BoundBlock([new BoundDrop(slot, cleanupFunction, node.Span)], node.Span), node.Span));
                }
                return new BoundBlock(statements.ToImmutable(), node.Span) {
                    IsManagedLexicalScope = true,
                    RetiredManagedLocals = ManagedClassTypes.IsManaged(value.Type) ? [slot] : [],
                };
            }

            case SyntaxKind.EmptyStatement:
                return new BoundBlock([], node.Span);

            case SyntaxKind.RequireStatement:
                return new BoundRequire(BindExpression((SyntaxNode)node.Children[1], PrimitiveType.Bool), node.Span);

            case SyntaxKind.StaticRequireStatement:
            case SyntaxKind.StaticEnsureStatement:
            {
                var isEnsure = node.Kind == SyntaxKind.StaticEnsureStatement;
                if (_genericContext is not null || (isEnsure && (_scopes.Count != 2 || _constructor)))
                {
                    Report("TRM3026", isEnsure
                        ? "Static ensure is supported only in the outer body of a nongeneric function."
                        : "Static require is not supported in a generic function template.", node.Span);
                }
                _hasStaticProof = true;
                return new BoundStaticProof(BindExpression((SyntaxNode)node.Children[2], PrimitiveType.Bool),
                    isEnsure, node.Span);
            }

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
                        if (value is BoundCall or BoundUnsafeExpression { Expression: BoundCall })
                        {
                            return new BoundBlock(
                                [new BoundExpressionStatement(value, valueNode.Span), new BoundReturn(null, node.Span)],
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

            case SyntaxKind.SwitchStatement:
                return BindSwitchStatement(node);

            case SyntaxKind.SwitchTransferStatement:
                return BindSwitchTransfer(node);

            case SyntaxKind.WhileStatement:
            {
                var condition = BindExpression((SyntaxNode)node.Children[2], PrimitiveType.Bool);
                _loops.Add(new LoopBinding { Name = loopLabel });
                var body = BindBlock((SyntaxNode)node.Children[4]);
                _loops.RemoveAt(_loops.Count - 1);
                return new BoundWhile(condition, body, node.Span);
            }

            case SyntaxKind.DoStatement:
            {
                _loops.Add(new LoopBinding { Name = loopLabel });
                var body = BindBlock((SyntaxNode)node.Children[1]);
                _loops.RemoveAt(_loops.Count - 1);
                var condition = BindExpression((SyntaxNode)node.Children[4], PrimitiveType.Bool);
                return new BoundWhile(condition, body, node.Span) { IsPostTested = true };
            }

            case SyntaxKind.LabeledLoopStatement:
            {
                var labelToken = ((SyntaxTokenElement)node.Children[0]).Token;
                var label = labelToken.Name;
                if (_loops.Exists(loop => loop.Name == label))
                {
                    Report("TRM3004", $"Loop label '{label}' already exists in an enclosing loop.",
                        labelToken.Span);
                }
                return BindStatement((SyntaxNode)node.Children[2], label);
            }

            case SyntaxKind.BreakStatement:
            case SyntaxKind.ContinueStatement:
            {
                var label = node.Children.Length == 3
                    ? ((SyntaxTokenElement)node.Children[1]).Token.Name : null;
                var target = label is null
                    ? _loops.FindLastIndex(static loop => !loop.IsSwitchDispatcher)
                    : _loops.FindLastIndex(loop => !loop.IsSwitchDispatcher && loop.Name == label);
                if (target < 0)
                {
                    Report("TRM3008", label is null ? "Loop transfer requires an enclosing loop."
                        : "Loop transfer requires an enclosing loop with the specified label.", node.Span);
                }
                else if (target < _finallyLoopDepth)
                {
                    Report("TRM3019", "A finally block cannot transfer control out of cleanup.", node.Span);
                }
                else if (node.Kind == SyntaxKind.ContinueStatement)
                {
                    _loops[target].HasContinue = true;
                }
                var depth = target < 0 ? 1 : _loops.Count - target;
                return node.Kind == SyntaxKind.BreakStatement ? new BoundBreak(node.Span) { TargetDepth = depth }
                    : new BoundContinue(node.Span) { TargetDepth = depth };
            }

            case SyntaxKind.ForStatement:
            {
                _scopes.Add(new Dictionary<string, int>(StringComparer.Ordinal));
                var firstVariable = _variables.Count;
                var initializer = BindStatement((SyntaxNode)node.Children[2]);
                if (initializer is BoundViewScope)
                {
                    Report("TRM3023", "Declare a native buffer view in a braced lexical block, not a for initializer.",
                        initializer.Span);
                }
                var conditionNode = (SyntaxNode)node.Children[4];
                var condition = conditionNode.Kind == SyntaxKind.OmittedExpression
                    ? new BoundConstant(1, PrimitiveType.Bool, conditionNode.Span)
                    : BindExpression(conditionNode, PrimitiveType.Bool);
                var iterator = BindStatement((SyntaxNode)node.Children[6]);
                var loopBinding = new LoopBinding { Name = loopLabel };
                _loops.Add(loopBinding);
                var body = BindBlock((SyntaxNode)node.Children[8]);
                _loops.RemoveAt(_loops.Count - 1);
                for (var slot = firstVariable; slot < _variables.Count; slot++)
                {
                    if (IsUnique(_variables[slot].Type)
                        && _scopes[^1].TryGetValue(_variables[slot].Name, out var scoped)
                        && scoped == slot
                        && !(initializer is BoundLocal ownerInitializer && ownerInitializer.Variable == slot)
                        && !(initializer is BoundVacantLocal vacantInitializer && vacantInitializer.Variable == slot))
                    {
                        Report("TRM3023", "An owning for-condition or iterator output has no lexical cleanup region.",
                            node.Span);
                    }
                }
                foreach (var slot in _scopes[^1].Values)
                {
                    if (_unresolvedVacantLocals.Remove(slot))
                    {
                        Report("TRM3016", $"Cannot infer the type of vacant local '{_variables[slot].Name}' from a checked initialization.",
                            _variables[slot].Span);
                    }
                }
                var managedLocals = _scopes[^1].Values
                    .Where(slot => ManagedClassTypes.IsManaged(_variables[slot].Type))
                    .Order().ToImmutableArray();
                _scopes.RemoveAt(_scopes.Count - 1);
                var loopBody = loopBinding.HasContinue ? body : new BoundBlock([body, iterator], node.Span);
                var loop = new BoundWhile(condition, loopBody, node.Span) {
                    Iterator = loopBinding.HasContinue ? iterator : null,
                };
                if (initializer is BoundLocal owner && IsUnique(_variables[owner.Variable].Type))
                {
                    var method = CleanupMethod(_variables[owner.Variable].Type);
                    var cleanupFunction = method is not null && _functions.TryGetValue(method, out var found) ? found : -1;
                    return new BoundBlock([
                        initializer,
                        new BoundTryFinally(new BoundBlock([loop], node.Span),
                            new BoundBlock([new BoundDrop(owner.Variable, cleanupFunction, node.Span)], node.Span), node.Span),
                    ], node.Span) {
                        IsManagedLexicalScope = true,
                        RetiredManagedLocals = managedLocals,
                    };
                }
                if (initializer is BoundVacantLocal vacant && IsUnique(_variables[vacant.Variable].Type))
                {
                    var method = CleanupMethod(_variables[vacant.Variable].Type);
                    var cleanupFunction = method is not null && _functions.TryGetValue(method, out var found) ? found : -1;
                    return new BoundBlock([
                        initializer,
                        new BoundTryFinally(new BoundBlock([loop], node.Span),
                            new BoundBlock([new BoundDrop(vacant.Variable, cleanupFunction, node.Span)], node.Span), node.Span),
                    ], node.Span) {
                        IsManagedLexicalScope = true,
                        RetiredManagedLocals = managedLocals,
                    };
                }
                return new BoundBlock([initializer, loop], node.Span) {
                    IsManagedLexicalScope = true,
                    RetiredManagedLocals = managedLocals,
                };
            }

            default:
                Report("TRM3001", "This statement is not supported.", node.Span);
                return new BoundBlock([], node.Span);
        }
    }

    private BoundStatement BindOptionalAssignment(SyntaxNode target, SyntaxNode value, TextSpan span)
    {
        while (target.Kind == SyntaxKind.ParenthesizedExpression)
        {
            target = (SyntaxNode)target.Children[1];
        }
        var place = BindExpression(target, null);
        if (!ManagedClassTypes.IsOptional(place.Type))
        {
            Report("TRM3007", "Coalescing assignment requires an initialized optional place.", target.Span);
            _ = BindExpression(value, null);
            return new BoundBlock([], span);
        }
        switch (place)
        {
            case BoundRead local:
                if (local.Variable == 0 && _hasReceiver && !_constructor)
                {
                    Report("TRM3008", "A method cannot rebind its receiver.", target.Span);
                }
                else if (!_variables[local.Variable].IsMutable)
                {
                    Report("TRM3008", "Coalescing assignment requires a mutable local.", target.Span);
                }
                break;
            case BoundFieldRead field when IsClass(field.Receiver.Type):
                if (!ManagedClassTypes.IsWritable(field.Receiver.Type))
                {
                    Report("TRM3008", "Field assignment requires a writable managed handle.", target.Span);
                }
                if (!GetField(field.Receiver.Type, field.Field).IsMutable)
                {
                    Report("TRM3008", "Coalescing assignment requires a mut field.", target.Span);
                }
                break;
            default:
                Report("TRM3008", "Coalescing assignment requires a mutable local or writable class field, not a value-returning getter or temporary.", target.Span);
                _ = BindExpression(value, place.Type);
                return new BoundBlock([], span);
        }
        return new BoundOptionalAssignment(place, BindExpression(value, place.Type), span);
    }

    private BoundStatement BindFieldAssignment(SyntaxNode target, SyntaxNode value, TextSpan span)
    {
        var members = new Stack<SyntaxToken>();
        var root = target;
        while (root.Kind is SyntaxKind.MemberAccessExpression or SyntaxKind.ParenthesizedExpression)
        {
            if (root.Kind == SyntaxKind.ParenthesizedExpression)
            {
                root = (SyntaxNode)root.Children[1];
            }
            else
            {
                members.Push(((SyntaxTokenElement)root.Children[2]).Token);
                root = (SyntaxNode)root.Children[0];
            }
        }
        var fields = ImmutableArray.CreateBuilder<int>();
        var variable = -1;
        if (root.Kind == SyntaxKind.NameExpression)
        {
            var name = ((SyntaxTokenElement)root.Children[0]).Token;
            if (!TryFindVariable(name.Name, out variable)
                && _hasReceiver && _currentStruct is not null
                && TryFindField(ResolveDeclaredType(_currentStruct), name.Name, out var ownField))
            {
                variable = 0;
                fields.Add(ownField);
            }
        }
        if (variable < 0)
        {
            Report("TRM3008", "Field assignment requires a mutable local or receiver, not a temporary value.", target.Span);
            _ = BindExpression(value, null);
            return new BoundBlock([], span);
        }
        var type = _variables[variable].Type;
        var isUnsafeAuthorized = false;
        if (fields.Count != 0)
        {
            isUnsafeAuthorized |= CheckFieldAccess(type, fields[0], root.Span);
            if (members.Count == 0 || !IsClass(type) || !IsClass(GetField(type, fields[0]).Type))
            {
                CheckFieldMutation(type, fields[0], _constructor && variable == 0, root.Span);
            }
            type = GetField(type, fields[0]).Type;
        }
        var remainingMembers = members.Count;
        foreach (var member in members)
        {
            if (!TryFindField(type, member.Name, out var field))
            {
                Report("TRM3008", $"Unknown field '{member.Text}' in assignment target.", member.Span);
                _ = BindExpression(value, null);
                return new BoundBlock([], span);
            }
            isUnsafeAuthorized |= CheckFieldAccess(type, field, member.Span);
            remainingMembers--;
            if (remainingMembers == 0 || !IsClass(type) || !IsClass(GetField(type, field).Type))
            {
                CheckFieldMutation(type, field, _constructor && variable == 0 && fields.Count == 0, member.Span);
            }
            fields.Add(field);
            type = GetField(type, field).Type;
        }
        if (!_variables[variable].IsMutable && !IsClass(_variables[variable].Type))
        {
            Report("TRM3008", $"Variable '{_variables[variable].Name}' is immutable.", target.Span);
        }
        var path = fields.ToImmutable();
        var fieldType = GetField(_variables[variable].Type, path[0]).Type;
        if (IsUnique(fieldType) && (variable != 0 || !_constructor || path.Length != 1))
        {
            Report("TRM3023", "An owning field can only be initialized as a whole in its parent's constructor.", target.Span);
        }
        for (var index = 1; index < path.Length; index++)
        {
            if (IsUnique(fieldType))
            {
                Report("TRM3023", "Mutation through an owning field loan is unsupported.", target.Span);
                break;
            }
            fieldType = GetField(fieldType, path[index]).Type;
        }
        return new BoundFieldAssignment(variable, path[0], BindExpression(value, type), span) {
            NestedFields = path.RemoveAt(0),
            IsUnsafeAuthorized = isUnsafeAuthorized,
        };
    }

    private void CheckWritableUpdatePlace(BoundExpression place, TextSpan span)
    {
        switch (place)
        {
            case BoundRead local:
                if (!_variables[local.Variable].IsMutable || (local.Variable == 0 && _hasReceiver))
                {
                    Report("TRM3008", "Update requires a mutable local, not a receiver or immutable slot.", span);
                }
                break;
            case BoundFieldRead field:
                CheckUpdateFieldPlace(field);
                break;
            case BoundBufferRead buffer:
                if (!_buffers[buffer.Buffer].IsWritable)
                {
                    Report("TRM3008", "Update requires a writable host buffer.", span);
                }
                break;
            default:
                Report("TRM3008", "Update requires an initialized writable place, not a getter or temporary.", span);
                break;
        }
    }

    private void CheckUpdateFieldPlace(BoundFieldRead place)
    {
        var receiver = place.Receiver;
        if (IsClass(receiver.Type))
        {
            CheckFieldMutation(receiver.Type, place.Field, false, place.Span);
            return;
        }
        if (!IsStruct(receiver.Type)
            || (IsUnique(receiver.Type) && receiver is not BoundRead))
        {
            Report("TRM3008", "Update requires a writable Copy field path.", place.Span);
            return;
        }
        CheckFieldMutation(receiver.Type, place.Field, false, place.Span);
        if (receiver is BoundFieldRead parent)
        {
            CheckUpdateFieldPlace(parent);
        }
        else if (receiver is not BoundRead root || !_variables[root.Variable].IsMutable)
        {
            Report("TRM3008", "A Copy field update requires a mutable local or writable class field, not a temporary.", place.Span);
        }
    }

    private void CheckFieldMutation(PrimitiveType type, int field, bool constructingReceiver, TextSpan span)
    {
        var declaration = GetField(type, field);
        if (IsClass(type) && !ManagedClassTypes.IsWritable(type) && !constructingReceiver)
        {
            Report("TRM3008", "Field assignment requires a writable managed handle.", span);
        }
        if (!constructingReceiver && !declaration.IsMutable)
        {
            Report("TRM3008", $"Field '{declaration.Name}' is read-only after construction; assignment requires a mut field.", span);
        }
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Only field acquisitions and nested control regions contribute to the order.")]
    private ImmutableArray<int> ConstructorAcquisitions(BoundBlock body, PrimitiveType ownerType)
    {
        var order = ImmutableArray.CreateBuilder<int>();
        Collect(body, sequential: true);
        return order.ToImmutable();

        void Collect(BoundStatement statement, bool sequential)
        {
            switch (statement)
            {
                case BoundBlock block:
                    foreach (var child in block.Statements)
                    {
                        Collect(child, sequential);
                    }
                    break;
                case BoundTryFinally region:
                    Collect(region.Body, sequential);
                    Collect(region.Finally, sequential: false);
                    break;
                case BoundFieldAssignment field when field.Variable == 0
                    && field.Field >= 0 && field.Field < _structs[(int)ownerType - 100].Fields.Length
                    && IsUnique(_structs[(int)ownerType - 100].Fields[field.Field].Type):
                    if (!sequential || !field.NestedFields.IsEmpty || order.Contains(field.Field))
                    {
                        Report("TRM3023", "Owning fields must be initialized once in sequential constructor flow.", field.Span);
                    }
                    order.Add(field.Field);
                    break;
                case BoundIf conditional:
                    Collect(conditional.Then, sequential: false);
                    if (conditional.Else is not null)
                    {
                        Collect(conditional.Else, sequential: false);
                    }
                    break;
                case BoundWhile loop:
                    Collect(loop.Body, sequential: false);
                    if (loop.Iterator is not null)
                    {
                        Collect(loop.Iterator, sequential: false);
                    }
                    break;
                case BoundTryCatch recovery:
                    Collect(recovery.Body, sequential: false);
                    Collect(recovery.Catch, sequential: false);
                    if (recovery.Finally is not null)
                    {
                        Collect(recovery.Finally, sequential: false);
                    }
                    break;
                case BoundUnsafe unsafeBlock:
                    Collect(unsafeBlock.Body, sequential);
                    break;
            }
        }
    }

    private bool ContainsAuthorizedExpressionScopeCall(BoundExpression expression) => expression switch {
        BoundUnsafeExpression unsafeExpression => ContainsAuthorizedExpressionScopeCall(unsafeExpression.Expression),
        BoundRequiredConstant required => ContainsAuthorizedExpressionScopeCall(required.Operand),
        BoundCall call => (call.IsUnsafeAuthorized && call.Function >= 0
            && call.Function < _signatures.Count
            && _signatures[call.Function].RequiresUnsafeCaller)
            || call.Arguments.Any(ContainsAuthorizedExpressionScopeCall),
        BoundNativeFunctionAddress address => address.IsUnsafeAuthorized,
        BoundIndirectCall call => call.IsUnsafeAuthorized
            || ContainsAuthorizedExpressionScopeCall(call.Target)
            || call.Arguments.Any(ContainsAuthorizedExpressionScopeCall),
        BoundUnary unary => ContainsAuthorizedExpressionScopeCall(unary.Operand),
        BoundUpdate update => ContainsAuthorizedExpressionScopeCall(update.Place),
        BoundNominalUpdate update => (update.IsUnsafeAuthorized
            && update.Function >= 0
            && update.Function < _signatures.Count
            && IsSupportedExpressionStaticUpdateOperator(_signatures[update.Function], update.Operator))
            || ContainsAuthorizedExpressionScopeCall(update.Place),
        BoundCompoundAssignment compound => ContainsAuthorizedExpressionScopeCall(compound.Place)
            || ContainsAuthorizedExpressionScopeCall(compound.Right),
        BoundBinary binary => ContainsAuthorizedExpressionScopeCall(binary.Left)
            || ContainsAuthorizedExpressionScopeCall(binary.Right),
        BoundConditional conditional => ContainsAuthorizedExpressionScopeCall(conditional.Condition)
            || ContainsAuthorizedExpressionScopeCall(conditional.WhenTrue)
            || ContainsAuthorizedExpressionScopeCall(conditional.WhenFalse),
        BoundCoalesce coalesce => ContainsAuthorizedExpressionScopeCall(coalesce.Optional)
            || ContainsAuthorizedExpressionScopeCall(coalesce.Fallback),
        BoundCast cast => ContainsAuthorizedExpressionScopeCall(cast.Operand),
        BoundFieldRead field => field.IsUnsafeAuthorized
            || ContainsAuthorizedExpressionScopeCall(field.Receiver),
        BoundFieldLoan loan => ContainsAuthorizedExpressionScopeCall(loan.Place),
        BoundMutableFieldLoan loan => ContainsAuthorizedExpressionScopeCall(loan.Place),
        BoundViewRead view => ContainsAuthorizedExpressionScopeCall(view.Index),
        BoundBufferRead buffer => ContainsAuthorizedExpressionScopeCall(buffer.Index),
        BoundSpanLength length => ContainsAuthorizedExpressionScopeCall(length.View),
        BoundSpanRead read => ContainsAuthorizedExpressionScopeCall(read.View)
            || ContainsAuthorizedExpressionScopeCall(read.Index),
        BoundSpanAdvance advance => ContainsAuthorizedExpressionScopeCall(advance.View),
        BoundOptionalLift lift => ContainsAuthorizedExpressionScopeCall(lift.Value),
        BoundReadonlyProjection projection => ContainsAuthorizedExpressionScopeCall(projection.Value),
        BoundIsNull test => ContainsAuthorizedExpressionScopeCall(test.Value),
        BoundIsSome pattern => ContainsAuthorizedExpressionScopeCall(pattern.Value),
        BoundIsVariablePattern pattern => ContainsAuthorizedExpressionScopeCall(pattern.Value),
        _ => false,
    };

    private static bool IsSupportedExpressionStaticUpdateOperator(Signature signature, TokenKind updateOperator) =>
        signature.IsStatic && signature.IsOperator && signature.StructName is string owner
        && TryGetStaticOperatorKey(signature.Name, owner, out var key)
        && key.Arity == 1 && key.Operator == updateOperator
        && updateOperator is TokenKind.PlusPlus or TokenKind.MinusMinus;

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Unsupported syntax kinds are rejected by the default branch.")]
    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Runtime struct IDs are represented by enum values outside the declared primitive range.")]
    private BoundExpression BindExpression(SyntaxNode node, PrimitiveType? expected)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var diagnosticCount = DiagnosticCount;
        BoundExpression expression;

        switch (node.Kind)
        {
            case SyntaxKind.ParenthesizedExpression:
                expression = BindExpression((SyntaxNode)node.Children[1], expected);
                break;

            case SyntaxKind.UnsafeExpression:
            {
                var alreadyUnsafe = _unsafeDepth != 0;
                _unsafeDepth++;
                BoundExpression operand;
                try
                {
                    operand = BindExpression((SyntaxNode)node.Children[2], expected);
                }
                finally
                {
                    _unsafeDepth--;
                }
                expression = !alreadyUnsafe && ContainsAuthorizedExpressionScopeCall(operand)
                    ? new BoundUnsafeExpression(operand, node.Span)
                    : operand;
                break;
            }

            case SyntaxKind.ConstExpression:
            {
                var operand = BindExpression((SyntaxNode)node.Children[1], expected);
                if (!ConstEvalScalar(operand.Type))
                {
                    Report("TRM3024", "A required constant expression must have a scalar type.", node.Span);
                }
                _hasRequiredExpression = true;
                expression = new BoundRequiredConstant(operand, node.Span);
                break;
            }

            case SyntaxKind.VoidExpression:
                expression = new BoundConstant(0, PrimitiveType.Void, node.Span);
                break;

            case SyntaxKind.LiteralExpression:
                var literalToken = ((SyntaxTokenElement)node.Children[0]).Token;
                if (literalToken.Kind == TokenKind.NullKeyword)
                {
                    if (expected == PrimitiveType.NativeRawPointer || (expected is PrimitiveType pointerType
                        && NativePointerTypes.IsType(pointerType)))
                    {
                        expression = new BoundConstant(0, expected.Value, node.Span);
                    }
                    else if (expected is not PrimitiveType optionalType || !ManagedClassTypes.IsOptional(optionalType))
                    {
                        Report("TRM3007", "'null' requires an optional managed-handle type.", node.Span);
                        expression = ErrorExpression(node.Span);
                    }
                    else
                    {
                        expression = new BoundNull(optionalType, node.Span);
                    }
                }
                else
                {
                    expression = BindLiteral(literalToken, expected);
                }
                break;

            case SyntaxKind.IsNullExpression:
                var nullable = BindExpression((SyntaxNode)node.Children[0], null);
                if (!ManagedClassTypes.IsOptional(nullable.Type))
                {
                    Report("TRM3007", "'is null' requires an optional managed handle.", node.Span);
                }
                expression = new BoundIsNull(nullable, node.Span);
                break;

            case SyntaxKind.IsSomeExpression:
                var source = BindExpression((SyntaxNode)node.Children[0], null);
                if (!ManagedClassTypes.IsOptional(source.Type))
                {
                    var pattern = ((SyntaxTokenElement)node.Children[2]).Token.Text;
                    Report("TRM3007", pattern == "var"
                        ? "'is var' requires an optional managed handle."
                        : "'is some' requires an optional managed handle.", node.Span);
                }
                var name = ((SyntaxTokenElement)node.Children[3]).Token;
                var payload = ManagedClassTypes.IsOptional(source.Type)
                    ? ManagedClassTypes.Required(source.Type) : PrimitiveType.Int32;
                var patternSlot = AddVariable(name.Name, payload, false, name.Span);
                expression = ((SyntaxTokenElement)node.Children[2]).Token.Text == "var"
                    && source is BoundRead read
                    ? new BoundIsVariablePattern(source, patternSlot, false, node.Span)
                    : new BoundIsSome(source, patternSlot, node.Span);
                break;

            case SyntaxKind.IsInitializedExpression:
                var initializedSourceNode = (SyntaxNode)node.Children[0];
                if (TryFindInitReferenceDescriptor(initializedSourceNode, out _))
                {
                    expression = new BoundConstant(1, PrimitiveType.Bool, node.Span);
                }
                else if (TryFindInitReferenceProjection(initializedSourceNode, out var initializedDestination))
                {
                    expression = new BoundIsInitialized(initializedDestination, node.Span);
                }
                else
                {
                    var initializedSource = BindExpression(initializedSourceNode, null);
                    if (initializedSource is not BoundRead initializedRead)
                    {
                        Report("TRM3007", "'is init' requires a named local or parameter.", node.Span);
                        expression = new BoundConstant(0, PrimitiveType.Bool, node.Span);
                    }
                    else
                    {
                        expression = new BoundIsInitialized(initializedRead.Variable, node.Span);
                    }
                }
                break;

            case SyntaxKind.IsVarNullableExpression:
                var nullableSource = BindExpression((SyntaxNode)node.Children[0], null);
                if (!ManagedClassTypes.IsOptional(nullableSource.Type))
                {
                    Report("TRM3007", "'is var?' requires an optional managed handle.", node.Span);
                }
                var nullableName = ((SyntaxTokenElement)node.Children[4]).Token;
                var nullableType = ManagedClassTypes.IsOptional(nullableSource.Type) ? nullableSource.Type : PrimitiveType.Int32;
                var nullableSlot = AddVariable(nullableName.Name, nullableType, false, nullableName.Span);
                expression = new BoundIsVariablePattern(nullableSource, nullableSlot, true, node.Span);
                break;

            case SyntaxKind.NameExpression:
            {
                var token = ((SyntaxTokenElement)node.Children[0]).Token;
                if (TryFindVariable(token, out var slot))
                {
                    if (NativeFixedArrayContract.IsCarrier(_variables[slot].Type))
                    {
                        Report("TRM3030", "Whole sample arrays are local-only and cannot be read as values.", node.Span);
                        expression = ErrorExpression(node.Span);
                    }
                    else
                    {
                        expression = new BoundRead(slot, _variables[slot].Type, node.Span);
                    }
                }
                else if (_hasReceiver && _currentStruct is not null
                    && _primitiveBackingFields.TryGetValue(_currentStruct, out var backing)
                    && token.Name == backing)
                {
                    expression = new BoundRead(0, _variables[0].Type, node.Span);
                }
                else if (_hasReceiver && _currentStruct is not null
                    && TryFindField(ResolveDeclaredType(_currentStruct), token.Name, out var field))
                {
                    var structure = ResolveDeclaredType(_currentStruct);
                    var isUnsafeAuthorized = CheckFieldAccess(structure, field, node.Span);
                    expression = new BoundFieldRead(new BoundRead(0, _variables[0].Type, node.Span),
                        field, GetField(structure, field).Type, node.Span) {
                        IsUnsafeAuthorized = isUnsafeAuthorized,
                    };
                }
                else if (_hasReceiver && _currentStruct is not null
                    && TryFindGetter(ResolveDeclaredType(_currentStruct), token.Name, out var getter))
                {
                    expression = BindArguments(node, new SyntaxNode(SyntaxKind.ArgumentList, [], node.Span.End),
                        getter, null);
                }
                else if (_currentStruct is not null
                    && TryFindStaticGetter(ResolveDeclaredType(_currentStruct), token.Name, out var staticGetter))
                {
                    expression = BindArguments(node, new SyntaxNode(SyntaxKind.ArgumentList, [], node.Span.End),
                        staticGetter, null);
                }
                else
                {
                    Report("TRM3006", $"Unknown or not-yet-declared variable '{token.Text}'.", token.Span);
                    expression = ErrorExpression(node.Span);
                }

                break;
            }

            case SyntaxKind.MemberAccessExpression:
            {
                if (((SyntaxTokenElement)node.Children[2]).Token.Text == "Length"
                    && TryFindBuffer((SyntaxNode)node.Children[0], out var buffer))
                {
                    expression = _buffers[buffer].IsInvocationScoped
                        ? new BoundBufferLength(buffer, node.Span)
                        : new BoundConstant(_buffers[buffer].Values.Length, PrimitiveType.Int32, node.Span);
                    break;
                }
                var member = ((SyntaxTokenElement)node.Children[2]).Token.Name;
                var receiverNode = (SyntaxNode)node.Children[0];
                var receiverType = KnownType(receiverNode);
                if (receiverType == PrimitiveType.TokenSpan && member == "Length")
                {
                    expression = new BoundSpanLength(BindExpression(receiverNode, PrimitiveType.TokenSpan), node.Span);
                    break;
                }
                if (_currentStruct is not null
                    && _primitiveBackingFields.TryGetValue(_currentStruct, out var backing)
                    && member == backing
                    && receiverType is PrimitiveType primitive
                    && TryPrimitiveType(_currentStruct, out var owner) && owner == primitive)
                {
                    expression = BindExpression(receiverNode, owner);
                    break;
                }
                if (receiverType is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView
                    && member == "Length")
                {
                    expression = new BoundViewLength(BindViewVariable(receiverNode), node.Span);
                    break;
                }
                if (receiverType == PrimitiveType.OwnedInt32Buffer && member is "Count" or "Capacity"
                    && TryFindFunction(NativeBufferIntrinsics.TypeName + "." + member, out var intrinsic))
                {
                    expression = new BoundCall(intrinsic,
                        [BindReceiverLoan(receiverNode, PrimitiveType.OwnedInt32Buffer)], PrimitiveType.Int32, node.Span);
                    break;
                }
                if (receiverType is PrimitiveType nodeType
                    && ManagedClassTypes.Readonly(nodeType) == PrimitiveType.ManagedNode && member == "Value"
                    && TryFindFunction(NativeManagedNodeIntrinsics.TypeName + ".Value", out var nodeValue))
                {
                    expression = new BoundCall(nodeValue,
                        [BindExpression(receiverNode, PrimitiveType.ManagedNode)], PrimitiveType.Int32, node.Span);
                    break;
                }
                if (receiverType is PrimitiveType fieldType && TryFindField(fieldType, member, out var field))
                {
                    var isUnsafeAuthorized = CheckFieldAccess(fieldType, field, node.Span);
                    expression = new BoundFieldRead(BindExpression(receiverNode, fieldType), field,
                        GetField(fieldType, field).Type, node.Span) {
                        IsUnsafeAuthorized = isUnsafeAuthorized,
                    };
                    break;
                }
                if (receiverType is PrimitiveType propertyType
                    && TryFindGetter(propertyType, member, out var getter))
                {
                    expression = BindArguments(node, new SyntaxNode(SyntaxKind.ArgumentList, [], node.Span.End),
                        getter, receiverNode);
                    break;
                }
                if (receiverType is null && !HasVariableReceiverName(receiverNode))
                {
                    var resolution = ResolveStaticMember(receiverNode, member, out var staticMember);
                    if (resolution == DeclaredTypeResolution.Ambiguous)
                    {
                        Report("TRM3011", $"Type '{NameText(receiverNode)}' is ambiguous across declared and imported types.",
                            receiverNode.Span);
                        expression = ErrorExpression(node.Span);
                        break;
                    }
                    if (resolution == DeclaredTypeResolution.Unique
                        && TryFindStaticGetter(staticMember, member, out var staticGetter))
                    {
                        expression = BindArguments(node, new SyntaxNode(SyntaxKind.ArgumentList, [], node.Span.End),
                            staticGetter, null);
                        break;
                    }
                }
                var typeName = NameText(receiverNode);
                if (member is "MinValue" or "MaxValue" && typeName is "int32" or "uint32" or "int64" or "uint64" or "char16")
                {
                    var type = typeName switch {
                        "int32" => PrimitiveType.Int32,
                        "uint32" => PrimitiveType.UInt32,
                        "int64" => PrimitiveType.Int64,
                        "uint64" => PrimitiveType.UInt64,
                        "int128" => PrimitiveType.Int128,
                        "uint128" => PrimitiveType.UInt128,
                        "char16" => PrimitiveType.Char16,
                        _ => throw new InvalidOperationException(),
                    };
                    var value = type switch {
                        PrimitiveType.Int32 => member == "MinValue" ? int.MinValue : int.MaxValue,
                        PrimitiveType.UInt32 => member == "MinValue" ? 0 : -1,
                        PrimitiveType.Int64 => member == "MinValue" ? long.MinValue : long.MaxValue,
                        PrimitiveType.UInt64 => member == "MinValue" ? 0L : -1L,
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
                if (op.Kind == TokenKind.Star)
                {
                    if (TryFindInitReferenceProjection(node, out var destination))
                    {
                        expression = new BoundRead(destination, _variables[destination].Type, node.Span);
                    }
                    else if (KnownType(operandNode) is PrimitiveType pointerType
                        && NativePointerTypeContract.IsValidType(pointerType, [.. _nativePointerTypes]))
                    {
                        var pointeeType = _nativePointerTypes[NativePointerTypes.Index(pointerType)].PointeeType;
                        if (pointeeType == PrimitiveType.Void)
                        {
                            Report("TRM3030", "A void pointer cannot be dereferenced.", node.Span);
                            expression = ErrorExpression(node.Span);
                            break;
                        }
                        if (!DirectNativeImportContract.IsCopyablePointee(pointeeType, _structs))
                        {
                            Report("TRM3023",
                                "A raw pointer cannot load or store an owning value without preserving its ownership and cleanup.",
                                node.Span);
                        }
                        if (_unsafeDepth == 0)
                        {
                            Report("TRM3028", "A pointer read requires an explicit unsafe context.", node.Span);
                        }
                        expression = new BoundNativePointerRead(
                            BindExpression(operandNode, pointerType), pointeeType, node.Span) {
                            IsUnsafeAuthorized = _unsafeDepth != 0,
                        };
                    }
                    else
                    {
                        Report("TRM3030", "Dereference requires an init ref destination in this source profile.",
                            op.Span);
                        expression = ErrorExpression(node.Span);
                    }
                    break;
                }
                if (op.Kind == TokenKind.Ampersand)
                {
                    if (expected is PrimitiveType expectedPointerType
                        && NativePointerTypeContract.IsValidType(expectedPointerType, [.. _nativePointerTypes]))
                    {
                        if (operandNode.Kind == SyntaxKind.IndexExpression)
                        {
                            expression = BindNativeFixedArrayPointerAddress(operandNode,
                                expectedPointerType, node.Span);
                            break;
                        }
                        if (operandNode.Kind != SyntaxKind.NameExpression
                            || !TryFindVariable(
                                ((SyntaxTokenElement)operandNode.Children[0]).Token,
                                out var pointerVariable)
                            || _nativePointerTypes[NativePointerTypes.Index(expectedPointerType)].PointeeType
                                != _variables[pointerVariable].Type
                            || !DirectNativeImportContract.IsInputPointee(
                                _variables[pointerVariable].Type, _structs)
                            || (_nativePointerTypes[NativePointerTypes.Index(expectedPointerType)].IsWritable
                                && !_variables[pointerVariable].IsMutable))
                        {
                            Report("TRM3030",
                                "Taking a native pointer address requires a matching local value and access mode.",
                                node.Span);
                            expression = ErrorExpression(node.Span);
                        }
                        else
                        {
                            if (_unsafeDepth == 0)
                            {
                                Report("TRM3028",
                                    "Taking a native pointer address requires an explicit unsafe context.",
                                    node.Span);
                            }
                            expression = new BoundNativePointerAddress(pointerVariable,
                                expectedPointerType, node.Span) {
                                IsUnsafeAuthorized = _unsafeDepth != 0,
                            };
                        }
                        break;
                    }
                    var functionName = NameText(operandNode);
                    if (expected is PrimitiveType nativeFunctionPointerType
                        && NativeFunctionPointerTypes.IsType(nativeFunctionPointerType)
                        && operandNode.Kind is SyntaxKind.NameExpression or SyntaxKind.MemberAccessExpression
                        && !TryFindVariable(functionName, out _)
                        && TryFindFunction(functionName, out var nativeCallable))
                    {
                        var signature = _signatures[nativeCallable];
                        if (signature.NativeFunctionPointerType != nativeFunctionPointerType)
                        {
                            Report("TRM3030",
                                "The function address does not match the expected native function pointer signature.",
                                node.Span);
                            expression = ErrorExpression(node.Span);
                        }
                        else
                        {
                            if (_unsafeDepth == 0)
                            {
                                Report("TRM3028",
                                    "Taking a native function address requires an explicit unsafe context.",
                                    node.Span);
                            }
                            expression = new BoundNativeFunctionAddress(nativeCallable,
                                nativeFunctionPointerType, node.Span) {
                                IsUnsafeAuthorized = _unsafeDepth != 0,
                            };
                        }
                    }
                    else
                    {
                        Report("TRM3030", "A function address requires a concrete native function pointer type.",
                            node.Span);
                        expression = ErrorExpression(node.Span);
                    }
                    break;
                }
                if (op.Kind == TokenKind.MoveKeyword)
                {
                    while (operandNode.Kind == SyntaxKind.ParenthesizedExpression)
                    {
                        operandNode = (SyntaxNode)operandNode.Children[1];
                    }
                    if (operandNode.Kind == SyntaxKind.NameExpression
                        && TryFindVariable(((SyntaxTokenElement)operandNode.Children[0]).Token.Name, out var owner)
                        && IsUnique(_variables[owner].Type) && (!_constructor || owner != 0))
                    {
                        expression = new BoundMove(owner, _variables[owner].Type, node.Span);
                    }
                    else
                    {
                        Report("TRM3023", "Move requires a named unique local; a constructor receiver cannot move.", node.Span);
                        expression = ErrorExpression(node.Span);
                    }
                    break;
                }
                if (KnownType(operandNode) is PrimitiveType concrete
                    && IsStruct(concrete) && IsStaticUnaryOperator(op.Kind))
                {
                    var operandValue = BindExpression(operandNode, concrete);
                    var operatorName = _structs[(int)concrete - 100].Name + "."
                        + OperatorName(new StaticOperatorKey(op.Kind, 1));
                    if (!TryFindFunction(operatorName, out var method)
                        || _signatures[method].Parameters is not [var parameter]
                        || parameter.Type != concrete || parameter.IsOut
                        || !_signatures[method].IsStatic || !_signatures[method].IsOperator)
                    {
                        Report("TRM3007", "No matching accessible unary static operator is declared.", op.Span);
                        expression = ErrorExpression(node.Span);
                        break;
                    }
                    if (_signatures[method].IsPrivate && _currentStruct != _signatures[method].StructName)
                    {
                        Report("TRM3011", "Private operator is inaccessible.", op.Span);
                    }
                    expression = new BoundCall(method, [operandValue], _signatures[method].ReturnType, node.Span) {
                        IsUnsafeAuthorized = CheckUnsafeCaller(_signatures[method], node.Span),
                    };
                    break;
                }
                var operandType = op.Kind == TokenKind.Bang ? PrimitiveType.Bool
                    : KnownType(operandNode) ?? expected ?? PrimitiveType.Int32;

                if (op.Kind is TokenKind.Plus or TokenKind.Minus
                    && operandNode.Kind == SyntaxKind.LiteralExpression
                    && TryBindSignedIntegerLiteral(
                        ((SyntaxTokenElement)operandNode.Children[0]).Token,
                        operandType, op.Kind == TokenKind.Minus, node.Span,
                        out var signedLiteral))
                {
                    expression = signedLiteral;
                    break;
                }
                if (op.Kind is TokenKind.Plus or TokenKind.Minus
                    && operandNode.Kind == SyntaxKind.LiteralExpression
                    && operandType is PrimitiveType.Float16 or PrimitiveType.BFloat16
                        or PrimitiveType.Float128 or PrimitiveType.Decimal32
                        or PrimitiveType.Decimal64 or PrimitiveType.Decimal128
                    && TryBindSignedWideFloatingLiteral(
                        ((SyntaxTokenElement)operandNode.Children[0]).Token,
                        operandType, op.Kind == TokenKind.Minus, node.Span,
                        out var signedFloatingLiteral))
                {
                    expression = signedFloatingLiteral;
                    break;
                }

                var expectedNumeric = expected is PrimitiveType expectedType
                    && (PrimitiveTypeFacts.IsIntegerOrWide(expectedType)
                        || PrimitiveTypeFacts.IsFloating(expectedType));
                var knownNumeric = KnownType(operandNode) is PrimitiveType knownType
                    && (PrimitiveTypeFacts.IsIntegerOrWide(knownType)
                        || PrimitiveTypeFacts.IsFloating(knownType));
                var operandContext = operandType == PrimitiveType.Bool ? operandType
                    : expectedNumeric || knownNumeric ? operandType : (PrimitiveType?)null;
                var operand = BindExpression(operandNode, operandContext);
                if (operand.Type != operandType)
                {
                    Report("TRM3007", $"Unary operator requires {TypeName(operandType)}.", op.Span);
                }
                if (op.Kind is not (TokenKind.Plus or TokenKind.Minus or TokenKind.Tilde or TokenKind.Bang)
                    || (op.Kind == TokenKind.Minus && !PrimitiveTypeFacts.IsIntegerOrWide(operandType)
                        && !PrimitiveTypeFacts.IsFloating(operandType))
                    || (op.Kind == TokenKind.Plus && !PrimitiveTypeFacts.IsIntegerOrWide(operandType)
                        && !PrimitiveTypeFacts.IsFloating(operandType))
                    || (op.Kind == TokenKind.Tilde && !PrimitiveTypeFacts.IsIntegerOrWide(operandType)))
                {
                    Report("TRM3001", "Unsupported unary operator.", op.Span);
                }

                expression = new BoundUnary(op.Kind, operand, operandType, node.Span) {
                    OverflowBehavior = SourceOverflowBehavior(op.Kind, operandType, unary: true),
                };
                break;
            }

            case SyntaxKind.UpdateExpression:
            {
                var prefix = node.Children[0] is SyntaxTokenElement;
                var op = ((SyntaxTokenElement)node.Children[prefix ? 0 : 1]).Token;
                var updateTarget = (SyntaxNode)node.Children[prefix ? 1 : 0];
                while (updateTarget.Kind == SyntaxKind.ParenthesizedExpression)
                {
                    updateTarget = (SyntaxNode)updateTarget.Children[1];
                }
                var place = BindExpression(updateTarget, null);
                var nominal = IsStruct(place.Type) && !IsUnique(place.Type);
                if (!nominal && !PrimitiveTypeFacts.IsIntegerOrWide(place.Type)
                    && !PrimitiveTypeFacts.IsFloating(place.Type))
                {
                    Report("TRM3007", "Increment and decrement require a Copy integer or Copy struct place.", updateTarget.Span);
                }
                CheckWritableUpdatePlace(place, updateTarget.Span);
                if (nominal)
                {
                    var operatorName = _structs[(int)place.Type - 100].Name + "."
                        + OperatorName(new StaticOperatorKey(op.Kind, 1));
                    if (!TryFindFunction(operatorName, out var method)
                        || _signatures[method].Parameters is not [var parameter]
                        || parameter.Type != place.Type || parameter.IsOut
                        || _signatures[method].ReturnType != place.Type
                        || !_signatures[method].IsStatic || !_signatures[method].IsOperator)
                    {
                        Report("TRM3007", "No matching exact-result unary static update operator is declared.", op.Span);
                        expression = ErrorExpression(node.Span);
                        break;
                    }
                    if (_signatures[method].IsPrivate && _currentStruct != _signatures[method].StructName)
                    {
                        Report("TRM3011", "Private update operator is inaccessible.", op.Span);
                    }
                    expression = new BoundNominalUpdate(op.Kind, place, prefix, method, place.Type, node.Span) {
                        IsUnsafeAuthorized = CheckUnsafeCaller(_signatures[method], node.Span),
                    };
                }
                else
                {
                    expression = new BoundUpdate(op.Kind, place, prefix, place.Type, node.Span) {
                        OverflowBehavior = SourceOverflowBehavior(op.Kind, place.Type),
                    };
                }
                break;
            }

            case SyntaxKind.BinaryExpression:
                expression = BindBinary(node, expected);
                break;

            case SyntaxKind.ConditionalExpression:
            {
                var condition = BindExpression((SyntaxNode)node.Children[0], PrimitiveType.Bool);
                var trueNode = (SyntaxNode)node.Children[2];
                var falseNode = (SyntaxNode)node.Children[4];
                var trueKnown = KnownType(trueNode);
                var falseKnown = KnownType(falseNode);
                var resultType = expected ?? (trueKnown is null ? falseKnown
                    : falseKnown is null || trueKnown == falseKnown ? trueKnown : null);
                var whenTrue = BindExpression(trueNode, resultType);
                var whenFalse = BindExpression(falseNode, resultType);
                if (resultType is null || resultType == PrimitiveType.Void
                    || whenTrue.Type != resultType || whenFalse.Type != resultType)
                {
                    Report("TRM3007", "Conditional arms require the same known, non-void result type or an expected result type.", node.Span);
                }
                expression = new BoundConditional(condition, whenTrue, whenFalse,
                    resultType ?? PrimitiveType.Int32, node.Span);
                break;
            }

            case SyntaxKind.InvocationExpression:
                expression = BindCall(node, expected);
                break;

            case SyntaxKind.ObjectCreationExpression:
            {
                var typeName = NameText((SyntaxNode)node.Children[1]);
                if (ResolveTypeName(typeName, out _) == DeclaredTypeResolution.Ambiguous)
                {
                    Report("TRM3005", $"Type '{typeName}' is ambiguous across declared and imported types.",
                        ((SyntaxNode)node.Children[1]).Span);
                    expression = ErrorExpression(node.Span);
                    break;
                }
                if (!TryResolveStruct(typeName, out var structure))
                {
                    Report("TRM3005", $"A class requires its conditional TryCreate factory; unknown struct '{typeName}'.", node.Span);
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
                if (KnownType((SyntaxNode)node.Children[0]) is PrimitiveType carrierType
                    && NativeFixedArrayContract.IsCarrier(carrierType))
                {
                    expression = BindNativeFixedArrayElement(node);
                    break;
                }
                if (KnownType((SyntaxNode)node.Children[0]) == PrimitiveType.TokenSpan)
                {
                    expression = new BoundSpanRead(
                        BindExpression((SyntaxNode)node.Children[0], PrimitiveType.TokenSpan),
                        BindExpression((SyntaxNode)node.Children[2], TokenSpanContract.IndexCarrier),
                        (PrimitiveType)(100 + TokenSpanContract.Find(_structs, TokenSpanContract.TokenName)), node.Span);
                    break;
                }
            {
                if (((SyntaxNode)node.Children[2]).Kind == SyntaxKind.SliceRangeExpression)
                {
                    Report("TRM3023", "A native buffer slice range must initialize a readonly lexical view local.",
                        node.Span);
                    expression = ErrorExpression(node.Span);
                    break;
                }
                if (TryFindBuffer((SyntaxNode)node.Children[0], out var buffer))
                {
                    var indexExpression = BindBufferIndex((SyntaxNode)node.Children[2], buffer,
                        out var isEndRelative);
                    expression = new BoundBufferRead(buffer, indexExpression,
                        _buffers[buffer].ElementType, node.Span)
                    {
                        IsEndRelativeOffset = isEndRelative,
                    };
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

        if (expected is PrimitiveType target && expression.Type != target
            && CanImplicitlyConvert(expression.Type, target))
        {
            if (ManagedClassTypes.IsOptional(target) && !ManagedClassTypes.IsOptional(expression.Type))
            {
                if (expression.Type != ManagedClassTypes.Required(target))
                {
                    expression = new BoundReadonlyProjection(expression,
                        ManagedClassTypes.Required(target), node.Span);
                }
                expression = new BoundOptionalLift(expression, target, node.Span);
            }
            else
            {
                expression = new BoundReadonlyProjection(expression, target, node.Span);
            }
        }
        if (expected is not null && expression.Type != expected)
        {
            Report("TRM3007", $"Expected {TypeName(expected.Value)}, got {TypeName(expression.Type)}.", node.Span);
        }

        return DiagnosticCount == diagnosticCount ? FoldScalarConstant(expression) : expression;
    }

    private static bool CanImplicitlyConvert(PrimitiveType source, PrimitiveType target)
    {
        if (source == target)
        {
            return true;
        }
        var readonlySource = ManagedClassTypes.IsWritable(source) ? ManagedClassTypes.Readonly(source) : source;
        return readonlySource == target
            || (ManagedClassTypes.IsOptional(target)
                && (source == ManagedClassTypes.Required(target)
                    || readonlySource == ManagedClassTypes.Required(target)));
    }

    private static bool IsNullExpression(SyntaxNode node)
    {
        while (node.Kind == SyntaxKind.ParenthesizedExpression)
        {
            node = (SyntaxNode)node.Children[1];
        }
        return node.Kind == SyntaxKind.LiteralExpression
            && ((SyntaxTokenElement)node.Children[0]).Token.Kind == TokenKind.NullKeyword;
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Unsupported operators are rejected by the default branch.")]
    private BoundExpression BindBinary(SyntaxNode node, PrimitiveType? expected)
    {
        var leftNode = (SyntaxNode)node.Children[0];
        var op = ((SyntaxTokenElement)node.Children[1]).Token;
        var rightNode = (SyntaxNode)node.Children[2];
        if (op.Kind == TokenKind.QuestionQuestion)
        {
            var leftContext = expected is PrimitiveType expectedOptional
                && ManagedClassTypes.IsOptional(expectedOptional) ? expectedOptional
                : KnownType(rightNode) is PrimitiveType knownOptional
                    && ManagedClassTypes.IsOptional(knownOptional) ? knownOptional : (PrimitiveType?)null;
            if (leftContext is null && IsNullExpression(leftNode)
                && (expected ?? KnownType(rightNode)) is PrimitiveType nullContext
                && ManagedClassTypes.IsManaged(nullContext))
            {
                leftContext = ManagedClassTypes.Optional(nullContext);
            }
            var optional = BindExpression(leftNode, leftContext);
            if (!ManagedClassTypes.IsOptional(optional.Type))
            {
                Report("TRM3007", "Coalescing requires a supported initialized optional operand.", leftNode.Span);
                _ = BindExpression(rightNode, expected);
                return ErrorExpression(node.Span);
            }
            var required = ManagedClassTypes.Required(optional.Type);
            var coalesceType = expected is PrimitiveType target && CanImplicitlyConvert(required, target)
                ? target
                : KnownType(rightNode) is PrimitiveType rhs && ManagedClassTypes.IsOptional(rhs)
                    && CanImplicitlyConvert(required, rhs) ? rhs
                : IsNullExpression(rightNode) ? optional.Type : required;
            var fallback = BindExpression(rightNode, coalesceType);
            return new BoundCoalesce(optional, fallback, coalesceType, node.Span);
        }
        var leftKnown = KnownType(leftNode);
        var rightKnown = KnownType(rightNode);
        if ((op.Kind is TokenKind.EqualsEquals or TokenKind.BangEquals)
            && (leftKnown == PrimitiveType.NativeRawPointer || rightKnown == PrimitiveType.NativeRawPointer)
            && (leftKnown is null or PrimitiveType.NativeRawPointer)
            && (rightKnown is null or PrimitiveType.NativeRawPointer))
        {
            var leftPointer = BindExpression(leftNode, PrimitiveType.NativeRawPointer);
            var rightPointer = BindExpression(rightNode, PrimitiveType.NativeRawPointer);
            return new BoundBinary(op.Kind, leftPointer, rightPointer, PrimitiveType.Bool, node.Span);
        }
        if (leftKnown is PrimitiveType concrete && IsStruct(concrete) && IsStaticBinaryOperator(op.Kind))
        {
            var leftValue = BindExpression(leftNode, concrete);
            var rightValue = BindExpression(rightNode, concrete);
            var comparisonResult = StaticOperatorResult(op.Kind, concrete);
            var name = _structs[(int)concrete - 100].Name + "."
                + OperatorName(new StaticOperatorKey(op.Kind, 2));
            if (!TryFindFunction(name, out var method) || _signatures[method].ReturnType != comparisonResult
                || _signatures[method].Parameters.Length != 2
                || _signatures[method].Parameters.Any(parameter => parameter.Type != concrete)
                || !_signatures[method].IsStatic || !_signatures[method].IsOperator)
            {
                Report("TRM3007", "No matching accessible static operator is declared.", op.Span);
                return new BoundBinary(op.Kind, leftValue, rightValue, comparisonResult, node.Span);
            }
            if (_signatures[method].IsPrivate && _currentStruct != _signatures[method].StructName)
            {
                Report("TRM3011", "Private operator is inaccessible.", op.Span);
            }
            return new BoundCall(method, [leftValue, rightValue], comparisonResult, node.Span) {
                IsUnsafeAuthorized = CheckUnsafeCaller(_signatures[method], node.Span),
            };
        }
        PrimitiveType? operandType;
        PrimitiveType resultType;

        switch (op.Kind)
        {
            case TokenKind.Plus:
            case TokenKind.Minus:
            case TokenKind.Star:
            case TokenKind.Slash:
            case TokenKind.Percent:
                operandType = leftKnown ?? rightKnown
                    ?? (expected is PrimitiveType expectedArithmeticType
                        && (PrimitiveTypeFacts.IsIntegerOrWide(expectedArithmeticType)
                            || PrimitiveTypeFacts.IsFloating(expectedArithmeticType)) ? expected : null);
                resultType = operandType ?? PrimitiveType.Int32;
                var floatingArithmetic = operandType is PrimitiveType floatingType
                    && PrimitiveTypeFacts.IsFloating(floatingType)
                    && op.Kind is TokenKind.Plus or TokenKind.Minus or TokenKind.Star
                        or TokenKind.Slash or TokenKind.Percent;
                if (!(operandType is PrimitiveType integerType && PrimitiveTypeFacts.IsIntegerOrWide(integerType))
                    && !floatingArithmetic)
                {
                    Report("TRM3007", "Arithmetic requires same-type integer or floating operands.", op.Span);
                }
                break;

            case TokenKind.ShiftLeft:
            case TokenKind.ShiftRight:
            case TokenKind.ShiftRightUnsigned:
                operandType = leftKnown ?? (expected is PrimitiveType expectedShiftType
                    && PrimitiveTypeFacts.IsIntegerOrWide(expectedShiftType) ? expected : null);
                resultType = operandType ?? PrimitiveType.Int32;
                if (operandType is not PrimitiveType shiftType || !PrimitiveTypeFacts.IsIntegerOrWide(shiftType))
                {
                    Report("TRM3007", "Shifts require a known integer left operand.", op.Span);
                }
                break;

            case TokenKind.Less:
            case TokenKind.LessEquals:
            case TokenKind.Greater:
            case TokenKind.GreaterEquals:
                operandType = leftKnown ?? rightKnown;
                resultType = PrimitiveType.Bool;
                if (!(operandType is PrimitiveType orderedType && (PrimitiveTypeFacts.IsIntegerOrWide(orderedType)
                    || PrimitiveTypeFacts.IsCodeUnitOrRune(orderedType)
                    || PrimitiveTypeFacts.IsFloating(orderedType))))
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
                if (operandType != PrimitiveType.Bool
                    && (operandType is not PrimitiveType bitwiseType
                        || !PrimitiveTypeFacts.IsIntegerOrWide(bitwiseType)))
                {
                    Report("TRM3007", "Bitwise operands must both be int32 or both be bool.", op.Span);
                }
                break;

            case TokenKind.EqualsEquals:
            case TokenKind.BangEquals:
                operandType = leftKnown ?? rightKnown;
                resultType = PrimitiveType.Bool;
                if (!(operandType == PrimitiveType.Bool
                    || (operandType is PrimitiveType equalityType
                        && (PrimitiveTypeFacts.IsIntegerOrWide(equalityType)
                            || PrimitiveTypeFacts.IsCodeUnitOrRune(equalityType)
                            || PrimitiveTypeFacts.IsFloating(equalityType)))))
                {
                    Report("TRM3007", "Equality requires operands of the same supported numeric, bool or char16 type.", op.Span);
                }
                break;

            default:
                Report("TRM3001", "Unsupported binary operator.", op.Span);
                operandType = null;
                resultType = PrimitiveType.Int32;
                break;
        }

        var left = BindExpression(leftNode, operandType);
        var shift = op.Kind is TokenKind.ShiftLeft or TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned;
        var rightContext = shift
            ? rightKnown is null ? PrimitiveType.Int32 : (PrimitiveType?)null
            : operandType;
        var right = BindExpression(rightNode, rightContext);
        if (operandType is null || left.Type != operandType
            || (shift ? !PrimitiveTypeFacts.IsIntegerOrWide(right.Type) : right.Type != operandType)
            || operandType == PrimitiveType.Void)
        {
            Report("TRM3007", "Binary operands must have the same supported type.", op.Span);
        }

        return new BoundBinary(op.Kind, left, right, resultType, node.Span) {
            OverflowBehavior = SourceOverflowBehavior(op.Kind, resultType),
        };
    }

    private BoundExpression BindCall(SyntaxNode node, PrimitiveType? expected)
    {
        var callee = (SyntaxNode)node.Children[0];
        if (KnownType(callee) is PrimitiveType functionPointerType
            && NativeFunctionPointerTypes.IsType(functionPointerType))
        {
            return BindNativeFunctionPointerCall(node, callee, functionPointerType);
        }
        var function = -1;
        SyntaxNode? receiver = null;
        if (callee.Kind == SyntaxKind.NameExpression)
        {
            var name = ((SyntaxTokenElement)callee.Children[0]).Token;
            if (TryFindVariable(name.Name, out _))
            {
                Report("TRM3011", $"'{name.Text}' is a variable, not a function.", name.Span);
            }
            else if (!TryFindFunction(name.Name, out function))
            {
                if (expected == PrimitiveType.NativeUtf16ZPointer && name.Name == "utf16z")
                {
                    return BindNativeUtf16ZLiteral(node, expected);
                }
                if (expected == PrimitiveType.NativeUtf8ZPointer && name.Name == "utf8z")
                {
                    return BindNativeUtf8ZLiteral(node, expected);
                }
                if (FindGenericTemplate(name.Name) is not null)
                {
                    function = InstantiateGeneric(callee, (SyntaxNode)node.Children[1], expected);
                }
                else if (TryConversionTarget(callee, out var target))
                {
                    return BindConversionCall(node, (SyntaxNode)node.Children[1], target);
                }
                else
                {
                    Report("TRM3011", $"Unknown function '{name.Text}'.", name.Span);
                }
            }
        }
        else if (callee.Kind == SyntaxKind.GenericNameExpression)
        {
            function = InstantiateGeneric(callee, (SyntaxNode)node.Children[1], expected);
        }
        else if (callee.Kind == SyntaxKind.MemberAccessExpression)
        {
            var left = (SyntaxNode)callee.Children[0];
            var member = ((SyntaxTokenElement)callee.Children[2]).Token.Name;
            var type = KnownType(left);
            if (member == "AsSpan" && type is PrimitiveType snapshot && IsStruct(snapshot)
                && _structs[(int)snapshot - 100].Name == TokenSpanContract.SnapshotName)
            {
                var arguments = (SyntaxNode)node.Children[1];
                if (arguments.Children.OfType<SyntaxNode>().Any()
                    || BindReceiverLoan(left, snapshot) is not BoundReceiverLoan loan
                    || !TokenSpanContract.ValidSnapshot(_structs[(int)snapshot - 100])
                    || !TokenSpanContract.ValidTokenLayout(_structs))
                {
                    Report("TRM3023", "AsSpan requires a named validated TokenSnapshot and the closed Copy Token layout.", node.Span);
                    return ErrorExpression(node.Span);
                }
                var source = -1;
                for (var index = 0; index < _buffers.Length; index++)
                {
                    if (_buffers[index].Name == "Source" && _buffers[index].ElementType == PrimitiveType.Char16
                        && !_buffers[index].IsWritable)
                    {
                        source = index;
                    }
                }
                if (source < 0)
                {
                    Report("TRM3023", "AsSpan requires the immutable scanner Source grant to validate record ranges.", node.Span);
                    return ErrorExpression(node.Span);
                }
                return new BoundSnapshotSpan(loan.Variable, source, node.Span);
            }
            if (type == PrimitiveType.TokenSpan)
            {
                if (member != "AdvanceOne" || _currentStruct is null
                    || Qualify(_currentStruct) != TokenSpanContract.CursorName
                    || ((SyntaxNode)node.Children[1]).Children.OfType<SyntaxNode>().Any())
                {
                    Report("TRM3023", "AdvanceOne is restricted to the compilation-local TokenCursor remaining-view update.", node.Span);
                    return ErrorExpression(node.Span);
                }
                return new BoundSpanAdvance(BindExpression(left, PrimitiveType.TokenSpan), node.Span);
            }
            if (type is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView)
            {
                if (member != "Read"
                    || TryGetSingleSyntaxArgument((SyntaxNode)node.Children[1]) is not {
                        Children: [SyntaxNode index],
                    })
                {
                    Report("TRM3011", "A native buffer view supports Read(index) with one positional int32 argument.",
                        node.Span);
                    return ErrorExpression(node.Span);
                }
                return new BoundViewRead(BindViewVariable(left), BindExpression(index, PrimitiveType.Int32), node.Span);
            }
            if (type == PrimitiveType.OwnedInt32Buffer && member is "Slice" or "SliceMutable")
            {
                Report("TRM3023", "A native buffer slice can only initialize a matching lexical view local.",
                    node.Span);
                return ErrorExpression(node.Span);
            }
            if (type == PrimitiveType.OwnedInt32Buffer)
            {
                receiver = left;
                _ = TryFindFunction(NativeBufferIntrinsics.TypeName + "." + member, out function);
            }
            else if (ManagedClassTypes.Readonly(type ?? PrimitiveType.Void) == PrimitiveType.ManagedNode)
            {
                receiver = left;
                _ = TryFindFunction(NativeManagedNodeIntrinsics.TypeName + "." + member, out function);
            }
            else if (type is PrimitiveType classType && IsClass(classType))
            {
                receiver = left;
                _ = TryFindFunction(_classes[ManagedClassTypes.ClassIndex(classType)].Name + "." + member,
                    out function);
            }
            else if (type is PrimitiveType structType && IsStruct(structType))
            {
                receiver = left;
                _ = TryFindFunction(_structs[(int)structType - 100].Name + "." + member, out function);
            }
            else if (type is PrimitiveType primitiveType
                && TryPrimitiveType(TypeName(primitiveType), out var intrinsicType)
                && intrinsicType == primitiveType)
            {
                receiver = left;
                _ = TryFindFunction(TypeName(primitiveType) + "." + member, out function);
            }
            else
            {
                if (ResolveStaticMember(left, member, out var staticMember) == DeclaredTypeResolution.Ambiguous)
                {
                    Report("TRM3011", $"Type '{NameText(left)}' is ambiguous across declared and imported types.",
                        left.Span);
                    return ErrorExpression(node.Span);
                }
                _ = TryFindFunction(staticMember, out function);
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

    private BoundExpression BindNativeFunctionPointerCall(SyntaxNode node, SyntaxNode callee,
        PrimitiveType functionPointerType)
    {
        var typeIndex = NativeFunctionPointerTypes.Index(functionPointerType);
        if ((uint)typeIndex >= (uint)_nativeFunctionPointerSignatures.Count)
        {
            Report("TRM3005", "The native function pointer type has no bound signature.", callee.Span);
            return ErrorExpression(node.Span);
        }

        var signature = _nativeFunctionPointerSignatures[typeIndex];
        var target = BindExpression(callee, functionPointerType);
        var argumentList = (SyntaxNode)node.Children[1];
        var argumentNodes = argumentList.Children.OfType<SyntaxNode>().ToArray();
        if (HasNamedArguments(argumentList))
        {
            Report("TRM3011", "Native function pointer calls do not support named arguments.", argumentList.Span);
        }
        if (argumentNodes.Length != signature.ParameterTypes.Length)
        {
            Report("TRM3011", "Native function pointer argument count does not match its signature.",
                argumentList.Span);
        }
        if (_unsafeDepth == 0)
        {
            Report("TRM3028", "Calling a native function pointer requires an explicit unsafe context.", node.Span);
        }

        var boundArguments = ImmutableArray.CreateBuilder<BoundExpression>(argumentNodes.Length);
        for (var index = 0; index < argumentNodes.Length; index++)
        {
            var argument = argumentNodes[index];
            if (argument.Children.Any(static child => child is SyntaxTokenElement {
                Token.Kind: TokenKind.OutKeyword or TokenKind.VarKeyword
            } or SyntaxTokenElement { Token.Text: "init" }))
            {
                Report("TRM3011", "Native function pointer parameters do not support output argument modes.",
                    argument.Span);
            }
            var value = argument.Children.OfType<SyntaxNode>().LastOrDefault();
            if (value is null)
            {
                Report("TRM3011", "Native function pointer calls require value arguments.", argument.Span);
                boundArguments.Add(ErrorExpression(argument.Span));
                continue;
            }
            var parameterType = index < signature.ParameterTypes.Length
                ? signature.ParameterTypes[index] : (PrimitiveType?)null;
            boundArguments.Add(BindExpression(value, parameterType));
        }

        return new BoundIndirectCall(target, boundArguments.MoveToImmutable(),
            signature.ReturnType, node.Span) {
            IsUnsafeAuthorized = _unsafeDepth != 0,
        };
    }

    private BoundConstant BindNativeUtf16ZLiteral(SyntaxNode node, PrimitiveType? expected)
    {
        if (expected != PrimitiveType.NativeUtf16ZPointer
            || TryGetSingleSyntaxArgument((SyntaxNode)node.Children[1]) is not {
                Kind: SyntaxKind.Argument,
                Children: [SyntaxNode literal],
            }
            || literal.Kind != SyntaxKind.LiteralExpression
            || literal.Children is not [SyntaxTokenElement { Token.Kind: TokenKind.StringLiteral } token]
            || !StringLiteralDecoder.TryDecode(token.Token.Text.AsSpan(), out var value))
        {
            Report("TRM3007", "utf16z requires one string literal at a matching readonly, nonescaping native parameter.",
                node.Span);
            return ErrorExpression(node.Span);
        }
        if (!NativeUtf16ZContract.IsValidLiteral(value))
        {
            Report("TRM3013", "utf16z rejects embedded NUL and literals exceeding the native text-data limit.",
                node.Span);
            return ErrorExpression(node.Span);
        }
        if (!_nativeUtf16ZLiteralIndexes.TryGetValue(value, out var literalIndex))
        {
            var storageBytes = NativeUtf16ZContract.StorageBytes(value);
            if (_nativeStringZLiteralBytes + storageBytes > NativeStringZPoolContract.MaximumStorageBytes)
            {
                Report("TRM3013", "utf16z exceeds the aggregate native text-data limit.", node.Span);
                return ErrorExpression(node.Span);
            }
            literalIndex = _nativeUtf16ZLiterals.Count;
            _nativeUtf16ZLiterals.Add(value);
            _nativeUtf16ZLiteralIndexes.Add(value, literalIndex);
            _nativeStringZLiteralBytes += storageBytes;
        }
        return new BoundConstant(literalIndex, PrimitiveType.NativeUtf16ZPointer, node.Span);
    }

    private BoundConstant BindNativeUtf8ZLiteral(SyntaxNode node, PrimitiveType? expected)
    {
        if (expected != PrimitiveType.NativeUtf8ZPointer
            || TryGetSingleSyntaxArgument((SyntaxNode)node.Children[1]) is not {
                Kind: SyntaxKind.Argument,
                Children: [SyntaxNode literal],
            }
            || literal.Kind != SyntaxKind.LiteralExpression
            || literal.Children is not [SyntaxTokenElement { Token.Kind: TokenKind.StringLiteral } token]
            || !StringLiteralDecoder.TryDecode(token.Token.Text.AsSpan(), out var value))
        {
            Report("TRM3007", "utf8z requires one string literal at a matching readonly, nonescaping native parameter.",
                node.Span);
            return ErrorExpression(node.Span);
        }
        if (!NativeUtf8ZContract.IsValidLiteral(value))
        {
            Report("TRM3013", "utf8z accepts only NUL-free ASCII literals within the native text-data limit.",
                node.Span);
            return ErrorExpression(node.Span);
        }
        if (!_nativeUtf8ZLiteralIndexes.TryGetValue(value, out var literalIndex))
        {
            var storageBytes = NativeUtf8ZContract.StorageBytes(value);
            if (_nativeStringZLiteralBytes + storageBytes > NativeStringZPoolContract.MaximumStorageBytes)
            {
                Report("TRM3013", "utf8z exceeds the aggregate native text-data limit.", node.Span);
                return ErrorExpression(node.Span);
            }
            literalIndex = _nativeUtf8ZLiterals.Count;
            _nativeUtf8ZLiterals.Add(value);
            _nativeUtf8ZLiteralIndexes.Add(value, literalIndex);
            _nativeStringZLiteralBytes += storageBytes;
        }
        return new BoundConstant(literalIndex, PrimitiveType.NativeUtf8ZPointer, node.Span);
    }

    private bool TryConversionTarget(SyntaxNode callee, out PrimitiveType target)
    {
        target = PrimitiveType.Int32;
        if (callee.Kind != SyntaxKind.NameExpression)
        {
            return false;
        }
        var name = ((SyntaxTokenElement)callee.Children[0]).Token.Name;
        if (TryFindVariable(name, out _) || TryFindFunction(name, out _))
        {
            return false;
        }
        target = name switch {
            "char8" => PrimitiveType.Char8,
            "char32" => PrimitiveType.Char32,
            "rune" => PrimitiveType.Rune,
            "int8" => PrimitiveType.Int8,
            "int16" => PrimitiveType.Int16,
            "int32" => PrimitiveType.Int32,
            "uint8" => PrimitiveType.UInt8,
            "uint16" => PrimitiveType.UInt16,
            "uint32" => PrimitiveType.UInt32,
            "int64" => PrimitiveType.Int64,
            "uint64" => PrimitiveType.UInt64,
            "int128" => PrimitiveType.Int128,
            "uint128" => PrimitiveType.UInt128,
            "char16" => PrimitiveType.Char16,
            "float16" => PrimitiveType.Float16,
            "bfloat16" => PrimitiveType.BFloat16,
            "float32" => PrimitiveType.Float32,
            "float64" => PrimitiveType.Float64,
            "float128" => PrimitiveType.Float128,
            "decimal32" => PrimitiveType.Decimal32,
            "decimal64" => PrimitiveType.Decimal64,
            "decimal128" => PrimitiveType.Decimal128,
            _ => PrimitiveType.Int32,
        };
        return name is "char8" or "char16" or "char32" or "rune"
            or "int8" or "int16" or "int32" or "int64" or "int128"
            or "uint8" or "uint16" or "uint32" or "uint64" or "uint128"
            or "float16" or "bfloat16" or "float32" or "float64" or "float128"
            or "decimal32" or "decimal64" or "decimal128";
    }

    private BoundExpression BindConversionCall(SyntaxNode node, SyntaxNode arguments, PrimitiveType target)
    {
        if (TryGetSingleSyntaxArgument(arguments) is not {
            Kind: SyntaxKind.Argument,
            Children: [SyntaxNode operandNode],
        })
        {
            Report("TRM3011", "A checked conversion requires exactly one positional value argument.", arguments.Span);
            return ErrorExpression(node.Span);
        }
        var operand = BindExpression(operandNode, KnownType(operandNode) is null ? target : null);
        if (operand.Type == target)
        {
            return operand;
        }
        if (target is PrimitiveType.Float32 or PrimitiveType.Float64)
        {
            if (PrimitiveTypeFacts.IsInteger(operand.Type)
                || operand.Type is PrimitiveType.Char16 or PrimitiveType.Float32 or PrimitiveType.Float64)
            {
                return new BoundCast(operand, target, node.Span);
            }
            Report("TRM3007", "This typed floating-point conversion is not supported.", operand.Span);
            return ErrorExpression(node.Span);
        }
        if (operand.Type is PrimitiveType.Float32 or PrimitiveType.Float64)
        {
            if (target is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64)
            {
                return new BoundCast(operand, target, node.Span);
            }
            Report("TRM3007", "This typed floating-point conversion is not supported.", operand.Span);
            return ErrorExpression(node.Span);
        }
        if (target == PrimitiveType.Char16 && PrimitiveTypeFacts.IsInteger(operand.Type))
        {
            return new BoundCast(operand, target, node.Span);
        }
        if (operand.Type == PrimitiveType.Char16
            && (PrimitiveTypeFacts.IsInteger(target)
                || (target is PrimitiveType.Int128 or PrimitiveType.UInt128)))
        {
            return new BoundCast(operand, target, node.Span);
        }
        if (PrimitiveTypeFacts.IsInteger(operand.Type)
            && target is PrimitiveType.Int128 or PrimitiveType.UInt128)
        {
            return new BoundCast(operand, target, node.Span);
        }
        if (!PrimitiveTypeFacts.IsInteger(operand.Type) || !PrimitiveTypeFacts.IsInteger(target))
        {
            Report("TRM3007", "A checked conversion requires a numeric operand.", operand.Span);
            return ErrorExpression(node.Span);
        }
        return new BoundCast(operand, target, node.Span);
    }

    private BoundCall BindArguments(SyntaxNode node, SyntaxNode arguments, int function, SyntaxNode? receiver)
    {
        var signature = function < 0 ? null : _signatures[function];
        var hasNamedArguments = HasNamedArguments(arguments);
        var namedArgumentsSupported = hasNamedArguments && signature is not null
            && (signature.External is null || signature.External.Intrinsic == BoundIntrinsic.None);
        var validNamedBinding = namedArgumentsSupported;
        if (hasNamedArguments && !namedArgumentsSupported)
        {
            Report("TRM3011", "Named arguments are supported only for uniquely resolved non-intrinsic calls.", arguments.Span);
        }
        var ownerName = signature?.StructName is null ? null
            : signature.Name[..signature.Name.LastIndexOf('.', StringComparison.Ordinal)];
        if (signature is not null && signature.IsPrivate
            && ownerName != (_currentStruct is null ? null : Qualify(_currentStruct)))
        {
            Report("TRM3011", $"'{signature.Name}' is private.", node.Span);
        }
        if (signature is not null && receiver is not null && signature.IsStatic)
        {
            Report("TRM3011", "A static method has no receiver.", node.Span);
        }
        var bound = ImmutableArray.CreateBuilder<BoundExpression>();
        ImmutableArray<int>.Builder? argumentParameterIndices = hasNamedArguments
            ? ImmutableArray.CreateBuilder<int>() : null;
        ImmutableArray<int>.Builder? implicitReferenceTemporaries = null;
        var receiverCount = 0;
        if (receiver is not null && signature is not null)
        {
            var receiverType = signature.External?.Variables[0].Type
                ?? (signature.IsMutable && IsClass(ResolveDeclaredType(ownerName!))
                    ? ManagedClassTypes.Writable(ResolveDeclaredType(ownerName!))
                    : ResolveDeclaredType(ownerName!));
            bound.Add(signature.External?.Intrinsic is BoundIntrinsic.BufferSet or BoundIntrinsic.BufferTryAppend or BoundIntrinsic.BufferClear
                ? BindMutableReceiverLoan(receiver, PrimitiveType.OwnedInt32Buffer, receiver.Span,
                    allowBufferField: true)
                : signature.IsMutable && IsStruct(receiverType)
                    ? BindMutableReceiverLoan(receiver, receiverType, receiver.Span)
                : IsUnique(receiverType) && !signature.IsCleanup
                    ? BindReceiverLoan(receiver, receiverType)
                    : BindExpression(receiver, receiverType));
            receiverCount = 1;
            argumentParameterIndices?.Add(0);
        }
        else if (signature is { StructName: not null, IsStatic: false, IsConstructor: false })
        {
            if (_currentStruct == signature.StructName && _variables.Count > 0 && _variables[0].Name == "this")
            {
                var receiverType = signature.External?.Variables[0].Type
                    ?? (signature.IsMutable && IsClass(ResolveDeclaredType(ownerName!))
                        ? ManagedClassTypes.Writable(ResolveDeclaredType(ownerName!))
                        : ResolveDeclaredType(ownerName!));
                bound.Add(signature.IsMutable && IsStruct(receiverType)
                    ? _variables[0].IsMutable
                        ? new BoundMutableReceiverLoan(0, receiverType, node.Span)
                        : ErrorMutableReceiver(node.Span)
                    : IsUnique(receiverType) && !signature.IsCleanup
                    ? new BoundReceiverLoan(0, receiverType, node.Span)
                    : BindImplicitReceiver(receiverType, node.Span));
                receiverCount = 1;
                argumentParameterIndices?.Add(0);
            }
            else
            {
                Report("TRM3011", "Instance methods require a receiver.", node.Span);
            }
        }
        var assignedParameters = signature is not null && namedArgumentsSupported
            ? new bool[signature.Parameters.Length] : null;
        var nextPositionalParameter = 0;
        var hasOutOfPositionNamedArgument = false;
        foreach (var child in arguments.Children)
        {
            if (child is not SyntaxNode argument)
            {
                continue;
            }

            var sourcePosition = bound.Count - receiverCount;
            var position = sourcePosition;
            var argumentName = GetArgumentName(argument);
            if (argumentName is not null)
            {
                if (!namedArgumentsSupported || signature is null)
                {
                    validNamedBinding = false;
                    position = sourcePosition;
                }
                else
                {
                    position = -1;
                    for (var index = 0; index < signature.Parameters.Length; index++)
                    {
                        if (signature.Parameters[index].Name == argumentName.Token.Name)
                        {
                            position = index;
                            break;
                        }
                    }
                    if (position < 0)
                    {
                        Report("TRM3011", $"'{argumentName.Token.Name}' is not a parameter of '{signature.Name}'.", argument.Span);
                        validNamedBinding = false;
                        position = sourcePosition;
                    }
                    else if (assignedParameters is not null && assignedParameters[position])
                    {
                        Report("TRM3011", $"Parameter '{argumentName.Token.Name}' is supplied more than once.", argument.Span);
                        validNamedBinding = false;
                    }
                    else if (position == nextPositionalParameter)
                    {
                        nextPositionalParameter++;
                    }
                    else
                    {
                        hasOutOfPositionNamedArgument = true;
                    }
                }
            }
            else
            {
                if (hasOutOfPositionNamedArgument)
                {
                    Report("TRM3011", "A positional argument cannot follow an out-of-position named argument.", argument.Span);
                    validNamedBinding = false;
                }
                position = nextPositionalParameter++;
            }
            var expectedParameter = signature is not null && position < signature.Parameters.Length
                ? signature.Parameters[position] : null;
            if (expectedParameter?.IsImplicitRef == true && implicitReferenceTemporaries is null)
            {
                implicitReferenceTemporaries = ImmutableArray.CreateBuilder<int>();
                for (var previous = 0; previous < bound.Count; previous++)
                {
                    implicitReferenceTemporaries.Add(-1);
                }
            }
            implicitReferenceTemporaries?.Add(-1);
            if (expectedParameter is not null && position >= 0 && assignedParameters is not null)
            {
                if (assignedParameters[position] && argumentName is null)
                {
                    Report("TRM3011", $"Parameter '{expectedParameter.Name}' is supplied more than once.", argument.Span);
                    validNamedBinding = false;
                }
                assignedParameters[position] = true;
            }
            var passingIndex = argumentName is null ? 0 : 2;
            if (argument.Children[passingIndex] is SyntaxTokenElement passing
                && (passing.Token.Kind == TokenKind.OutKeyword
                    || (passing.Token.Kind == TokenKind.Identifier && passing.Token.Text == "init")))
            {
                var isInit = passing.Token.Text == "init";
                if (expectedParameter is null || !expectedParameter.IsOut
                    || expectedParameter.IsInit != isInit)
                {
                    Report("TRM3011", "Output argument mode does not match the parameter.", argument.Span);
                    bound.Add(ErrorExpression(argument.Span));
                    continue;
                }
                var outputTargetIndex = passingIndex + 1;
                var declares = argument.Children.Length == outputTargetIndex + 2
                    && argument.Children[outputTargetIndex] is SyntaxTokenElement variableKeyword
                    && variableKeyword.Token.Kind == TokenKind.VarKeyword;
                var outputTarget = !declares && argument.Children.Length == outputTargetIndex + 1
                    ? argument.Children[outputTargetIndex] as SyntaxNode : null;
                var slot = -1;
                if (declares)
                {
                    var outName = ((SyntaxTokenElement)argument.Children[outputTargetIndex + 1]).Token;
                    slot = AddVariable(outName.Name, expectedParameter.Type, false, outName.Span);
                }
                else if (isInit && outputTarget is not null
                    && TryFindInitReferenceProjection(outputTarget, out var forwarded))
                {
                    slot = forwarded;
                }
                else if (outputTarget?.Kind == SyntaxKind.NameExpression
                    && TryFindVariable(((SyntaxTokenElement)outputTarget.Children[0]).Token,
                        out var existing))
                {
                    slot = existing;
                }
                if (slot < 0)
                {
                    Report("TRM3011", "Output argument must be a local variable or an init ref destination projection.",
                        argument.Span);
                    bound.Add(ErrorExpression(argument.Span));
                    continue;
                }
                if (slot >= 0 && _unresolvedVacantLocals.Contains(slot) && isInit)
                {
                    _variables[slot] = _variables[slot] with { Type = expectedParameter.Type };
                    _ = _unresolvedVacantLocals.Remove(slot);
                }
                if (slot < 0 || _variables[slot].Type != expectedParameter.Type
                    || (!declares && ((!isInit && !_variables[slot].IsMutable)
                        || (_hasReceiver && slot == 0))))
                {
                    Report("TRM3011", "Output target must be a matching local with a valid passing mode.", argument.Span);
                    bound.Add(ErrorExpression(argument.Span));
                    continue;
                }
                bound.Add(new BoundOutArgument(slot, declares, expectedParameter.Type, argument.Span) {
                    IsInit = isInit,
                });
                argumentParameterIndices?.Add(position + receiverCount);
                continue;
            }
            if (expectedParameter?.IsOut == true
                && !(signature?.External?.DirectNativeImport is DirectNativeImport directOutputImport
                    && position >= 0
                    && directOutputImport.Outputs.Any(output => output.ParameterIndex == position))
                && !(signature?.External is BoundFunction outputVTableFunction
                    && position >= 0
                    && NativeVTableCallContract.OutputForParameter(
                        outputVTableFunction, position) is not null))
            {
                Report("TRM3011", "An output parameter requires the matching out or init argument.", argument.Span);
            }
            var valueIndex = argumentName is null ? 0 : 2;
            if (argument.Children.Length != valueIndex + 1
                || argument.Children[valueIndex] is not SyntaxNode value)
            {
                Report("TRM3011", "A by-value argument requires one expression.", argument.Span);
                // The malformed argument cannot become a valid program; retain no executable value.
                bound.Add(ErrorExpression(argument.Span));
                argumentParameterIndices?.Add(position + receiverCount);
                continue;
            }

            var expected = expectedParameter?.Type;
            if (expectedParameter?.IsImplicitRef == true)
            {
                var reference = BindExpression(value, expected);
                if (!IsSupportedImplicitReferenceType(reference.Type))
                {
                    Report("TRM3011",
                        "The provisional implicit ref implementation requires a supported scalar or non-unique struct place or value.",
                        argument.Span);
                    bound.Add(ErrorExpression(argument.Span));
                }
                else if (reference is BoundRead read)
                {
                    if (_constLocals.Contains(read.Variable))
                    {
                        Report("TRM3011",
                            "The provisional implicit ref implementation cannot borrow compile-time constant storage.",
                            argument.Span);
                        bound.Add(ErrorExpression(argument.Span));
                    }
                    else
                    {
                        bound.Add(reference);
                    }
                }
                else if (reference is BoundFieldRead field
                    && IsSupportedImplicitReferenceFieldPlace(field))
                {
                    bound.Add(reference);
                }
                else if (reference is BoundBufferRead buffer
                    && (uint)buffer.Buffer < (uint)_buffers.Length
                    && _buffers[buffer.Buffer].ElementType == buffer.Type)
                {
                    bound.Add(reference);
                }
                else if (reference is BoundViewRead view
                    && (uint)view.Variable < (uint)_variables.Count
                    && _variables[view.Variable].Type is PrimitiveType.ReadonlyInt32BufferView
                        or PrimitiveType.MutableInt32BufferView)
                {
                    bound.Add(reference);
                }
                else if (reference is BoundFieldRead or BoundBufferRead or BoundViewRead)
                {
                    Report("TRM3011",
                        "The provisional implicit ref implementation requires a direct local or parameter place, a supported struct-field place, a host-buffer or lexical-view element place, or a value expression.",
                        argument.Span);
                    bound.Add(ErrorExpression(argument.Span));
                }
                else
                {
                    var temporary = AddImplicitReferenceTemporary(reference.Type, argument.Span);
                    bound.Add(reference);
                    if (implicitReferenceTemporaries is null)
                    {
                        throw new InvalidOperationException("An implicit-reference argument requires call metadata.");
                    }
                    implicitReferenceTemporaries[^1] = temporary;
                }
            }
            else
            {
                if (signature?.External?.DirectNativeImport is DirectNativeImport directImport
                    && expectedParameter is not null && position >= 0
                    && directImport.Outputs.FirstOrDefault(output =>
                        output.ParameterIndex == position) is DirectNativeOutput nativeOutput)
                {
                    bound.Add(BindDirectNativeOutputReservation(value, nativeOutput, argument.Span));
                }
                else if (signature?.External is BoundFunction nativeVTableOutputFunction
                    && nativeVTableOutputFunction.NativeVTableMethod is not null
                    && position >= 0
                    && NativeVTableCallContract.OutputForParameter(
                        nativeVTableOutputFunction, position) is DirectNativeOutput vtableOutput)
                {
                    bound.Add(BindDirectNativeOutputReservation(value, vtableOutput, argument.Span));
                }
                else if (signature?.External is BoundFunction directParameterFunction
                    && directParameterFunction.DirectNativeImport is not null
                    && expectedParameter?.Type == PrimitiveType.NativeRawPointer && position >= 0
                    && DirectNativeImportContract.ParameterForParameter(
                        directParameterFunction, position) is DirectNativeParameter directInputParameter)
                {
                    bound.Add(BindDirectNativeParameterAddress(value, directInputParameter, argument.Span));
                }
                else if (signature?.External is BoundFunction nativeVTableFunction
                    && nativeVTableFunction.NativeVTableMethod is not null
                    && expectedParameter?.Type == PrimitiveType.NativeRawPointer && position >= 0
                    && NativeVTableCallContract.ParameterForParameter(
                        nativeVTableFunction, position) is DirectNativeParameter vtableInputParameter)
                {
                    bound.Add(BindDirectNativeParameterAddress(value, vtableInputParameter, argument.Span));
                }
                else
                {
                    bound.Add(BindExpression(value, expected));
                }
            }
            argumentParameterIndices?.Add(position + receiverCount);
        }

        if (signature is not null)
        {
            _ = CheckUnsafeCaller(signature, node.Span);
            if (signature.IsCleanup)
            {
                Report("TRM3023", "Cleanup methods are called only by lexical owner cleanup.", node.Span);
            }
            var supplied = bound.Count - receiverCount;
            if (signature.Parameters.Length != supplied)
            {
                Report("TRM3011", $"Function '{signature.Name}' expects {signature.Parameters.Length} arguments, got {supplied}.", arguments.Span);
            }
            if (hasNamedArguments && namedArgumentsSupported && signature is not null
                && assignedParameters is { } suppliedParameters)
            {
                for (var index = 0; index < suppliedParameters.Length; index++)
                {
                    if (!suppliedParameters[index])
                    {
                        Report("TRM3011", $"Required parameter '{signature.Parameters[index].Name}' was not supplied.", arguments.Span);
                        validNamedBinding = false;
                    }
                }
            }
        }

        return new BoundCall(Math.Max(0, function), bound.ToImmutable(),
            signature?.ReturnType ?? PrimitiveType.Int32, node.Span) {
            ArgumentParameterIndices = validNamedBinding && argumentParameterIndices is { } parameterIndices
                ? parameterIndices.ToImmutable() : default,
            ArgumentImplicitReferenceTemporaries = implicitReferenceTemporaries?.ToImmutable() ?? default,
            IsUnsafeAuthorized = signature is { RequiresUnsafeCaller: true } && _unsafeDepth != 0,
        };
    }

    private static bool HasArgumentName(SyntaxNode argument) =>
        argument.Children.Length >= 3
        && argument.Children[0] is SyntaxTokenElement { Token.Kind: TokenKind.Identifier }
        && argument.Children[1] is SyntaxTokenElement { Token.Kind: TokenKind.Colon };

    private static bool HasNamedArguments(SyntaxNode arguments)
    {
        foreach (var child in arguments.Children)
        {
            if (child is SyntaxNode argument && HasArgumentName(argument))
            {
                return true;
            }
        }
        return false;
    }

    private static SyntaxTokenElement? GetArgumentName(SyntaxNode argument) =>
        HasArgumentName(argument) ? (SyntaxTokenElement)argument.Children[0] : null;

    private bool CheckUnsafeCaller(Signature signature, TextSpan span)
    {
        if (signature.RequiresUnsafeCaller && _unsafeDepth == 0)
        {
            Report("TRM3028", $"Calling '{signature.Name}' requires an explicit unsafe context.", span);
            return false;
        }
        return signature.RequiresUnsafeCaller;
    }

    private bool IsStruct(PrimitiveType type) => (int)type >= 100
        && (int)type < ManagedClassTypes.First && (int)type - 100 < _structs.Count;

    private bool IsGenericStructSpecialization(PrimitiveType type) =>
        _genericStructSpecializationTypes.Contains(type);

    private bool ContainsGenericStructSpecialization(PrimitiveType type)
    {
        var pending = new Stack<PrimitiveType>();
        var visited = new HashSet<PrimitiveType>();
        pending.Push(type);
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }
            if (IsGenericStructSpecialization(current))
            {
                return true;
            }
            if (IsStruct(current))
            {
                foreach (var field in _structs[(int)current - 100].Fields)
                {
                    pending.Push(field.Type);
                }
            }
            if (ManagedClassTypes.IsManaged(current) && (int)current >= ManagedClassTypes.First)
            {
                var classIndex = ManagedClassTypes.ClassIndex(current);
                if (classIndex < _classes.Count)
                {
                    foreach (var field in _classes[classIndex].Fields)
                    {
                        pending.Push(field.Type);
                    }
                }
            }
            if (NativePointerTypes.IsType(current))
            {
                pending.Push(_nativePointerTypes[NativePointerTypes.Index(current)].PointeeType);
            }
            if (NativeFunctionPointerTypes.IsType(current))
            {
                var signature = _nativeFunctionPointerSignatures[NativeFunctionPointerTypes.Index(current)];
                pending.Push(signature.ReturnType);
                foreach (var parameter in signature.ParameterTypes)
                {
                    pending.Push(parameter);
                }
            }
        }
        return false;
    }

    private bool TryFindBuffer(SyntaxNode node, out int buffer)
    {
        if (node.Kind == SyntaxKind.MemberAccessExpression
            && ((SyntaxNode)node.Children[0]).Kind == SyntaxKind.NameExpression
            && ((SyntaxTokenElement)((SyntaxNode)node.Children[0]).Children[0]).Token.Text == "Host")
        {
            var name = ((SyntaxTokenElement)node.Children[2]).Token.Name;
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
        if (IsStruct(type) || IsClass(type))
        {
            var fields = IsClass(type) ? _classes[ManagedClassTypes.ClassIndex(type)].Fields
                : _structs[(int)type - 100].Fields;
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

    private bool TryFindGetter(PrimitiveType type, string name, out int getter)
    {
        var owner = IsClass(type) ? _classes[ManagedClassTypes.ClassIndex(type)].Name
            : IsStruct(type) ? _structs[(int)type - 100].Name
            : TryPrimitiveType(TypeName(type), out var primitive) && primitive == type
                ? TypeName(type) : null;
        getter = -1;
        return owner is not null && TryFindFunctionExact(owner + ".#get:" + name, out getter)
            && !_signatures[getter].IsStatic && _signatures[getter].PropertyName == name;
    }

    private bool TryFindStaticGetter(PrimitiveType type, string name, out int getter)
    {
        var owner = IsStruct(type) ? _structs[(int)type - 100].Name : null;
        getter = -1;
        return owner is not null && TryFindFunctionExact(owner + ".#get:" + name, out getter)
            && _signatures[getter].IsStatic && _signatures[getter].PropertyName == name;
    }

    private bool TryFindStaticGetter(string staticMember, string propertyName, out int getter)
    {
        var separator = staticMember.LastIndexOf('.');
        getter = -1;
        return separator >= 0 && TryFindFunctionExact(
            staticMember[..(separator + 1)] + "#get:" + propertyName, out getter)
            && _signatures[getter].IsStatic && _signatures[getter].PropertyName == propertyName;
    }

    private static bool IsScalarPrimitiveType(PrimitiveType type) =>
        type is PrimitiveType.Bool or PrimitiveType.Char8 or PrimitiveType.Char16 or PrimitiveType.Char32
            or PrimitiveType.Rune or PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32
            or PrimitiveType.Int64 or PrimitiveType.UInt8 or PrimitiveType.UInt16 or PrimitiveType.UInt32
            or PrimitiveType.UInt64 or PrimitiveType.Float32 or PrimitiveType.Float64
            || NativeFunctionPointerTypes.IsType(type);

    private bool IsSupportedImplicitReferenceType(PrimitiveType type) =>
        IsScalarPrimitiveType(type) || (IsStruct(type) && !IsUnique(type));

    private bool IsSupportedImplicitReferenceFieldPlace(BoundFieldRead field)
    {
        if (!IsSupportedImplicitReferenceType(field.Type)
            || !IsSupportedImplicitReferenceType(field.Receiver.Type)
            || !IsStruct(field.Receiver.Type))
        {
            return false;
        }
        return field.Receiver switch
        {
            BoundRead read => !_constLocals.Contains(read.Variable),
            BoundFieldRead parent => IsSupportedImplicitReferenceFieldPlace(parent),
            _ => false,
        };
    }

    private bool IsSupportedStaticGetterType(PrimitiveType type) =>
        IsScalarPrimitiveType(type) || (IsStruct(type) && !IsUnique(type));

    private BoundStructField GetField(PrimitiveType type, int field) => IsClass(type)
        ? _classes[ManagedClassTypes.ClassIndex(type)].Fields[field]
        : _structs[(int)type - 100].Fields[field];

    private BoundExpression BindImplicitReceiver(PrimitiveType type, TextSpan span)
    {
        var receiver = new BoundRead(0, _variables[0].Type, span);
        return ManagedClassTypes.IsWritable(receiver.Type) && ManagedClassTypes.Readonly(receiver.Type) == type
            ? new BoundReadonlyProjection(receiver, type, span) : receiver;
    }

    private BoundConstant ErrorMutableReceiver(TextSpan span)
    {
        Report("TRM3023", "Mutation requires a mutable receiver place.", span);
        return ErrorExpression(span);
    }

    private bool CheckFieldAccess(PrimitiveType type, int field, TextSpan span)
    {
        var declaration = GetField(type, field);
        if (declaration.IsPrivate
            && (IsClass(type) ? _classes[ManagedClassTypes.ClassIndex(type)].Name
                : _structs[(int)type - 100].Name) != (string.IsNullOrEmpty(_namespace)
                ? _currentStruct : _namespace + "." + _currentStruct))
        {
            Report("TRM3011", "Private field cannot be accessed outside its declaring struct.", span);
        }
        if (declaration.RequiresUnsafeCaller && _unsafeDepth == 0)
        {
            Report("TRM3028", "Field access requires an explicit unsafe context.", span);
        }
        return declaration.RequiresUnsafeCaller && _unsafeDepth != 0;
    }

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Unsupported literal contexts are diagnosed before code emission.")]
    private BoundConstant BindLiteral(SyntaxToken token, PrimitiveType? expected)
    {
        if (token.Kind is TokenKind.TrueKeyword or TokenKind.FalseKeyword)
        {
            return new BoundConstant(token.Kind == TokenKind.TrueKeyword ? 1 : 0, PrimitiveType.Bool, token.Span);
        }

        if (token.Kind == TokenKind.CharacterLiteral)
        {
            if (!CharacterLiteralDecoder.TryDecode(token.Text.AsSpan(), out var scalar))
            {
                return ErrorExpression(token.Span);
            }
            if (expected is not (PrimitiveType.Char8 or PrimitiveType.Char16
                or PrimitiveType.Char32 or PrimitiveType.Rune))
            {
                Report("TRM3014", "A character literal requires a representable target type.", token.Span);
                return ErrorExpression(token.Span);
            }
            var valid = expected.Value switch
            {
                PrimitiveType.Char8 => scalar <= 0x7F,
                PrimitiveType.Char16 => scalar <= ushort.MaxValue,
                PrimitiveType.Char32 or PrimitiveType.Rune => scalar <= 0x10_FFFF,
                _ => false,
            };
            if (!valid)
            {
                Report("TRM3013", "Character literal is outside the target's representable scalar range.", token.Span);
                return ErrorExpression(token.Span);
            }
            return new BoundConstant(scalar, expected.Value, token.Span);
        }

        if (token.Kind != TokenKind.NumericLiteral)
        {
            Report("TRM3001", "Only boolean and integer literals are supported.", token.Span);
            return ErrorExpression(token.Span);
        }

        var text = _tree.Source.GetSubText(token.Span);
        if (expected is PrimitiveType.Float32 or PrimitiveType.Float64)
        {
            if (FloatingLiteralParser.TryParse(text.AsSpan(), expected.Value, out var floatingValue))
            {
                return new BoundConstant(floatingValue, expected.Value, token.Span);
            }
            Report("TRM3013", "Floating-point literal is outside its target format.", token.Span);
            return ErrorExpression(token.Span);
        }
        if (expected is PrimitiveType.Float16 or PrimitiveType.BFloat16 or PrimitiveType.Float128
            or PrimitiveType.Decimal32 or PrimitiveType.Decimal64 or PrimitiveType.Decimal128)
        {
            if (FloatingLiteralParser.TryParseWide(text.AsSpan(), expected.Value, out var wideFloatingValue))
            {
                return new BoundConstant(wideFloatingValue, expected.Value, token.Span);
            }
            Report("TRM3013", "Floating-point literal is outside its target format.", token.Span);
            return ErrorExpression(token.Span);
        }
        if (expected is PrimitiveType.Int128 or PrimitiveType.UInt128
            && TryBindWideIntegerLiteral(token, expected.Value, out var wideIntegerLiteral))
        {
            return wideIntegerLiteral;
        }
        var digits = text.AsSpan();
        var hasDecodedIntegers = _tree.HasDecodedIntegers;
        ulong parsed = 0;
        var overflow = false;

        if (digits.IsEmpty || (hasDecodedIntegers
            ? ScannerHost.IntegerDigitCount(digits) != digits.Length
            : !ScannerHost.TryParseIntegerMagnitude(digits, out parsed, out overflow)))
        {
            Report("TRM3013", "Floating-point literals require a float32 or float64 target context; unsupported numeric tags are not supported.", token.Span);
            return ErrorExpression(token.Span);
        }

        var type = expected;
        if (type is not (PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32
            or PrimitiveType.UInt8 or PrimitiveType.UInt16 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Char8
            or PrimitiveType.Char16 or PrimitiveType.Char32 or PrimitiveType.Rune))
        {
            Report("TRM3014", "An unsuffixed integer literal requires a numeric context.", token.Span);
        }

        var based = ScannerHost.IntegerRadix(digits) != 10;
        // Based literals construct the destination's bits; only nonzero bits beyond its width are out of range.
        ulong max = type switch {
            PrimitiveType.Int8 when based => byte.MaxValue,
            PrimitiveType.Int8 => 127UL,
            PrimitiveType.Int16 when based => ushort.MaxValue,
            PrimitiveType.Int16 => 32767UL,
            PrimitiveType.UInt32 => uint.MaxValue,
            PrimitiveType.UInt8 or PrimitiveType.Char8 => byte.MaxValue,
            PrimitiveType.UInt16 or PrimitiveType.Char16 => ushort.MaxValue,
            PrimitiveType.Char32 => uint.MaxValue,
            PrimitiveType.Rune => 0x10_FFFF,
            PrimitiveType.Int32 when based => uint.MaxValue,
            PrimitiveType.Int64 when based => ulong.MaxValue,
            PrimitiveType.Int64 => long.MaxValue,
            PrimitiveType.UInt64 => ulong.MaxValue,
            _ => int.MaxValue,
        };
        if ((hasDecodedIntegers && !TryIntegerMagnitude(token, out parsed)) || overflow)
        {
            Report("TRM3013", "Integer literal is outside its destination range.", token.Span);
            return ErrorExpression(token.Span);
        }
        if (parsed > max)
        {
            Report("TRM3013", "Integer literal is outside its destination range.", token.Span);
            return ErrorExpression(token.Span);
        }
        if (type == PrimitiveType.Rune && !IsUnicodeScalar((uint)parsed))
        {
            Report("TRM3013", "Rune literal is not a Unicode scalar value.", token.Span);
            return ErrorExpression(token.Span);
        }

        var value = type switch {
            PrimitiveType.UInt64 => unchecked((long)parsed),
            PrimitiveType.Int64 => unchecked((long)parsed),
            PrimitiveType.Int8 => unchecked((sbyte)parsed),
            PrimitiveType.Int16 => unchecked((short)parsed),
            PrimitiveType.UInt8 or PrimitiveType.Char8 => (byte)parsed,
            PrimitiveType.UInt16 or PrimitiveType.Char16 => (ushort)parsed,
            PrimitiveType.Char32 or PrimitiveType.Rune => (long)(uint)parsed,
            PrimitiveType.UInt32 => unchecked((int)parsed),
            _ => unchecked((int)parsed),
        };
        return new BoundConstant(value, type ?? PrimitiveType.Int32, token.Span);
    }

    private bool TryBindSignedIntegerLiteral(SyntaxToken token, PrimitiveType type,
        bool negative, TextSpan span, out BoundConstant constant)
    {
        constant = ErrorExpression(span);
        if (token.Kind == TokenKind.NumericLiteral
            && type is PrimitiveType.Int128 or PrimitiveType.UInt128)
        {
            return TryBindSignedWideIntegerLiteral(token, type, negative, span, out constant);
        }
        if (token.Kind != TokenKind.NumericLiteral
            || type is not (PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32
                or PrimitiveType.Int64 or PrimitiveType.UInt8 or PrimitiveType.UInt16
                or PrimitiveType.UInt32 or PrimitiveType.UInt64 or PrimitiveType.Char8
                or PrimitiveType.Char16 or PrimitiveType.Char32 or PrimitiveType.Rune))
        {
            return false;
        }
        if (negative && type is PrimitiveType.UInt8 or PrimitiveType.UInt16
            or PrimitiveType.UInt32 or PrimitiveType.UInt64)
        {
            return false;
        }
        if (!TryIntegerMagnitude(token, out var magnitude))
        {
            Report("TRM3013", "Integer literal is outside its destination range.", token.Span);
            return true;
        }

        var maximum = type switch
        {
            PrimitiveType.Int8 => negative ? 128UL : 127UL,
            PrimitiveType.Int16 => negative ? 32768UL : 32767UL,
            PrimitiveType.Int32 => negative ? 2147483648UL : 2147483647UL,
            PrimitiveType.Int64 => negative ? 9223372036854775808UL : 9223372036854775807UL,
            PrimitiveType.UInt8 or PrimitiveType.Char8 => negative ? 0UL : byte.MaxValue,
            PrimitiveType.UInt16 or PrimitiveType.Char16 => negative ? 0UL : ushort.MaxValue,
            PrimitiveType.UInt32 or PrimitiveType.Char32 => negative ? 0UL : uint.MaxValue,
            PrimitiveType.Rune => negative ? 0UL : 0x10_FFFF,
            PrimitiveType.UInt64 => negative ? 0UL : ulong.MaxValue,
            _ => ulong.MaxValue,
        };
        if (magnitude > maximum)
        {
            Report("TRM3013", "Integer literal is outside its destination range.", token.Span);
            return true;
        }
        if (type == PrimitiveType.Rune && !IsUnicodeScalar((uint)magnitude))
        {
            Report("TRM3013", "Rune literal is not a Unicode scalar value.", token.Span);
            return true;
        }

        var value = type switch
        {
            PrimitiveType.Int8 => negative ? -(long)magnitude : (long)magnitude,
            PrimitiveType.Int16 => negative ? -(long)magnitude : (long)magnitude,
            PrimitiveType.Int32 => negative ? -(long)magnitude : (long)magnitude,
            PrimitiveType.Int64 => negative && magnitude == 9223372036854775808UL
                ? long.MinValue : negative ? -(long)magnitude : (long)magnitude,
            PrimitiveType.UInt8 or PrimitiveType.Char8 => (long)(byte)magnitude,
            PrimitiveType.UInt16 or PrimitiveType.Char16 => (long)(ushort)magnitude,
            PrimitiveType.UInt32 => unchecked((int)magnitude),
            PrimitiveType.Char32 or PrimitiveType.Rune => (long)(uint)magnitude,
            PrimitiveType.UInt64 => unchecked((long)magnitude),
            _ => 0,
        };
        constant = new BoundConstant(value, type, span);
        return true;
    }

    private bool TryBindWideIntegerLiteral(SyntaxToken token, PrimitiveType type,
        out BoundConstant constant)
    {
        constant = ErrorExpression(token.Span);
        if (token.Kind != TokenKind.NumericLiteral
            || type is not (PrimitiveType.Int128 or PrimitiveType.UInt128))
        {
            return false;
        }
        if (!TryParseWideIntegerMagnitude(token, out var magnitude))
        {
            Report("TRM3013", "Integer literal is outside its destination range.", token.Span);
            return true;
        }

        var based = ScannerHost.IntegerRadix(_tree.Source.GetSubText(token.Span).AsSpan()) != 10;
        BigInteger value;
        if (type == PrimitiveType.Int128 && based)
        {
            var modulus = BigInteger.One << 128;
            if (magnitude >= modulus)
            {
                Report("TRM3013", "Integer literal is outside its destination range.", token.Span);
                return true;
            }
            var signBit = BigInteger.One << 127;
            value = magnitude >= signBit ? magnitude - modulus : magnitude;
        }
        else
        {
            var maximum = type == PrimitiveType.Int128
                ? (BigInteger.One << 127) - BigInteger.One
                : (BigInteger.One << 128) - BigInteger.One;
            if (magnitude > maximum)
            {
                Report("TRM3013", "Integer literal is outside its destination range.", token.Span);
                return true;
            }
            value = magnitude;
        }

        if (!ScalarBits.TryFromIntegerValue(value, type, out var bits))
        {
            Report("TRM3013", "Integer literal is outside its destination range.", token.Span);
            return true;
        }
        constant = new BoundConstant(bits, type, token.Span);
        return true;
    }

    private bool TryBindSignedWideIntegerLiteral(SyntaxToken token, PrimitiveType type,
        bool negative, TextSpan span, out BoundConstant constant)
    {
        constant = ErrorExpression(span);
        if (negative && type == PrimitiveType.UInt128)
        {
            return false;
        }
        if (!TryParseWideIntegerMagnitude(token, out var magnitude))
        {
            Report("TRM3013", "Integer literal is outside its destination range.", token.Span);
            return true;
        }

        var maximum = type == PrimitiveType.Int128
            ? negative ? BigInteger.One << 127 : (BigInteger.One << 127) - BigInteger.One
            : (BigInteger.One << 128) - BigInteger.One;
        if (magnitude > maximum)
        {
            Report("TRM3013", "Integer literal is outside its destination range.", token.Span);
            return true;
        }

        var value = negative ? -magnitude : magnitude;
        if (!ScalarBits.TryFromIntegerValue(value, type, out var bits))
        {
            Report("TRM3013", "Integer literal is outside its destination range.", token.Span);
            return true;
        }
        constant = new BoundConstant(bits, type, span);
        return true;
    }

    private bool TryBindSignedWideFloatingLiteral(SyntaxToken token, PrimitiveType type,
        bool negative, TextSpan span, out BoundConstant constant)
    {
        constant = ErrorExpression(span);
        var spelling = _tree.Source.GetSubText(token.Span);
        if (negative)
        {
            spelling = string.Concat("-", spelling);
        }
        else
        {
            spelling = string.Concat("+", spelling);
        }
        if (!FloatingLiteralParser.TryParseWide(spelling.AsSpan(), type, out var bits))
        {
            Report("TRM3013", "Floating-point literal is outside its target format.", token.Span);
            return true;
        }
        constant = new BoundConstant(bits, type, span);
        return true;
    }

    private bool TryParseWideIntegerMagnitude(SyntaxToken token, out BigInteger magnitude)
    {
        var text = _tree.Source.GetSubText(token.Span).AsSpan();
        var radix = ScannerHost.IntegerRadix(text);
        var digits = (radix == 10 ? text : text[2..]).ToString()
            .Replace("_", "", StringComparison.Ordinal);
        var parseText = radix == 10 ? digits : string.Concat("0", digits);
        var styles = radix switch {
            2 => NumberStyles.AllowBinarySpecifier,
            10 => NumberStyles.None,
            16 => NumberStyles.AllowHexSpecifier,
            _ => NumberStyles.None,
        };
        return BigInteger.TryParse(parseText, styles, CultureInfo.InvariantCulture, out magnitude);
    }

    private static bool IsUnicodeScalar(uint value)
        => value is <= 0x10_FFFF and not (>= 0xD800 and <= 0xDFFF);

    private bool TryIntegerMagnitude(SyntaxToken token, out ulong magnitude)
    {
        if (_tree.HasDecodedIntegers)
        {
            if (_tree.GetDecodedInteger(token) is { IsOverflow: false, Magnitude: ulong decoded })
            {
                magnitude = decoded;
                return true;
            }
            magnitude = 0;
            return false;
        }

        var digits = _tree.Source.GetSubText(token.Span).AsSpan();
        return ScannerHost.TryParseIntegerMagnitude(digits, out magnitude);
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Only expressions supported by this profile contribute a known type.")]
    [SuppressMessage("Style", "IDE0072:Add missing cases", Justification = "Unsupported token kinds have no known type.")]
    private PrimitiveType? KnownType(SyntaxNode node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ParenthesizedExpression:
                return KnownType((SyntaxNode)node.Children[1]);
            case SyntaxKind.UnsafeExpression:
                return KnownType((SyntaxNode)node.Children[2]);
            case SyntaxKind.ConstExpression:
                return KnownType((SyntaxNode)node.Children[1]);
            case SyntaxKind.VoidExpression:
                return PrimitiveType.Void;
            case SyntaxKind.NameExpression:
            {
                var token = ((SyntaxTokenElement)node.Children[0]).Token;
                var name = token.Name;
                if (TryFindVariable(token, out var slot))
                {
                    return _unresolvedVacantLocals.Contains(slot) ? null : _variables[slot].Type;
                }
                if (_hasReceiver && _currentStruct is not null
                    && _primitiveBackingFields.TryGetValue(_currentStruct, out var backing)
                    && name == backing && TryPrimitiveType(_currentStruct, out var primitive))
                {
                    return primitive;
                }
                if (_hasReceiver && _currentStruct is not null
                    && TryFindGetter(ResolveDeclaredType(_currentStruct), name, out var getter))
                {
                    return _signatures[getter].ReturnType;
                }
                if (_currentStruct is not null
                    && TryFindStaticGetter(ResolveDeclaredType(_currentStruct), name, out var staticGetter))
                {
                    return _signatures[staticGetter].ReturnType;
                }
                return _hasReceiver && _currentStruct is not null
                    && TryFindField(ResolveDeclaredType(_currentStruct), name, out var field)
                    ? GetField(ResolveDeclaredType(_currentStruct), field).Type : null;
            }
            case SyntaxKind.LiteralExpression:
            {
                var token = ((SyntaxTokenElement)node.Children[0]).Token;
                return token.Kind switch {
                    TokenKind.TrueKeyword or TokenKind.FalseKeyword => PrimitiveType.Bool,
                    _ => null,
                };
            }
            case SyntaxKind.IsNullExpression or SyntaxKind.IsSomeExpression
                or SyntaxKind.IsVarNullableExpression or SyntaxKind.IsInitializedExpression:
                return PrimitiveType.Bool;
            case SyntaxKind.InvocationExpression:
            {
                var callee = (SyntaxNode)node.Children[0];
                if (KnownType(callee) is PrimitiveType functionPointerType
                    && NativeFunctionPointerTypes.IsType(functionPointerType))
                {
                    var functionPointerIndex = NativeFunctionPointerTypes.Index(functionPointerType);
                    return (uint)functionPointerIndex < (uint)_nativeFunctionPointerSignatures.Count
                        ? _nativeFunctionPointerSignatures[functionPointerIndex].ReturnType : null;
                }
                if (callee.Kind == SyntaxKind.GenericNameExpression)
                {
                    return GenericCallType(callee, (SyntaxNode)node.Children[1]);
                }
                if (callee.Kind == SyntaxKind.NameExpression)
                {
                    var name = ((SyntaxTokenElement)callee.Children[0]).Token.Name;
                    if (!TryFindVariable(name, out _) && TryFindFunction(name, out var index))
                    {
                        return _signatures[index].ReturnType;
                    }
                    if (FindGenericTemplate(name) is not null)
                    {
                        return GenericCallType(callee, (SyntaxNode)node.Children[1]);
                    }
                    if (TryConversionTarget(callee, out var target))
                    {
                        return target;
                    }
                }

                if (callee.Kind == SyntaxKind.MemberAccessExpression)
                {
                    var left = (SyntaxNode)callee.Children[0];
                    var member = ((SyntaxTokenElement)callee.Children[2]).Token.Name;
                    var receiverType = KnownType(left);
                    if ((receiverType == PrimitiveType.TokenSpan && member == "AdvanceOne")
                        || (member == "AsSpan" && receiverType is PrimitiveType snapshot
                            && IsStruct(snapshot) && _structs[(int)snapshot - 100].Name == TokenSpanContract.SnapshotName))
                    {
                        return PrimitiveType.TokenSpan;
                    }
                    if (receiverType == PrimitiveType.OwnedInt32Buffer && member is "Slice" or "SliceMutable")
                    {
                        return member == "Slice" ? PrimitiveType.ReadonlyInt32BufferView
                            : PrimitiveType.MutableInt32BufferView;
                    }
                    if (receiverType is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView
                        && member == "Read")
                    {
                        return PrimitiveType.Int32;
                    }
                    var name = receiverType switch {
                        PrimitiveType.OwnedInt32Buffer => NativeBufferIntrinsics.TypeName + "." + member,
                        PrimitiveType type when ManagedClassTypes.Readonly(type) == PrimitiveType.ManagedNode
                            => NativeManagedNodeIntrinsics.TypeName + "." + member,
                        PrimitiveType type when IsClass(type) => _classes[ManagedClassTypes.ClassIndex(type)].Name + "." + member,
                        PrimitiveType type when IsStruct(type) => _structs[(int)type - 100].Name + "." + member,
                        PrimitiveType type when TryPrimitiveType(TypeName(type), out var intrinsic)
                            && intrinsic == type => TypeName(type) + "." + member,
                        _ => ResolveStaticMember(left, member, out var staticMember)
                            == DeclaredTypeResolution.Ambiguous ? "" : staticMember,
                    };
                    return TryFindFunction(name, out var method) ? _signatures[method].ReturnType : null;
                }
                return null;
            }
            case SyntaxKind.ObjectCreationExpression:
                return TryResolveStruct(NameText((SyntaxNode)node.Children[1]), out var created)
                    ? created : null;
            case SyntaxKind.MemberAccessExpression:
            {
                if (((SyntaxTokenElement)node.Children[2]).Token.Text == "Length"
                    && TryFindBuffer((SyntaxNode)node.Children[0], out _))
                {
                    return PrimitiveType.Int32;
                }
                var receiver = (SyntaxNode)node.Children[0];
                var member = ((SyntaxTokenElement)node.Children[2]).Token.Name;
                var receiverType = KnownType(receiver);
                if (receiverType == PrimitiveType.TokenSpan && member == "Length")
                {
                    return TokenSpanContract.IndexCarrier;
                }
                if (_currentStruct is not null
                    && _primitiveBackingFields.TryGetValue(_currentStruct, out var backing)
                    && member == backing && receiverType is PrimitiveType primitive
                    && TryPrimitiveType(_currentStruct, out var owner) && owner == primitive)
                {
                    return owner;
                }
                if (receiverType is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView
                    && member == "Length")
                {
                    return PrimitiveType.Int32;
                }
                if (receiverType == PrimitiveType.OwnedInt32Buffer && member is "Count" or "Capacity")
                {
                    return PrimitiveType.Int32;
                }
                if (receiverType is PrimitiveType nodeType
                    && ManagedClassTypes.Readonly(nodeType) == PrimitiveType.ManagedNode && member == "Value")
                {
                    return PrimitiveType.Int32;
                }
                if (receiverType is PrimitiveType type && TryFindField(type, member, out var field))
                {
                    return GetField(type, field).Type;
                }
                if (receiverType is PrimitiveType propertyType
                    && TryFindGetter(propertyType, member, out var getter))
                {
                    return _signatures[getter].ReturnType;
                }
                if (receiverType is null && !HasVariableReceiverName(receiver)
                    && ResolveStaticMember(receiver, member, out var staticMember) == DeclaredTypeResolution.Unique
                    && TryFindStaticGetter(staticMember, member, out var staticGetter))
                {
                    return _signatures[staticGetter].ReturnType;
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
                if (text is "int64.MinValue" or "int64.MaxValue")
                {
                    return PrimitiveType.Int64;
                }
                if (text is "uint64.MinValue" or "uint64.MaxValue")
                {
                    return PrimitiveType.UInt64;
                }
                if (text is "char16.MinValue" or "char16.MaxValue")
                {
                    return PrimitiveType.Char16;
                }
                return null;
            }
            case SyntaxKind.IndexExpression:
                if (KnownType((SyntaxNode)node.Children[0]) is PrimitiveType carrierType
                    && NativeFixedArrayContract.IsCarrier(carrierType)
                    && TryFindNativeFixedArrayLocal((SyntaxNode)node.Children[0], out var arrayLocal))
                {
                    return _variables[arrayLocal].NativeFixedArrayLayout?.ElementType;
                }
                if (KnownType((SyntaxNode)node.Children[0]) == PrimitiveType.TokenSpan)
                {
                    return (PrimitiveType)(100 + TokenSpanContract.Find(_structs, TokenSpanContract.TokenName));
                }
                return TryFindBuffer((SyntaxNode)node.Children[0], out var buffer)
                    ? ((SyntaxNode)node.Children[2]).Kind == SyntaxKind.SliceRangeExpression
                        ? null : _buffers[buffer].ElementType
                    : ((SyntaxNode)node.Children[2]).Kind == SyntaxKind.SliceRangeExpression
                        && KnownType((SyntaxNode)node.Children[0]) == PrimitiveType.OwnedInt32Buffer
                            ? PrimitiveType.ReadonlyInt32BufferView : null;
            case SyntaxKind.UnaryExpression:
            {
                var operandNode = (SyntaxNode)node.Children[1];
                var operand = KnownType(operandNode);
                var operation = ((SyntaxTokenElement)node.Children[0]).Token.Kind;
                if (operation == TokenKind.Ampersand)
                {
                    if (operandNode is { Kind: SyntaxKind.IndexExpression } element
                        && KnownType((SyntaxNode)element.Children[0]) is PrimitiveType arrayCarrier
                        && NativeFixedArrayContract.IsCarrier(arrayCarrier))
                    {
                        return PrimitiveType.NativeStorageAddress;
                    }
                    var functionName = NameText(operandNode);
                    if (operandNode.Kind is SyntaxKind.NameExpression or SyntaxKind.MemberAccessExpression
                        && !TryFindVariable(functionName, out _)
                        && TryFindFunction(functionName, out var function)
                        && _signatures[function].NativeFunctionPointerType is PrimitiveType functionPointerType)
                    {
                        return functionPointerType;
                    }
                    return null;
                }
                if (operation == TokenKind.Star && operand is PrimitiveType pointerType
                    && NativePointerTypeContract.IsValidType(pointerType, [.. _nativePointerTypes]))
                {
                    return _nativePointerTypes[NativePointerTypes.Index(pointerType)].PointeeType;
                }
                if (operand is PrimitiveType concrete && IsStruct(concrete)
                    && IsStaticUnaryOperator(operation)
                    && TryFindFunction(_structs[(int)concrete - 100].Name + "."
                        + OperatorName(new StaticOperatorKey(operation, 1)), out var method))
                {
                    return _signatures[method].ReturnType;
                }
                return operation == TokenKind.Bang ? PrimitiveType.Bool : operand;
            }
            case SyntaxKind.UpdateExpression:
                return KnownType((SyntaxNode)node.Children[node.Children[0] is SyntaxTokenElement ? 1 : 0]);
            case SyntaxKind.BinaryExpression:
            {
                var op = ((SyntaxTokenElement)node.Children[1]).Token.Kind;
                if (op == TokenKind.QuestionQuestion)
                {
                    var left = (SyntaxNode)node.Children[0];
                    var optional = KnownType(left);
                    var right = (SyntaxNode)node.Children[2];
                    var fallback = KnownType(right);
                    if (optional is null && IsNullExpression(left)
                        && fallback is PrimitiveType knownOptional
                        && ManagedClassTypes.IsManaged(knownOptional))
                    {
                        return knownOptional;
                    }
                    if (optional is PrimitiveType type && ManagedClassTypes.IsOptional(type))
                    {
                        return fallback is PrimitiveType rhs && ManagedClassTypes.IsOptional(rhs)
                            && CanImplicitlyConvert(ManagedClassTypes.Required(type), rhs) ? rhs
                            : IsNullExpression(right) ? type : ManagedClassTypes.Required(type);
                    }
                    return null;
                }
                if (op is TokenKind.EqualsEquals or TokenKind.BangEquals or TokenKind.Less or TokenKind.LessEquals
                    or TokenKind.Greater or TokenKind.GreaterEquals or TokenKind.AmpersandAmpersand or TokenKind.PipePipe)
                {
                    return PrimitiveType.Bool;
                }

                return KnownType((SyntaxNode)node.Children[0]) ?? KnownType((SyntaxNode)node.Children[2]);
            }
            case SyntaxKind.ConditionalExpression:
            {
                var whenTrue = KnownType((SyntaxNode)node.Children[2]);
                var whenFalse = KnownType((SyntaxNode)node.Children[4]);
                return whenTrue is null ? whenFalse : whenFalse is null || whenTrue == whenFalse ? whenTrue : null;
            }
            default:
                return null;
        }
    }

    private bool TryFindNativeFixedArrayLocal(SyntaxNode node, out int variable)
    {
        while (node.Kind == SyntaxKind.ParenthesizedExpression)
        {
            node = (SyntaxNode)node.Children[1];
        }

        variable = -1;
        if (node.Kind != SyntaxKind.NameExpression
            || !TryFindVariable(((SyntaxTokenElement)node.Children[0]).Token, out variable)
            || !NativeFixedArrayContract.IsCarrier(_variables[variable].Type)
            || _variables[variable].NativeFixedArrayLayout is not BoundNativeFixedArrayLayout layout
            || layout.CarrierType != _variables[variable].Type)
        {
            variable = -1;
            return false;
        }

        return NativeFixedArrayContract.MatchesLayout(layout, _structs);
    }

    private BoundExpression BindNativeFixedArrayElement(SyntaxNode node)
    {
        if (!TryFindNativeFixedArrayLocal((SyntaxNode)node.Children[0], out var variable)
            || _variables[variable].NativeFixedArrayLayout is not BoundNativeFixedArrayLayout layout)
        {
            Report("TRM3030", "Indexing requires an initialized local with an admitted fixed-array layout.",
                node.Span);
            return ErrorExpression(node.Span);
        }

        var index = BindNativeFixedArrayIndex((SyntaxNode)node.Children[2], layout);
        if (index is BoundConstant constant
            && (!constant.TryGetLegacyValue(out var constantValue)
                || constantValue < 0 || constantValue >= layout.ElementCount))
        {
            Report("TRM3030", $"Fixed-array element index must be in [0, {layout.ElementCount}).", index.Span);
        }

        return new BoundNativeFixedArrayElement(variable, layout, index, node.Span);
    }

    private int AddVariable(string name, PrimitiveType type, bool isMutable,
        TextSpan span, bool allowReceiverShadow = false)
    {
        foreach (var scope in _scopes)
        {
            if (scope.TryGetValue(name, out var existing)
                && !(allowReceiverShadow && name == "this" && _hasReceiver
                    && _scopes.Count == 1 && existing == 0))
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

    private int AddImplicitReferenceTemporary(PrimitiveType type, TextSpan span)
    {
        var slot = _variables.Count;
        _variables.Add(new BoundVariable($"<implicit-ref-temp-{slot}>", type, false, span) {
            IsImplicitReferenceTemporary = true,
        });
        return slot;
    }

    private bool TryFindInitReferenceProjection(SyntaxNode node, out int variable)
    {
        while (node.Kind == SyntaxKind.ParenthesizedExpression)
        {
            node = (SyntaxNode)node.Children[1];
        }
        if (node.Kind == SyntaxKind.UnaryExpression
            && ((SyntaxTokenElement)node.Children[0]).Token.Kind == TokenKind.Star
            && TryFindInitReferenceDescriptor((SyntaxNode)node.Children[1], out variable))
        {
            return true;
        }

        variable = -1;
        return false;
    }

    private bool TryFindInitReferenceDescriptor(SyntaxNode node, out int variable)
    {
        while (node.Kind == SyntaxKind.ParenthesizedExpression)
        {
            node = (SyntaxNode)node.Children[1];
        }
        if (node.Kind == SyntaxKind.NameExpression
            && TryFindVariable(((SyntaxTokenElement)node.Children[0]).Token.Name, out variable)
            && _variables[variable].IsInit)
        {
            return true;
        }

        variable = -1;
        return false;
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

    private bool TryFindVariable(SyntaxToken token, out int slot)
    {
        if (token.Text == "this" && _hasReceiver && _variables.Count != 0
            && _variables[0].Name == "this")
        {
            slot = 0;
            return true;
        }

        return TryFindVariable(token.Name, out slot);
    }

    private bool HasVariableReceiverName(SyntaxNode receiver)
    {
        while (receiver.Kind == SyntaxKind.MemberAccessExpression)
        {
            receiver = (SyntaxNode)receiver.Children[0];
        }

        return receiver.Kind == SyntaxKind.NameExpression
            && TryFindVariable(((SyntaxTokenElement)receiver.Children[0]).Token, out _);
    }

    private bool TryFindFunctionExact(string name, out int function)
    {
        if (_genericProofSource is Binder source && source._functions.TryGetValue(name, out var sourceIndex)
            && !_functions.ContainsKey(name))
        {
            _ = HydrateProofFunction(sourceIndex);
        }
        return _functions.TryGetValue(name, out function);
    }

    private bool TryFindFunction(string name, out int function)
    {
        if (_genericProofSource is Binder source && source.TryFindFunction(name, out var sourceIndex)
            && !_functions.ContainsKey(source._signatures[sourceIndex].Name))
        {
            _ = HydrateProofFunction(sourceIndex);
        }
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

    private static BoundConstant ErrorExpression(TextSpan span) => new(0, PrimitiveType.Int32, span);

    private const int MaximumDiagnosticTypeNameLength = 384;
    private const int MaximumDiagnosticTypeNameNodes = 256;
    private const int MaximumDiagnosticTypeNameDepth = 32;

    private string TypeName(PrimitiveType type)
    {
        var builder = new StringBuilder(MaximumDiagnosticTypeNameLength);
        var remainingCharacters = MaximumDiagnosticTypeNameLength;
        var remainingNodes = MaximumDiagnosticTypeNameNodes;
        AppendTypeName(type, builder, ref remainingCharacters, ref remainingNodes, 0);
        return builder.ToString();
    }

    private void AppendTypeName(PrimitiveType type, StringBuilder builder, ref int remainingCharacters,
        ref int remainingNodes, int depth)
    {
        if (remainingCharacters <= 0)
        {
            return;
        }
        if (depth > MaximumDiagnosticTypeNameDepth || --remainingNodes < 0)
        {
            AppendTypeNameText(builder, "...", ref remainingCharacters);
            return;
        }
        switch (type)
        {
            case PrimitiveType.Char8:
                AppendTypeNameText(builder, "char8", ref remainingCharacters);
                break;
            case PrimitiveType.Int32:
                AppendTypeNameText(builder, "int32", ref remainingCharacters);
                break;
            case PrimitiveType.Int8:
                AppendTypeNameText(builder, "int8", ref remainingCharacters);
                break;
            case PrimitiveType.Int16:
                AppendTypeNameText(builder, "int16", ref remainingCharacters);
                break;
            case PrimitiveType.UInt32:
                AppendTypeNameText(builder, "uint32", ref remainingCharacters);
                break;
            case PrimitiveType.UInt8:
                AppendTypeNameText(builder, "uint8", ref remainingCharacters);
                break;
            case PrimitiveType.UInt16:
                AppendTypeNameText(builder, "uint16", ref remainingCharacters);
                break;
            case PrimitiveType.Int64:
                AppendTypeNameText(builder, "int64", ref remainingCharacters);
                break;
            case PrimitiveType.UInt64:
                AppendTypeNameText(builder, "uint64", ref remainingCharacters);
                break;
            case PrimitiveType.Int128:
                AppendTypeNameText(builder, "int128", ref remainingCharacters);
                break;
            case PrimitiveType.UInt128:
                AppendTypeNameText(builder, "uint128", ref remainingCharacters);
                break;
            case PrimitiveType.Float16:
                AppendTypeNameText(builder, "float16", ref remainingCharacters);
                break;
            case PrimitiveType.BFloat16:
                AppendTypeNameText(builder, "bfloat16", ref remainingCharacters);
                break;
            case PrimitiveType.Float32:
                AppendTypeNameText(builder, "float32", ref remainingCharacters);
                break;
            case PrimitiveType.Float64:
                AppendTypeNameText(builder, "float64", ref remainingCharacters);
                break;
            case PrimitiveType.Float128:
                AppendTypeNameText(builder, "float128", ref remainingCharacters);
                break;
            case PrimitiveType.Decimal32:
                AppendTypeNameText(builder, "decimal32", ref remainingCharacters);
                break;
            case PrimitiveType.Decimal64:
                AppendTypeNameText(builder, "decimal64", ref remainingCharacters);
                break;
            case PrimitiveType.Decimal128:
                AppendTypeNameText(builder, "decimal128", ref remainingCharacters);
                break;
            case PrimitiveType.Char16:
                AppendTypeNameText(builder, "char16", ref remainingCharacters);
                break;
            case PrimitiveType.Char32:
                AppendTypeNameText(builder, "char32", ref remainingCharacters);
                break;
            case PrimitiveType.Rune:
                AppendTypeNameText(builder, "rune", ref remainingCharacters);
                break;
            case PrimitiveType.Bool:
                AppendTypeNameText(builder, "bool", ref remainingCharacters);
                break;
            case PrimitiveType.Void:
                AppendTypeNameText(builder, "void", ref remainingCharacters);
                break;
            case PrimitiveType.OwnedInt32Buffer:
                AppendTypeNameText(builder, NativeBufferIntrinsics.TypeName, ref remainingCharacters);
                break;
            case PrimitiveType.ReadonlyInt32BufferView:
                AppendTypeNameText(builder, "ReadonlyInt32BufferView", ref remainingCharacters);
                break;
            case PrimitiveType.MutableInt32BufferView:
                AppendTypeNameText(builder, "MutableInt32BufferView", ref remainingCharacters);
                break;
            case PrimitiveType.TokenSpan:
                AppendTypeNameText(builder, "Span<Token>", ref remainingCharacters);
                break;
            case PrimitiveType.NativeUtf16ZPointer:
                AppendTypeNameText(builder, "ptr<char16>", ref remainingCharacters);
                break;
            case PrimitiveType.NativeUtf8ZPointer:
                AppendTypeNameText(builder, "ptr<char8>", ref remainingCharacters);
                break;
            case PrimitiveType.NativeRawPointer:
                AppendTypeNameText(builder, "ptr<void>", ref remainingCharacters);
                break;
            case PrimitiveType.NativeStorageAddress:
                AppendTypeNameText(builder, "native address", ref remainingCharacters);
                break;
            case PrimitiveType.ManagedNode:
                AppendTypeNameText(builder, "ManagedNode", ref remainingCharacters);
                break;
            case PrimitiveType.WriteManagedNode:
                AppendTypeNameText(builder, "write ManagedNode", ref remainingCharacters);
                break;
            case PrimitiveType.OptionalManagedNode:
                AppendTypeNameText(builder, "ManagedNode?", ref remainingCharacters);
                break;
            case PrimitiveType.OptionalWriteManagedNode:
                AppendTypeNameText(builder, "write ManagedNode?", ref remainingCharacters);
                break;
            default:
                if (NativePointerTypes.IsType(type))
                {
                    AppendNativePointerTypeName(type, builder, ref remainingCharacters, ref remainingNodes, depth);
                }
                else if (NativeFunctionPointerTypes.IsType(type))
                {
                    AppendNativeFunctionPointerTypeName(type, builder, ref remainingCharacters,
                        ref remainingNodes, depth);
                }
                else if (ManagedClassTypes.IsOptional(type))
                {
                    if (ManagedClassTypes.IsWritable(type))
                    {
                        AppendTypeNameText(builder, "write ", ref remainingCharacters);
                    }
                    AppendTypeNameText(builder, _classes[ManagedClassTypes.ClassIndex(type)].Name,
                        ref remainingCharacters);
                    AppendTypeNameText(builder, "?", ref remainingCharacters);
                }
                else if (IsClass(type))
                {
                    if (ManagedClassTypes.IsWritable(type))
                    {
                        AppendTypeNameText(builder, "write ", ref remainingCharacters);
                    }
                    AppendTypeNameText(builder, _classes[ManagedClassTypes.ClassIndex(type)].Name,
                        ref remainingCharacters);
                }
                else if (IsStruct(type))
                {
                    AppendTypeNameText(builder, _structs[(int)type - 100].Name, ref remainingCharacters);
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(type));
                }
                break;
        }
    }

    private void AppendNativeFunctionPointerTypeName(PrimitiveType type, StringBuilder builder,
        ref int remainingCharacters, ref int remainingNodes, int depth)
    {
        var index = NativeFunctionPointerTypes.Index(type);
        if ((uint)index >= (uint)_nativeFunctionPointerSignatures.Count)
        {
            AppendTypeNameText(builder, $"fnptr<{index}>", ref remainingCharacters);
            return;
        }
        var signature = _nativeFunctionPointerSignatures[index];
        AppendTypeNameText(builder, "fnptr<NativeAbi, ", ref remainingCharacters);
        foreach (var parameterType in signature.ParameterTypes)
        {
            if (remainingCharacters <= 3 || remainingNodes <= 0)
            {
                AppendTypeNameText(builder, "...", ref remainingCharacters);
                return;
            }
            AppendTypeName(parameterType, builder, ref remainingCharacters, ref remainingNodes, depth + 1);
            AppendTypeNameText(builder, ", ", ref remainingCharacters);
        }
        if (remainingCharacters <= 3 || remainingNodes <= 0)
        {
            AppendTypeNameText(builder, "...", ref remainingCharacters);
            return;
        }
        AppendTypeName(signature.ReturnType, builder, ref remainingCharacters, ref remainingNodes, depth + 1);
        AppendTypeNameText(builder, ">", ref remainingCharacters);
    }

    private void AppendNativePointerTypeName(PrimitiveType type, StringBuilder builder,
        ref int remainingCharacters, ref int remainingNodes, int depth)
    {
        var index = NativePointerTypes.Index(type);
        if ((uint)index >= (uint)_nativePointerTypes.Count)
        {
            AppendTypeNameText(builder, $"ptr<{index}>", ref remainingCharacters);
            return;
        }
        var pointerType = _nativePointerTypes[index];
        if (pointerType.IsWritable)
        {
            AppendTypeNameText(builder, "write ", ref remainingCharacters);
        }
        AppendTypeNameText(builder, "ptr<", ref remainingCharacters);
        AppendTypeName(pointerType.PointeeType, builder, ref remainingCharacters, ref remainingNodes, depth + 1);
        AppendTypeNameText(builder, ">", ref remainingCharacters);
    }

    private static void AppendTypeNameText(StringBuilder builder, string text, ref int remainingCharacters)
    {
        if (remainingCharacters <= 0)
        {
            return;
        }
        if (text.Length <= remainingCharacters)
        {
            _ = builder.Append(text.AsSpan());
            remainingCharacters -= text.Length;
            return;
        }
        var ellipsisLength = Math.Min(remainingCharacters, 3);
        var contentLength = remainingCharacters - ellipsisLength;
        _ = builder.Append(text.AsSpan(0, contentLength));
        _ = builder.Append("...".AsSpan(0, ellipsisLength));
        remainingCharacters = 0;
    }

    private void Report(string code, string message, TextSpan span)
    {
        var diagnostic = new Diagnostic(code, DiagnosticSeverity.Error, message, span);
        AddDiagnostic(diagnostic, _tree);
    }

    private void AddDiagnostic(Diagnostic diagnostic, SyntaxTree tree)
    {
        (_diagnostics ??= []).Add(diagnostic);
        (_sourceDiagnostics ??= []).Add(new SourceDiagnostic(tree, diagnostic));
    }

    private enum DependencyAttributeTargetKind : byte
    {
        Default,
        Return,
        Receiver,
        Parameter,
        Operation,
    }

    private readonly record struct DependencyAttributeTarget(
        DependencyAttributeTargetKind Kind, string? ParameterName);

    private sealed record Parameter(string Name, PrimitiveType Type, TextSpan Span,
        bool IsOut, bool IsInit = false, bool IsImplicitRef = false,
        PrimitiveType? NativePointeeType = null, bool IsWritableNativePointer = false,
        bool IsEscapedThis = false);

    private sealed record Signature(string Name, PrimitiveType ReturnType, ImmutableArray<Parameter> Parameters,
        SyntaxNode? Body, SyntaxNode? Declaration, SyntaxTree Tree, string? StructName, bool IsConstructor, bool IsPrivate,
        bool IsInternal, bool IsMutable, bool IsStatic, bool IsNoThrow, bool DoesNotReturn, BoundFunction? External = null)
    {
        internal string? PropertyName { get; init; }
        internal bool IsCleanup { get; init; }
        internal bool RequiresUnsafeCaller { get; init; }
        internal bool IsOperator { get; init; }
        internal PrimitiveType? NativeFunctionPointerType { get; init; }
        internal GenericInstantiation? Generic { get; init; }
        internal ImmutableDictionary<string, PrimitiveType>? GenericOwnerTypeArguments { get; init; }
        internal bool IsClassConstructor { get; init; }
        internal int ClassConstructor { get; init; } = -1;
        internal bool IsConstEval { get; init; }
        internal ImmutableArray<int> CapturedParameters { get; init; } = [];
        internal bool ReturnsForwardedBacking { get; init; }
        internal ImmutableArray<int> ResultDependencyParameters { get; init; } = [];
        internal bool ResultDependencyIncludesReceiver { get; init; }
        internal ImmutableArray<BoundDependencyContract> DependencyContracts { get; init; } = [];
    }
}
