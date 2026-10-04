// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
    private readonly CancellationToken _cancellationToken;
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly List<SourceDiagnostic> _sourceDiagnostics = [];
    private readonly List<Signature> _signatures = [];
    private readonly HashSet<BoundLocal> _constRequired = new(ReferenceEqualityComparer.Instance);
    private readonly List<BoundStruct> _structs = [];
    private readonly Dictionary<int, (SyntaxTree Tree, SyntaxNode Declaration)> _structDeclarations = [];
    private readonly HashSet<int> _layingOut = [];
    private readonly HashSet<int> _laidOut = [];
    private readonly Dictionary<string, PrimitiveType> _structNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AliasDefinition> _aliases = new(StringComparer.Ordinal);
    private readonly List<AliasDefinition> _aliasResolutionStack = [];
    private readonly List<Dictionary<string, PrimitiveType>> _aliasTypeArguments = [];
    private readonly ImmutableArray<HostBuffer> _buffers;
    private readonly Dictionary<string, int> _functions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usings = new(StringComparer.Ordinal);
    private string _namespace = "";
    private readonly List<BoundVariable> _variables = [];
    private readonly HashSet<int> _unresolvedVacantLocals = [];
    private readonly HashSet<int> _constLocals = [];
    private int _localStart;
    private readonly List<Dictionary<string, int>> _scopes = [];
    private readonly List<LoopBinding> _loops = [];
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
    private bool _usesNativeBuffer;
    private bool _usesManagedNode;

    private sealed class LoopBinding
    {
        internal string? Name { get; init; }
        internal bool HasContinue { get; set; }
    }

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
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast, bool coreLibrary = false)
    {
        if (coreLibrary && (!isLibrary || reference is not null))
        {
            throw new ArgumentException("Core declarations require an independent library compilation.");
        }
        _tree = trees[0];
        _trees = trees;
        _buffers = buffers;
        _cancellationToken = cancellationToken;
        _isLibrary = isLibrary;
        _coreLibrary = coreLibrary;
        _reference = reference;
        _runtimePolicy = runtimePolicy;
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
                        parameter.IsInit));
                }
                _signatures.Add(new Signature(function.Name, function.ReturnType, parameters.ToImmutable(),
                    null, null, _trees[0], function.DeclaringStruct, function.IsConstructor,
                    false, false, false, function.IsStatic, function.IsNoThrow, function.DoesNotReturn, function) {
                    IsCleanup = function.IsCleanup,
                    PropertyName = function.PropertyName,
                    RequiresUnsafeCaller = function.RequiresUnsafeCaller,
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
        }
        DeclareProtocols();
        foreach (var tree in _trees)
        {
            SetContext(tree);
            foreach (var child in tree.Root.Children)
            {
                if (child is SyntaxNode { Kind: SyntaxKind.ClassDeclaration } classDeclaration)
                {
                    DeclareClassName(tree, classDeclaration);
                }
                if (child is SyntaxNode { Kind: SyntaxKind.StructDeclaration } structure)
                {
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
                    if (qualified is NativeBufferIntrinsics.TypeName or "ReadonlyInt32BufferView"
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
                    if (_classNames.ContainsKey(qualified)
                        || !_structNames.TryAdd(qualified, (PrimitiveType)(100 + index)))
                    {
                        Report("TRM3003", $"Duplicate struct '{name}'.", structure.Span);
                        continue;
                    }
                    _structs.Add(new BoundStruct(qualified, [], 0));
                    _structDeclarations.Add(index, (tree, structure));
                }
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
        foreach (var index in _structDeclarations.Keys)
        {
            DeclareStruct(index);
        }
        foreach (var index in _classDeclarations.Keys)
        {
            DeclareClass(index);
        }
        DeclareConformances();

        foreach (var tree in _trees)
        {
            SetContext(tree);
            foreach (var child in tree.Root.Children)
            {
                if (child is not SyntaxNode node || node.Kind is SyntaxKind.UsingDirective or SyntaxKind.NamespaceDeclaration)
                {
                    continue;
                }
                if (node.Kind is SyntaxKind.StructDeclaration or SyntaxKind.ClassDeclaration)
                {
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
                if (node.Kind == SyntaxKind.InterfaceDeclaration)
                {
                    continue;
                }
                if (node.Kind == SyntaxKind.AliasDeclaration)
                {
                    continue;
                }
                if (node.Kind != SyntaxKind.FunctionDeclaration)
                {
                    Report("TRM3001", "Only top-level function declarations are supported.", node.Span);
                    continue;
                }
                if (node.Children.Any(static child => child is SyntaxNode { Kind: SyntaxKind.TypeParameterList }))
                {
                    DeclareGeneric(node);
                }
                else
                {
                    Declare(node, null);
                }
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
                SetContext(template.Tree);
                Report("TRM3003", $"Generic and concrete functions cannot share '{template.Name}'.",
                    template.Declaration.Span);
            }
        }
        ValidateConformances();
        if (_diagnostics.Count == 0 && !ValidateGenericTemplates())
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
                    _tree = signature.Tree;
                    Report("TRM3002", "Multiple parameterless Main functions make the entry point ambiguous.", declaration.Span);
                }
                else
                {
                    entryPoint = index;
                    if (signature.RequiresUnsafeCaller)
                    {
                        _tree = signature.Tree;
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
            SetContext(signature.Tree);
            _variables.Clear();
            _scopes.Clear();
            _unresolvedVacantLocals.Clear();
            _constLocals.Clear();
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
                var slot = AddVariable(parameter.Name, parameter.Type, parameter.IsOut, parameter.Span);
                if (parameter.IsOut)
                {
                    _variables[slot] = _variables[slot] with { IsOut = true, IsInit = parameter.IsInit };
                }
            }

            _localStart = _variables.Count;
            var body = signature.Generic is GenericInstantiation generic
                ? BindGenericBody(generic) : BindCallableBody(syntaxBody);
            if (signature.IsConstructor)
            {
                var end = new TextSpan(syntaxBody.Span.End, 0);
                body = body with {
                    Statements = body.Statements.Add(new BoundReturn(
                    new BoundRead(0, _returnType, end), end))
                };
                if (_structs[(int)_returnType - 100].Fields.Any(field => IsUnique(field.Type)))
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
                IsNoThrow = signature.IsNoThrow,
                RequiresUnsafeCaller = signature.RequiresUnsafeCaller,
                IsClassConstructor = signature.IsClassConstructor,
                IsCleanup = signature.IsCleanup,
                DoesNotReturn = signature.DoesNotReturn,
                DeclaringStruct = signature.StructName is null ? null
                   : (string.IsNullOrEmpty(_namespace) ? "" : _namespace + ".") + signature.StructName,
                PropertyName = signature.PropertyName,
            });
        }

        if (_diagnostics.Count != 0)
        {
            return null;
        }
        if (!ResolveRequiredConstants(bound))
        {
            return null;
        }
        var program = new BoundProgram(bound.ToImmutable(), entryPoint) {
            Structures = [.. _structs],
            Classes = [.. _classes],
            Buffers = _buffers,
            RuntimePolicy = _runtimePolicy,
        };
        if (!SemanticVerifier.TryVerifySource(program, out var functionIndex, out var span, out var exceededWorkLimit, out var invalidEffect))
        {
            _tree = _signatures.Count == 0 ? _trees[0] : _signatures[functionIndex].Tree;
            var (code, message) = invalidEffect switch {
                "NoThrow" => ("TRM3017", "NoThrow declarations cannot perform potentially throwing operations or call functions without a NoThrow contract."),
                "DoesNotReturn" => ("TRM3018", "DoesNotReturn declarations must have no reachable normal return or fallthrough."),
                "NormalReturn" => ("TRM3012", "Not all normally completing paths return a value."),
                "FinallyReturn" => ("TRM3019", "A finally block cannot return or transfer control out of cleanup."),
                "LoopTransfer" => ("TRM3008", "Loop transfer requires an enclosing loop."),
                "FinallyNoReturn" => ("TRM3020", "Potentially nonreturning finally cleanup is unsupported in this bounded profile."),
                "UnsafeRecovery" => ("TRM3021", "Checked failure recovery requires an unsafe block."),
                "CheckedPolicy" => ("TRM3022", "Recovery requires the checked-exceptions runtime policy."),
                "Ownership" => ("TRM3023", "A unique value must be moved from a live named owner; copies, vacant reads, and escaping receiver loans are not allowed."),
                _ => ("TRM3016", exceededWorkLimit
                    ? "Source exceeds the semantic verification work limit."
                    : "Source violates definite assignment, initialization, or executable type rules."),
            };
            Report(code, message, span);
            return null;
        }
        return program;
    }

    private static string DirectiveName(SyntaxNode directive)
    {
        var builder = new StringBuilder();
        for (var index = 1; index < directive.Children.Length - 1; index++)
        {
            _ = builder.Append(((SyntaxTokenElement)directive.Children[index]).Token.Name);
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

    internal static bool TryPrimitiveType(string name, out PrimitiveType type)
    {
        type = name switch
        {
            "bool" => PrimitiveType.Bool,
            "char16" => PrimitiveType.Char16,
            "int32" => PrimitiveType.Int32,
            "uint32" => PrimitiveType.UInt32,
            "int64" => PrimitiveType.Int64,
            "uint64" => PrimitiveType.UInt64,
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
                            or TokenKind.InternalKeyword or TokenKind.MutKeyword or TokenKind.UnsafeKeyword))
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
                || member is SyntaxTokenElement { Token.Kind: TokenKind.UniqueKeyword
                    or TokenKind.MutKeyword or TokenKind.InternalKeyword or TokenKind.UnsafeKeyword })
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
        SetContext(tree);
        if (_layingOut.Count >= MaximumSemanticDepth || !_layingOut.Add(index))
        {
            Report("TRM3005", "Value layouts must be acyclic and within the semantic nesting limit.", declaration.Span);
            return;
        }
        var name = StructName(declaration);
        var qualified = string.IsNullOrEmpty(_namespace) ? name.Name : _namespace + "." + name.Name;
        var isPublic = false;
        var isUnique = false;
        var isTransparent = false;
        var isSequential = false;
        var visibilityCount = 0;
        foreach (var element in declaration.Children)
        {
            if (element is SyntaxNode { Kind: SyntaxKind.Attribute } attribute)
            {
                if (attribute.Children is [
                    SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket },
                    SyntaxTokenElement { Token.Kind: TokenKind.Identifier, Token.Text: "Layout" },
                    SyntaxTokenElement { Token.Kind: TokenKind.OpenParen },
                    SyntaxTokenElement { Token.Kind: TokenKind.Identifier } layout,
                    SyntaxTokenElement { Token.Kind: TokenKind.CloseParen },
                    SyntaxTokenElement { Token.Kind: TokenKind.CloseBracket }]
                    && !isTransparent && !isSequential
                    && (layout.Token.Text is "Transparent" or "Sequential"))
                {
                    isTransparent = layout.Token.Text == "Transparent";
                    isSequential = layout.Token.Text == "Sequential";
                }
                else
                {
                    Report("TRM3001", "Only one [Layout(Transparent)] or [Layout(Sequential)] attribute is supported on a struct.", attribute.Span);
                }
            }
            if (element is SyntaxTokenElement modifier)
            {
                isPublic |= modifier.Token.Kind == TokenKind.PublicKeyword;
                isUnique |= modifier.Token.Kind == TokenKind.UniqueKeyword;
                visibilityCount += modifier.Token.Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword or TokenKind.PrivateKeyword ? 1 : 0;
                if (modifier.Token.Kind is TokenKind.PrivateKeyword or TokenKind.StaticKeyword
                    or TokenKind.MutKeyword or TokenKind.UnsafeKeyword)
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
            if (IsStruct(type) && !_laidOut.Contains((int)type - 100))
            {
                DeclareStruct((int)type - 100);
                SetContext(tree);
            }
            if (IsUnique(type) && !isUnique)
            {
                Report("TRM3023", "Copy structs cannot store unique owning fields.", field.Span);
            }
            var width = ValueLayout.Size(type, _structs);
            var fieldAlignment = width == 0 ? 1 : ValueLayout.Alignment(type, _structs);
            var duplicate = false;
            foreach (var previous in fields)
            {
                duplicate |= previous.Name == fieldName.Name;
            }
            var offset = (size + fieldAlignment - 1) & -fieldAlignment;
            if (width == 0 || offset + width > ValueLayout.MaximumSize || duplicate)
            {
                Report("TRM3005", "Structs require distinct, initialized value fields within the 16-byte aggregate profile.", field.Span);
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
            || fields[0].Type is not (PrimitiveType.Bool or PrimitiveType.Char16
                or PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64)))
        {
            Report("TRM3005", "Transparent Copy structs require exactly one scalar value field.", declaration.Span);
        }
        if (isSequential && (isPublic || isUnique || fields.Any(static field => !field.IsPrivate)))
        {
            Report("TRM3005", "Sequential layout supports only nonpublic Copy structs with private fields.", declaration.Span);
        }
        size = (size + alignment - 1) & -alignment;
        if (size is < 1 or > ValueLayout.MaximumSize)
        {
            Report("TRM3005", "A struct must have a naturally aligned size from 1 through 16 bytes.", declaration.Span);
        }
        _structs[index] = new BoundStruct(qualified, fields.ToImmutable(), size) {
            IsInternal = !isPublic,
            IsUnique = isUnique,
            IsTransparent = isTransparent,
            IsSequential = isSequential,
        };
        _ = _layingOut.Remove(index);
        _ = _laidOut.Add(index);
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

    private void DeclareAliases()
    {
        foreach (var tree in _trees)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            SetContext(tree);
            foreach (var declaration in tree.Root.Children.OfType<SyntaxNode>()
                .Where(static node => node.Kind == SyntaxKind.AliasDeclaration))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var nameToken = declaration.Children.OfType<SyntaxTokenElement>()
                    .FirstOrDefault(static element => element.Token.Kind == TokenKind.Identifier)?.Token;
                var target = declaration.Children.OfType<SyntaxNode>()
                    .FirstOrDefault(static node => node.Kind == SyntaxKind.TypeName);
                if (nameToken is not SyntaxToken name || target is null)
                {
                    continue;
                }
                var typeParameterList = declaration.Children.OfType<SyntaxNode>()
                    .FirstOrDefault(static node => node.Kind == SyntaxKind.TypeParameterList);
                var typeParameters = typeParameterList is null ? [] : typeParameterList.Children
                    .OfType<SyntaxTokenElement>()
                    .Where(static element => element.Token.Kind == TokenKind.Identifier)
                    .Select(static element => element.Token.Name).ToImmutableArray();
                if (typeParameters.Distinct(StringComparer.Ordinal).Count() != typeParameters.Length)
                {
                    Report("TRM3001", "A type alias cannot repeat a type parameter.", declaration.Span);
                    continue;
                }
                var visibility = declaration.Children.OfType<SyntaxTokenElement>()
                    .Where(static element => element.Token.Kind is TokenKind.PublicKeyword
                        or TokenKind.InternalKeyword or TokenKind.PrivateKeyword)
                    .Select(static element => element.Token.Kind).ToArray();
                var isPublic = visibility.Contains(TokenKind.PublicKeyword);
                if (visibility.Length > 1 || visibility.Contains(TokenKind.PrivateKeyword)
                    || declaration.Children.OfType<SyntaxTokenElement>().Any(static element =>
                        element.Token.Kind is TokenKind.StaticKeyword or TokenKind.MutKeyword
                            or TokenKind.UniqueKeyword or TokenKind.UnsafeKeyword)
                    || declaration.Children.OfType<SyntaxNode>()
                        .Any(static node => node.Kind == SyntaxKind.Attribute))
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
                    || qualifiedName is NativeBufferIntrinsics.TypeName
                        or "ReadonlyInt32BufferView" or "MutableInt32BufferView"
                    || name.Name is NativeManagedNodeIntrinsics.TypeName or NativeManagedNodeIntrinsics.HeapName
                    || _structNames.ContainsKey(qualifiedName)
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
            var previousTree = _tree;
            SetContext(alias.Tree);
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
            SetContext(previousTree);
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
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        if (name.Contains('.', StringComparison.Ordinal))
        {
            Add(name);
        }
        else
        {
            Add(name);
            Add(Qualify(name));
            var parent = _namespace;
            while (parent.Contains('.', StringComparison.Ordinal))
            {
                parent = parent[..parent.LastIndexOf('.', StringComparison.Ordinal)];
                Add(parent + "." + name);
            }
            foreach (var imported in _usings)
            {
                Add(imported + "." + name);
            }
        }
        return candidates.Count == 1 ? _aliases[candidates.Single()] : null;

        void Add(string qualified)
        {
            if (_aliases.ContainsKey(qualified))
            {
                _ = candidates.Add(qualified);
            }
        }
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

        var previousTree = _tree;
        SetContext(alias.Tree);
        var diagnosticCount = _diagnostics.Count;
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
            if (_diagnostics.Count != diagnosticCount)
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
            SetContext(previousTree);
        }
    }

    private bool IsPublicType(PrimitiveType type)
    {
        if (IsStruct(type))
        {
            var index = (int)type - 100;
            return !_structDeclarations.TryGetValue(index, out var declaration)
                || declaration.Declaration.Children.OfType<SyntaxTokenElement>()
                    .Any(static element => element.Token.Kind == TokenKind.PublicKeyword);
        }
        if (IsClass(type) || (ManagedClassTypes.IsOptional(type) && (int)type >= ManagedClassTypes.First))
        {
            var index = ManagedClassTypes.ClassIndex(type);
            return !_classDeclarations.TryGetValue(index, out var declaration)
                || declaration.Declaration.Children.OfType<SyntaxTokenElement>()
                    .Any(static element => element.Token.Kind == TokenKind.PublicKeyword);
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
        var isNoThrow = false;
        var requiresUnsafeCaller = false;
        var isCleanup = false;
        var isConstEval = false;
        var doesNotReturn = false;
        var accessModifiers = 0;
        for (var index = 0; index < modifierCount; index++)
        {
            if (declaration.Children[index] is SyntaxNode { Kind: SyntaxKind.Attribute } attribute)
            {
                if (property)
                {
                    Report("TRM3001", "Getter effects belong on the get accessor.", attribute.Span);
                }
                else if (attribute.Children[1] is SyntaxTokenElement { Token.Text: "NoThrow" } && !isNoThrow)
                {
                    isNoThrow = true;
                }
                else if (attribute.Children[1] is SyntaxTokenElement { Token.Text: "DoesNotReturn" } && !doesNotReturn)
                {
                    doesNotReturn = true;
                }
                else if (attribute.Children[1] is SyntaxTokenElement { Token.Text: "Cleanup" } && !isCleanup)
                {
                    isCleanup = true;
                }
                else if (attribute.Children is [_, SyntaxTokenElement { Token.Text: "ConstEval" }, _]
                    && !isConstEval)
                {
                    isConstEval = true;
                }
                else
                {
                    Report("TRM3001", "Only distinct parameterless NoThrow, DoesNotReturn, Cleanup, and ConstEval contracts are supported.", attribute.Span);
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
        if (property)
        {
            if (getter is not null)
            {
                foreach (var child in getter.Children)
                {
                    if (child is SyntaxNode { Kind: SyntaxKind.Attribute } attribute)
                    {
                        if (attribute.Children[1] is not SyntaxTokenElement { Token.Text: "NoThrow" } || isNoThrow)
                        {
                            Report("TRM3001", "Only a single getter-local NoThrow effect is supported.", attribute.Span);
                        }
                        isNoThrow = true;
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
            if (containingType is null || isStatic || isMutable || isCleanup || doesNotReturn
                || (!TryResolveStruct(Qualify(containingType), out var propertyOwner)
                    && !TryResolveClass(Qualify(containingType), out propertyOwner)
                    && !_primitiveDeclarations.Contains(containingType))
                || (IsStruct(propertyOwner) && IsUnique(propertyOwner))
                || TryFindField(propertyOwner, nameToken.Name, out _)
                || containingType is not null
                    && _primitiveBackingFields.TryGetValue(containingType, out var backing)
                    && backing == nameToken.Name)
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
        var returnType = constructor ? ResolveDeclaredType(containingType!) : BindType(returnNode!, allowVoid: true);
        if (property && (returnType == PrimitiveType.Void || !IsCopyGenericType(returnType)
            || IsUnique(returnType)))
        {
            Report("TRM3007", "A getter must return a supported by-value Copy type.", declaration.Span);
        }
        if (returnType is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView)
        {
            Report("TRM3023", "A native buffer view cannot be returned.", returnNode!.Span);
        }
        if (isCleanup)
        {
            var structureIndex = containingType is not null && !classConstructor
                && _structNames.TryGetValue(Qualify(containingType), out var structType)
                && IsStruct(structType) ? (int)structType - 100 : -1;
            var structure = structureIndex < 0 ? null : _structs[structureIndex];
            if (structure is null || !structure.IsUnique || isStatic || constructor || isMutable || !isNoThrow
                || requiresUnsafeCaller || returnType != PrimitiveType.Void || parameters?.Children.Length != 2
                || structure.CleanupMethod is not null || isPrivate || isInternal)
            {
                Report("TRM3023", "Cleanup requires one public, NoThrow, nonmutating, parameterless instance method on a unique struct.", declaration.Span);
            }
            else
            {
                _structs[structureIndex] = structure with {
                    CleanupMethod = structure.Name + "." + nameText,
                };
            }
        }
        if (doesNotReturn && returnType != PrimitiveType.Void)
        {
            Report("TRM3018", "DoesNotReturn requires a void function.", declaration.Span);
        }
        if (isConstEval && (containingType is not null || !isInternal || constructor || isCleanup
            || doesNotReturn || !ConstEvalScalar(returnType)))
        {
            Report("TRM3024", "ConstEval requires an internal scalar-returning top-level function.", declaration.Span);
        }
        var declared = ImmutableArray.CreateBuilder<Parameter>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var child in parameters?.Children ?? [])
        {
            if (child is not SyntaxNode parameter)
            {
                continue;
            }

            var typeNode = parameter.Children.OfType<SyntaxNode>().Single();
            var token = ((SyntaxTokenElement)parameter.Children[^1]).Token;
            var type = BindType(typeNode, allowVoid: false);
            if (type is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView)
            {
                Report("TRM3023", "A native buffer view cannot be a parameter.", parameter.Span);
            }

            var isInit = parameter.Children is [SyntaxTokenElement { Token.Text: "init" },
                SyntaxTokenElement { Token.Kind: TokenKind.RefKeyword },
                SyntaxTokenElement { Token.Kind: TokenKind.Less }, SyntaxNode,
                SyntaxTokenElement { Token.Kind: TokenKind.Greater }, SyntaxTokenElement];
            var isOut = parameter.Children.Length == 3
                && parameter.Children[0] is SyntaxTokenElement passing
                && passing.Token.Kind == TokenKind.OutKeyword;
            if (parameter.Children.Length != 2 && !isOut && !isInit)
            {
                Report("TRM3001", "Passing modes are not supported.", parameter.Span);
            }
            if (isOut && returnType != PrimitiveType.Bool
                && type is not (PrimitiveType.Bool or PrimitiveType.Char16 or PrimitiveType.Int32
                    or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64))
            {
                Report("TRM3011", "A non-boolean output parameter requires a scalar type.", parameter.Span);
            }
            if (isInit && type is not (PrimitiveType.Bool or PrimitiveType.Char16 or PrimitiveType.Int32
                or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64)
                && !IsStruct(type) && !IsUnique(type) && !ManagedClassTypes.IsManaged(type))
            {
                Report("TRM3011", "Init ref parameters require supported scalar, Copy-struct, unique, or managed-handle outputs.",
                    parameter.Span);
            }
            if (isInit && ManagedClassTypes.IsManaged(type) && !isPrivate && !isInternal)
            {
                Report("TRM3011", "Managed-handle init ref parameters are compilation-private.", parameter.Span);
            }
            if (isConstEval && (isOut || isInit || !ConstEvalScalar(type)))
            {
                Report("TRM3024", "ConstEval parameters must be non-output scalar values.", parameter.Span);
            }

            if (!names.Add(token.Name))
            {
                Report("TRM3004", $"Duplicate parameter '{token.Text}'.", token.Span);
            }

            declared.Add(new Parameter(token.Name, type, token.Span, isOut || isInit, isInit));
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
        if (!_functions.TryAdd(qualifiedName, _signatures.Count))
        {
            Report("TRM3003", $"Duplicate function '{nameToken.Text}'; overloads are not supported.", nameToken.Span);
        }

        _signatures.Add(new Signature(qualifiedName, classConstructor ? PrimitiveType.Void : returnType,
            declared.ToImmutable(), body, declaration, _tree,
            containingType, constructor && !classConstructor, isPrivate, isInternal, isMutable, isStatic,
            isNoThrow, doesNotReturn) {
            IsCleanup = isCleanup,
            IsOperator = operatorDeclaration,
            IsClassConstructor = classConstructor,
            PropertyName = property ? nameText : null,
            IsConstEval = isConstEval,
            RequiresUnsafeCaller = requiresUnsafeCaller,
        });
    }

    private PrimitiveType BindType(SyntaxNode node, bool allowVoid)
    {
        var name = TypeNameIdentifier(node);
        var writable = node.Children[0] is SyntaxTokenElement { Token.Kind: TokenKind.WriteKeyword };
        var optional = node.Children[^1] is SyntaxTokenElement { Token.Kind: TokenKind.Question };
        var typeArgumentNodes = node.Children.OfType<SyntaxNode>().ToArray();
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
                var diagnosticCount = _diagnostics.Count;
                var arguments = ImmutableArray.CreateBuilder<PrimitiveType>(typeArgumentNodes.Length);
                foreach (var argumentNode in typeArgumentNodes)
                {
                    arguments.Add(BindType(argumentNode, allowVoid: false));
                }
                if (_diagnostics.Count != diagnosticCount)
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
            && TryFindVariable(((SyntaxTokenElement)receiver.Children[0]).Token.Name, out var slot)
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
            && TryFindVariable(((SyntaxTokenElement)receiver.Children[0]).Token.Name, out var slot)
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

        var arguments = ((SyntaxNode)initializer.Children[1]).Children.OfType<SyntaxNode>().ToArray();
        if (arguments.Length != 2 || arguments.Any(static argument =>
            argument.Children.Length != 1 || argument.Children[0] is not SyntaxNode))
        {
            Report("TRM3011", "Slice requires two positional int32 arguments.", initializer.Span);
            var invalid = AddVariable(name.Name, viewType, isMutable, name.Span);
            return new BoundViewScope(invalid, ErrorExpression(span), ErrorExpression(span), ErrorExpression(span),
                new BoundBlock([], span), span) { IsMutable = writable };
        }

        var owner = writable ? BindMutableReceiverLoan((SyntaxNode)callee.Children[0],
                PrimitiveType.OwnedInt32Buffer, callee.Span, allowBufferField: true)
            : BindReceiverLoan((SyntaxNode)callee.Children[0], PrimitiveType.OwnedInt32Buffer);
        var start = BindExpression((SyntaxNode)arguments[0].Children[0], PrimitiveType.Int32);
        var length = BindExpression((SyntaxNode)arguments[1].Children[0], PrimitiveType.Int32);
        var variable = AddVariable(name.Name, viewType, isMutable, name.Span);
        return new BoundViewScope(variable, owner, start, length, new BoundBlock([], span), span) {
            IsMutable = writable,
        };
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
        if (lower.Kind == SyntaxKind.EndRelativeIndexBound)
        {
            Report("TRM3001", "End-relative lower bounds are not supported for native buffer views.", lower.Span);
            var invalid = AddVariable(name.Name, viewType, false, name.Span);
            return new BoundViewScope(invalid, ErrorExpression(span), ErrorExpression(span), ErrorExpression(span),
                new BoundBlock([], span), span);
        }

        var valid = true;
        var startValue = lower.Kind == SyntaxKind.OmittedIndexBound
            ? 0
            : BindRangeConstant(lower, "lower", ref valid);
        var owner = BindReceiverLoan((SyntaxNode)initializer.Children[0], PrimitiveType.OwnedInt32Buffer);
        BoundExpression length;
        if (connector.Kind == TokenKind.DotDotCaret || upper.Kind == SyntaxKind.EndRelativeIndexBound)
        {
            var offsetNode = upper.Kind == SyntaxKind.EndRelativeIndexBound
                ? (SyntaxNode)upper.Children[1] : upper;
            var offset = BindRangeConstant(offsetNode, "end-relative upper", ref valid);
            var totalOffset = (long)startValue + offset;
            if (startValue < 0 || offset < 0 || totalOffset > int.MaxValue)
            {
                length = new BoundConstant(-1, PrimitiveType.Int32, upper.Span);
            }
            else
            {
                length = BindBufferRemaining(owner, (int)totalOffset, upper.Span);
            }
        }
        else if (upper.Kind == SyntaxKind.OmittedIndexBound)
        {
            length = startValue < 0
                ? new BoundConstant(-1, PrimitiveType.Int32, upper.Span)
                : BindBufferRemaining(owner, startValue, upper.Span);
        }
        else
        {
            var endValue = BindRangeConstant(upper, "upper", ref valid);
            var lengthValue = (long)endValue - startValue;
            length = lengthValue is < int.MinValue or > int.MaxValue
                ? new BoundConstant(-1, PrimitiveType.Int32, upper.Span)
                : new BoundConstant(lengthValue, PrimitiveType.Int32, upper.Span);
        }

        var start = valid
            ? new BoundConstant(startValue, PrimitiveType.Int32, lower.Span)
            : new BoundConstant(-1, PrimitiveType.Int32, lower.Span);
        var variable = AddVariable(name.Name, viewType, false, name.Span);
        return new BoundViewScope(variable, owner, start, length, new BoundBlock([], span), span);
    }

    private int BindRangeConstant(SyntaxNode node, string boundName, ref bool valid)
    {
        var expression = BindExpression(node, PrimitiveType.Int32);
        if (expression is BoundConstant { Type: PrimitiveType.Int32 } constant
            && constant.Value is >= int.MinValue and <= int.MaxValue)
        {
            return (int)constant.Value;
        }

        Report("TRM3001", $"The {boundName} bound of a native buffer view must be a compile-time int32 constant.",
            node.Span);
        valid = false;
        return 0;
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

    private int BindViewVariable(SyntaxNode receiver)
    {
        while (receiver.Kind == SyntaxKind.ParenthesizedExpression)
        {
            receiver = (SyntaxNode)receiver.Children[1];
        }
        if (receiver.Kind == SyntaxKind.NameExpression
            && TryFindVariable(((SyntaxTokenElement)receiver.Children[0]).Token.Name, out var slot)
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
                bound = bound[..index].Add(view with { Body = new BoundBlock(bound[(index + 1)..], block.Span) });
            }
            if (bound[index] is BoundLocal local && IsUnique(local.Initializer.Type))
            {
                var method = CleanupMethod(local.Initializer.Type);
                var cleanupFunction = method is not null && _functions.TryGetValue(method, out var found) ? found : -1;
                bound = bound[..(index + 1)].Add(new BoundTryFinally(
                    new BoundBlock(bound[(index + 1)..], block.Span),
                    new BoundBlock([new BoundDrop(local.Variable, cleanupFunction, block.Span)], block.Span),
                    block.Span));
            }
            if (bound[index] is BoundVacantLocal vacant && IsUnique(_variables[vacant.Variable].Type))
            {
                var method = CleanupMethod(_variables[vacant.Variable].Type);
                var cleanupFunction = method is not null && _functions.TryGetValue(method, out var found) ? found : -1;
                bound = bound[..(index + 1)].Add(new BoundTryFinally(
                    new BoundBlock(bound[(index + 1)..], block.Span),
                    new BoundBlock([new BoundDrop(vacant.Variable, cleanupFunction, block.Span)], block.Span),
                    block.Span));
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
                    .Add(new BoundOwnerDeclaration(slot, block.Span))
                    .Add(new BoundTryFinally(new BoundBlock(bound[index..], block.Span),
                        new BoundBlock([new BoundDrop(slot, cleanupFunction, block.Span)], block.Span),
                        block.Span));
            }
        }
        return new BoundBlock(bound, block.Span) {
            IsManagedLexicalScope = true,
            RetiredManagedLocals = managedLocals,
        };
    }

    private BoundViewSet? BindViewSetStatement(SyntaxNode expressionNode, TextSpan span)
    {
        if (expressionNode.Kind != SyntaxKind.InvocationExpression
            || expressionNode.Children[0] is not SyntaxNode { Kind: SyntaxKind.MemberAccessExpression } member
            || ((SyntaxTokenElement)member.Children[2]).Token.Text != "Set"
            || KnownType((SyntaxNode)member.Children[0]) != PrimitiveType.MutableInt32BufferView)
        {
            return null;
        }

        var arguments = ((SyntaxNode)expressionNode.Children[1]).Children.OfType<SyntaxNode>().ToArray();
        if (arguments.Length == 2 && arguments.All(static argument => argument.Children is [SyntaxNode]))
        {
            return new BoundViewSet(BindViewVariable((SyntaxNode)member.Children[0]),
                BindExpression((SyntaxNode)arguments[0].Children[0], PrimitiveType.Int32),
                BindExpression((SyntaxNode)arguments[1].Children[0], PrimitiveType.Int32), span);
        }
        Report("TRM3011", "A mutable native buffer view supports Set(index, value) with two positional int32 arguments.",
            expressionNode.Span);
        return null;
    }

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
                var typeElement = node.Children[1];
                var name = ((SyntaxTokenElement)node.Children[2]).Token;
                var inferred = typeElement is SyntaxTokenElement;
                var type = inferred ? PrimitiveType.Void : BindType((SyntaxNode)typeElement, allowVoid: false);
                if (type is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView)
                {
                    Report("TRM3023", "Native buffer views require an initialized lexical owner loan.", node.Span);
                }
                var slot = AddVariable(name.Name, type, false, name.Span);
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
                    return new BoundBufferAssignment(buffer,
                        BindExpression((SyntaxNode)target.Children[2], PrimitiveType.Int32),
                        BindExpression(valueNode, _buffers[buffer].ElementType), node.Span);
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
                if (place.Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32
                    or PrimitiveType.Int64 or PrimitiveType.UInt64))
                {
                    Report("TRM3007", "Built-in compound assignment requires an integer place; nominal compound operators are unsupported.", op.Span);
                }
                CheckWritableUpdatePlace(place, target.Span);
                var right = BindExpression((SyntaxNode)node.Children[2],
                    op.Kind is TokenKind.ShiftLeftEquals or TokenKind.ShiftRightEquals or TokenKind.ShiftRightUnsignedEquals
                        ? PrimitiveType.Int32 : place.Type);
                if (right.Type != (op.Kind is TokenKind.ShiftLeftEquals or TokenKind.ShiftRightEquals
                    or TokenKind.ShiftRightUnsignedEquals ? PrimitiveType.Int32 : place.Type))
                {
                    Report("TRM3007", "Compound assignment requires an exact operand type.", op.Span);
                }
                return new BoundExpressionStatement(
                    new BoundCompoundAssignment(op.Kind, place, right, place.Type, node.Span), node.Span);
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
                if (!isDirectCall)
                {
                    Report("TRM3009", "Only direct calls and updates may be used as expression statements.", expressionNode.Span);
                }

                return new BoundExpressionStatement(BindExpression(expressionNode, null), node.Span);
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
                    if (value is BoundCall)
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
                var target = label is null ? _loops.Count - 1
                    : _loops.FindLastIndex(loop => loop.Name == label);
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
                if (!_variables[local.Variable].IsMutable || local.Variable == 0 && _hasReceiver)
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

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Unsupported syntax kinds are rejected by the default branch.")]
    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Runtime struct IDs are represented by enum values outside the declared primitive range.")]
    private BoundExpression BindExpression(SyntaxNode node, PrimitiveType? expected)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var diagnosticCount = _diagnostics.Count;
        BoundExpression expression;

        switch (node.Kind)
        {
            case SyntaxKind.ParenthesizedExpression:
                expression = BindExpression((SyntaxNode)node.Children[1], expected);
                break;

            case SyntaxKind.UnsafeExpression:
            {
                _unsafeDepth++;
                try
                {
                    expression = BindExpression((SyntaxNode)node.Children[2], expected);
                }
                finally
                {
                    _unsafeDepth--;
                }
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
                    if (expected is not PrimitiveType optionalType || !ManagedClassTypes.IsOptional(optionalType))
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
                var initializedSource = BindExpression((SyntaxNode)node.Children[0], null);
                if (initializedSource is not BoundRead initializedRead)
                {
                    Report("TRM3007", "'is init' requires a named local or parameter.", node.Span);
                    expression = new BoundConstant(0, PrimitiveType.Bool, node.Span);
                }
                else
                {
                    expression = new BoundIsInitialized(initializedRead.Variable, node.Span);
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
                if (TryFindVariable(token.Name, out var slot))
                {
                    expression = new BoundRead(slot, _variables[slot].Type, node.Span);
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
                    expression = new BoundConstant(_buffers[buffer].Values.Length, PrimitiveType.Int32, node.Span);
                    break;
                }
                var member = ((SyntaxTokenElement)node.Children[2]).Token.Name;
                var receiverNode = (SyntaxNode)node.Children[0];
                var receiverType = KnownType(receiverNode);
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
                var typeName = NameText(receiverNode);
                if (member is "MinValue" or "MaxValue" && typeName is "int32" or "uint32" or "int64" or "uint64" or "char16")
                {
                    var type = typeName switch {
                        "int32" => PrimitiveType.Int32,
                        "uint32" => PrimitiveType.UInt32,
                        "int64" => PrimitiveType.Int64,
                        "uint64" => PrimitiveType.UInt64,
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

                // A decimal negative minimum cannot be represented as a positive operand of its signed type.
                if (op.Kind == TokenKind.Minus && operandNode.Kind == SyntaxKind.LiteralExpression &&
                    operandType is PrimitiveType.Int32 or PrimitiveType.Int64 &&
                    TryMinimumLiteral(((SyntaxTokenElement)operandNode.Children[0]).Token, operandType))
                {
                    var literal = ((SyntaxTokenElement)operandNode.Children[0]).Token;
                    if (expected != operandType)
                    {
                        Report("TRM3014", "An unsuffixed integer literal requires a signed integer context.", literal.Span);
                    }

                    expression = new BoundConstant(operandType == PrimitiveType.Int32 ? int.MinValue : long.MinValue,
                        operandType, node.Span);
                    break;
                }

                var operandContext = operandType == PrimitiveType.Bool ? operandType
                    : expected is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
                        || KnownType(operandNode) is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
                        ? operandType : (PrimitiveType?)null;
                var operand = BindExpression(operandNode, operandContext);
                if (operand.Type != operandType)
                {
                    Report("TRM3007", $"Unary operator requires {TypeName(operandType)}.", op.Span);
                }
                if (op.Kind is not (TokenKind.Plus or TokenKind.Minus or TokenKind.Tilde or TokenKind.Bang)
                    || (op.Kind == TokenKind.Minus && operandType is not (PrimitiveType.Int32 or PrimitiveType.Int64))
                    || (op.Kind is TokenKind.Plus or TokenKind.Tilde && operandType is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64)))
                {
                    Report("TRM3001", "Unsupported unary operator.", op.Span);
                }

                expression = new BoundUnary(op.Kind, operand, operandType, node.Span);
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
                if (!nominal && place.Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32
                    or PrimitiveType.Int64 or PrimitiveType.UInt64))
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
                    expression = new BoundUpdate(op.Kind, place, prefix, place.Type, node.Span);
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

        return _diagnostics.Count == diagnosticCount ? FoldScalarConstant(expression) : expression;
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
                operandType = leftKnown ?? rightKnown ?? (expected is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64 ? expected : null);
                resultType = operandType ?? PrimitiveType.Int32;
                if (operandType is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64))
                {
                    Report("TRM3007", "Arithmetic requires int32 or uint32 operands with a known context.", op.Span);
                }
                break;

            case TokenKind.ShiftLeft:
            case TokenKind.ShiftRight:
            case TokenKind.ShiftRightUnsigned:
                operandType = leftKnown ?? (expected is PrimitiveType.Int32 or PrimitiveType.UInt32
                    or PrimitiveType.Int64 or PrimitiveType.UInt64 ? expected : null);
                resultType = operandType ?? PrimitiveType.Int32;
                if (operandType is not (PrimitiveType.Int32 or PrimitiveType.UInt32
                    or PrimitiveType.Int64 or PrimitiveType.UInt64))
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
                if (operandType is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Char16))
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
                if (operandType is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Bool))
                {
                    Report("TRM3007", "Bitwise operands must both be int32 or both be bool.", op.Span);
                }
                break;

            case TokenKind.EqualsEquals:
            case TokenKind.BangEquals:
                operandType = leftKnown ?? rightKnown;
                resultType = PrimitiveType.Bool;
                if (operandType is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Char16 or PrimitiveType.Bool))
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
        var shift = op.Kind is TokenKind.ShiftLeft or TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned;
        var right = BindExpression(rightNode, shift ? PrimitiveType.Int32 : operandType);
        if (operandType is null || left.Type != operandType
            || right.Type != (shift ? PrimitiveType.Int32 : operandType) || operandType == PrimitiveType.Void)
        {
            Report("TRM3007", "Binary operands must have the same supported type.", op.Span);
        }

        return new BoundBinary(op.Kind, left, right, resultType, node.Span);
    }

    private BoundExpression BindCall(SyntaxNode node, PrimitiveType? expected)
    {
        var callee = (SyntaxNode)node.Children[0];
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
            if (type is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView)
            {
                var arguments = ((SyntaxNode)node.Children[1]).Children.OfType<SyntaxNode>().ToArray();
                if (member != "Read" || arguments.Length != 1 || arguments[0].Children is not [SyntaxNode index])
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
            "int32" => PrimitiveType.Int32,
            "uint32" => PrimitiveType.UInt32,
            "int64" => PrimitiveType.Int64,
            "uint64" => PrimitiveType.UInt64,
            "char16" => PrimitiveType.Char16,
            _ => PrimitiveType.Int32,
        };
        return name is "int32" or "uint32" or "int64" or "uint64" or "char16";
    }

    private BoundExpression BindConversionCall(SyntaxNode node, SyntaxNode arguments, PrimitiveType target)
    {
        var items = arguments.Children.OfType<SyntaxNode>().ToArray();
        if (items is not [{ Kind: SyntaxKind.Argument, Children: [SyntaxNode operandNode] }])
        {
            Report("TRM3011", "A checked conversion requires exactly one positional value argument.", arguments.Span);
            return ErrorExpression(node.Span);
        }
        var operand = BindExpression(operandNode, KnownType(operandNode) is null ? target : null);
        if (operand.Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64
            or PrimitiveType.UInt64 or PrimitiveType.Char16))
        {
            Report("TRM3007", "A checked conversion requires a numeric operand.", operand.Span);
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
                var outName = declares
                    ? ((SyntaxTokenElement)argument.Children[outputTargetIndex + 1]).Token
                    : argument.Children.Length == outputTargetIndex + 1
                        && argument.Children[outputTargetIndex] is SyntaxNode outExpression
                        && outExpression.Kind == SyntaxKind.NameExpression
                        ? ((SyntaxTokenElement)outExpression.Children[0]).Token : null;
                if (outName is null)
                {
                    Report("TRM3011", "Output argument must be a local variable.", argument.Span);
                    bound.Add(ErrorExpression(argument.Span));
                    continue;
                }
                var slot = declares ? AddVariable(outName.Name, expectedParameter.Type, false, outName.Span)
                    : TryFindVariable(outName.Name, out var existing) ? existing : -1;
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
            if (expectedParameter?.IsOut == true)
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
            bound.Add(BindExpression(value, expected));
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
        return owner is not null && TryFindFunction(owner + ".#get:" + name, out getter)
            && _signatures[getter].PropertyName == name;
    }

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
            if (expected != PrimitiveType.Char16)
            {
                Report("TRM3014", "A character literal requires a representable target type.", token.Span);
                return ErrorExpression(token.Span);
            }
            if (scalar > ushort.MaxValue)
            {
                Report("TRM3013", "Character literal is outside the char16 scalar range.", token.Span);
                return ErrorExpression(token.Span);
            }
            return new BoundConstant(scalar, PrimitiveType.Char16, token.Span);
        }

        if (token.Kind != TokenKind.NumericLiteral)
        {
            Report("TRM3001", "Only boolean and integer literals are supported.", token.Span);
            return ErrorExpression(token.Span);
        }

        var text = _tree.Source.GetSubText(token.Span);
        var digits = text.AsSpan();

        if (digits.IsEmpty || ScannerHost.IntegerDigitCount(digits) != digits.Length)
        {
            Report("TRM3013", "Floating-point literals and unsupported numeric tags are not supported.", token.Span);
            return ErrorExpression(token.Span);
        }

        var type = expected;
        if (type is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Char16))
        {
            Report("TRM3014", "An unsuffixed integer literal requires a numeric context.", token.Span);
        }

        var based = ScannerHost.IntegerRadix(digits) != 10;
        // Based literals construct the destination's bits; only nonzero bits beyond its width are out of range.
        ulong max = type switch {
            PrimitiveType.UInt32 => uint.MaxValue,
            PrimitiveType.Int32 when based => uint.MaxValue,
            PrimitiveType.Int64 when based => ulong.MaxValue,
            PrimitiveType.Int64 => long.MaxValue,
            PrimitiveType.UInt64 => ulong.MaxValue,
            PrimitiveType.Char16 => ushort.MaxValue,
            _ => int.MaxValue,
        };
        if (!TryIntegerMagnitude(token, out var parsed))
        {
            Report("TRM3013", "Integer literal is outside its destination range.", token.Span);
            return ErrorExpression(token.Span);
        }
        if (parsed > max)
        {
            Report("TRM3013", "Integer literal is outside its destination range.", token.Span);
            return ErrorExpression(token.Span);
        }

        var value = type switch {
            PrimitiveType.UInt64 => unchecked((long)parsed),
            PrimitiveType.Int64 => unchecked((long)parsed),
            _ => unchecked((int)parsed),
        };
        return new BoundConstant(value, type ?? PrimitiveType.Int32, token.Span);
    }

    private bool TryMinimumLiteral(SyntaxToken token, PrimitiveType type)
    {
        if (token.Kind != TokenKind.NumericLiteral)
        {
            return false;
        }

        var digits = _tree.Source.GetSubText(token.Span).AsSpan();
        if (digits.IsEmpty || ScannerHost.IntegerRadix(digits) != 10
            || ScannerHost.IntegerDigitCount(digits) != digits.Length)
        {
            return false;
        }

        var minimumMagnitude = type == PrimitiveType.Int32 ? 2147483648UL : 9223372036854775808UL;
        return TryIntegerMagnitude(token, out var parsed) && parsed == minimumMagnitude;
    }

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
            case SyntaxKind.ConstExpression:
                return KnownType((SyntaxNode)node.Children[1]);
            case SyntaxKind.VoidExpression:
                return PrimitiveType.Void;
            case SyntaxKind.NameExpression:
            {
                var name = ((SyntaxTokenElement)node.Children[0]).Token.Name;
                if (TryFindVariable(name, out var slot))
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
                return TryFindBuffer((SyntaxNode)node.Children[0], out var buffer)
                    ? ((SyntaxNode)node.Children[2]).Kind == SyntaxKind.SliceRangeExpression
                        ? null : _buffers[buffer].ElementType
                    : ((SyntaxNode)node.Children[2]).Kind == SyntaxKind.SliceRangeExpression
                        && KnownType((SyntaxNode)node.Children[0]) == PrimitiveType.OwnedInt32Buffer
                            ? PrimitiveType.ReadonlyInt32BufferView : null;
            case SyntaxKind.UnaryExpression:
            {
                var operand = KnownType((SyntaxNode)node.Children[1]);
                var operation = ((SyntaxTokenElement)node.Children[0]).Token.Kind;
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

    private string TypeName(PrimitiveType type) => type switch {
        PrimitiveType.Int32 => "int32",
        PrimitiveType.UInt32 => "uint32",
        PrimitiveType.Int64 => "int64",
        PrimitiveType.UInt64 => "uint64",
        PrimitiveType.Char16 => "char16",
        PrimitiveType.Bool => "bool",
        PrimitiveType.Void => "void",
        PrimitiveType.OwnedInt32Buffer => NativeBufferIntrinsics.TypeName,
        PrimitiveType.ReadonlyInt32BufferView => "ReadonlyInt32BufferView",
        PrimitiveType.MutableInt32BufferView => "MutableInt32BufferView",
        PrimitiveType.ManagedNode => "ManagedNode",
        PrimitiveType.WriteManagedNode => "write ManagedNode",
        PrimitiveType.OptionalManagedNode => "ManagedNode?",
        PrimitiveType.OptionalWriteManagedNode => "write ManagedNode?",
        _ when ManagedClassTypes.IsOptional(type) =>
            (ManagedClassTypes.IsWritable(type) ? "write " : "")
                + _classes[ManagedClassTypes.ClassIndex(type)].Name + "?",
        _ when IsClass(type) => (ManagedClassTypes.IsWritable(type) ? "write " : "")
            + _classes[ManagedClassTypes.ClassIndex(type)].Name,
        _ when IsStruct(type) => _structs[(int)type - 100].Name,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private void Report(string code, string message, TextSpan span)
    {
        var diagnostic = new Diagnostic(code, DiagnosticSeverity.Error, message, span);
        _diagnostics.Add(diagnostic);
        _sourceDiagnostics.Add(new SourceDiagnostic(_tree, diagnostic));
    }

    private sealed record Parameter(string Name, PrimitiveType Type, TextSpan Span, bool IsOut, bool IsInit = false);

    private sealed record Signature(string Name, PrimitiveType ReturnType, ImmutableArray<Parameter> Parameters,
        SyntaxNode? Body, SyntaxNode? Declaration, SyntaxTree Tree, string? StructName, bool IsConstructor, bool IsPrivate,
        bool IsInternal, bool IsMutable, bool IsStatic, bool IsNoThrow, bool DoesNotReturn, BoundFunction? External = null)
    {
        internal string? PropertyName { get; init; }
        internal bool IsCleanup { get; init; }
        internal bool RequiresUnsafeCaller { get; init; }
        internal bool IsOperator { get; init; }
        internal GenericInstantiation? Generic { get; init; }
        internal bool IsClassConstructor { get; init; }
        internal int ClassConstructor { get; init; } = -1;
        internal bool IsConstEval { get; init; }
    }
}
