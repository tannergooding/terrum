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
    private void BindSourceNativeComMetadata()
    {
        var sourceInterfaces = new Dictionary<string, SourceNativeComInterface>(StringComparer.Ordinal);
        var sourceInterfaceIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in _trees)
        {
            SetContext(tree);
            foreach (var declaration in tree.Root.DescendantNodes().OfType<SyntaxNode>()
                .Where(static node => node.Kind == SyntaxKind.InterfaceDeclaration
                    && node.Children.OfType<SyntaxNode>().Any(static child =>
                        child.Kind == SyntaxKind.Attribute
                        && AttributeName(child) == "Terrum.Interop.NativeComInterface")))
            {
                var name = InterfaceName(declaration);
                if (name is not null)
                {
                    _ = sourceInterfaceIdentities.Add(Qualify(name));
                }
            }
        }
        var sourceOwners = new Dictionary<string, SourceNativeComOwner>(StringComparer.Ordinal);
        foreach (var tree in _trees)
        {
            SetContext(tree);
            foreach (var declaration in tree.Root.DescendantNodes().OfType<SyntaxNode>()
                .Where(static node => node.Kind == SyntaxKind.StructDeclaration))
            {
                var attributes = declaration.Children.OfType<SyntaxNode>()
                    .Where(static child => child.Kind == SyntaxKind.Attribute
                        && AttributeName(child) == "Terrum.Interop.NativeComOwner").ToArray();
                if (attributes.Length == 0)
                {
                    continue;
                }
                if (attributes.Length != 1
                    || !TryDecodeNativeComOwnerAttribute(attributes[0], out var interfaceName))
                {
                    Report("TRM3030", "NativeComOwner requires one interface identity string literal.",
                        declaration.Span);
                    continue;
                }
                var ownerName = Qualify(StructName(declaration).Name);
                var interfaceIdentity = ResolveComTypeIdentity(interfaceName, sourceInterfaceIdentities);
                if ((sourceInterfaceIdentities.Contains(interfaceIdentity)
                        || _nativeImportCatalog?.TryGetComInterface(interfaceIdentity, out _) == true)
                    && _nativeImportCatalog?.TryGetComOwner(ownerName, out _) != true
                    && sourceOwners.TryAdd(ownerName, new(interfaceIdentity, declaration.Span)))
                {
                    continue;
                }
                Report("TRM3030",
                    $"NativeComOwner '{ownerName}' has a duplicate declaration or unknown interface '{interfaceIdentity}'.",
                    declaration.Span);
            }
        }
        var ownedInterfaceIdentities = sourceOwners.Values
            .Select(static owner => owner.InterfaceIdentity).ToHashSet(StringComparer.Ordinal);
        if (_nativeImportCatalog is NativeImportCatalog existingCatalog)
        {
            ownedInterfaceIdentities.UnionWith(existingCatalog.ComOwners
                .Select(static owner => owner.InterfaceIdentity));
        }
        var hasNativeComMethodAttribute = false;
        foreach (var tree in _trees)
        {
            SetContext(tree);
            foreach (var declaration in tree.Root.DescendantNodes().OfType<SyntaxNode>()
                .Where(static node => node.Kind == SyntaxKind.InterfaceDeclaration))
            {
                var attributes = declaration.Children.OfType<SyntaxNode>()
                    .Where(static child => child.Kind == SyntaxKind.Attribute).ToArray();
                var interfaceAttributes = attributes.Where(static attribute =>
                    AttributeName(attribute) == "Terrum.Interop.NativeComInterface").ToArray();
                foreach (var member in declaration.Children.OfType<SyntaxNode>()
                    .Where(static child => child.Kind == SyntaxKind.FunctionDeclaration))
                {
                    hasNativeComMethodAttribute |= member.Children.OfType<SyntaxNode>()
                        .Any(static child => child.Kind == SyntaxKind.Attribute
                            && AttributeName(child) == "Terrum.Interop.NativeComMethod");
                }
                if (interfaceAttributes.Length == 0)
                {
                    continue;
                }
                if (interfaceAttributes.Length != 1
                    || !TryDecodeNativeComInterfaceAttribute(interfaceAttributes[0], out var iid))
                {
                    Report("TRM3030",
                        "NativeComInterface requires one valid IID string literal.",
                        declaration.Span);
                    continue;
                }
                var name = InterfaceName(declaration);
                if (string.IsNullOrWhiteSpace(name)
                    || declaration.Children.OfType<SyntaxNode>()
                        .Any(static child => child.Kind == SyntaxKind.TypeParameterList))
                {
                    Report("TRM3030",
                        "Native COM interfaces require a non-generic interface declaration.",
                        declaration.Span);
                    continue;
                }

                var identity = Qualify(name);
                var baseTypes = declaration.Children.OfType<SyntaxNode>()
                    .Where(static child => child.Kind == SyntaxKind.ConformanceList)
                    .SelectMany(static child => child.Children.OfType<SyntaxNode>()
                        .Where(static type => type.Kind == SyntaxKind.TypeName))
                    .ToArray();
                if (baseTypes.Length > 1)
                {
                    Report("TRM3030",
                        "A native COM interface can declare only one immediate base interface.",
                        declaration.Span);
                    continue;
                }
                var baseIdentity = baseTypes.Length == 0 ? null
                    : ResolveComTypeIdentity(NameText(baseTypes[0]), sourceInterfaceIdentities);
                var declaredMethods = ImmutableArray.CreateBuilder<NativeComMethodDefinition>();
                var methodDeclarations = declaration.Children.OfType<SyntaxNode>()
                    .Where(static child => child.Kind == SyntaxKind.FunctionDeclaration).ToArray();
                foreach (var attribute in attributes)
                {
                    if (AttributeName(attribute) != "Terrum.Interop.NativeComInterface")
                    {
                        Report("TRM3030",
                            "Native COM interfaces support only the NativeComInterface attribute.",
                            attribute.Span);
                    }
                }
                foreach (var member in declaration.Children.OfType<SyntaxNode>()
                    .Where(static child => child.Kind != SyntaxKind.Attribute
                        && child.Kind is not (SyntaxKind.FunctionDeclaration
                            or SyntaxKind.TypeParameterList or SyntaxKind.ConformanceList)))
                {
                    Report("TRM3030",
                        "Native COM interfaces currently support only attributed method declarations.",
                        member.Span);
                }
                for (var index = 0; index < methodDeclarations.Length; index++)
                {
                    var methodDeclaration = methodDeclarations[index];
                    var methodAttributes = methodDeclaration.Children.OfType<SyntaxNode>()
                        .Where(static child => child.Kind == SyntaxKind.Attribute).ToArray();
                    var methodContracts = methodAttributes.Where(static attribute =>
                        AttributeName(attribute) == "Terrum.Interop.NativeComMethod").ToArray();
                    var outputAttributes = methodAttributes.Where(static attribute =>
                        AttributeName(attribute) == "Terrum.Interop.NativeOutput").ToArray();
                    var nativeParameters = ImmutableArray.CreateBuilder<NativeParameterDefinition>();
                    var nativeParameterIndexes = new HashSet<int>();
                    foreach (var attribute in methodAttributes)
                    {
                        var attributeName = AttributeName(attribute);
                        if (attributeName == "Terrum.Interop.NativeParameter")
                        {
                            if (!TryDecodeNativeParameterAttribute(attribute, out var nativeParameter)
                                || !nativeParameterIndexes.Add(nativeParameter.ParameterIndex))
                            {
                                Report("TRM3030",
                                    "NativeParameter requires a unique ordinal, access, retention, and nonnegative extent.",
                                    attribute.Span);
                            }
                            else
                            {
                                nativeParameters.Add(nativeParameter);
                            }
                        }
                        else if (attributeName == "Terrum.Interop.NativeOutput")
                        {
                            if (!TryDecodeNativeOutputAttribute(attribute, out _))
                            {
                                Report("TRM3030",
                                    "NativeOutput requires one nonnegative parameter ordinal.",
                                    attribute.Span);
                            }
                        }
                        else if (attributeName is not ("Terrum.Interop.NativeComMethod" or "NoThrow"))
                        {
                            Report("TRM3030",
                                "Native COM methods support only NativeComMethod, NativeParameter, NativeOutput, and NoThrow attributes.",
                                attribute.Span);
                        }
                    }
                    if (methodContracts.Length != 1
                        || !TryDecodeNativeComMethodAttribute(methodContracts[0],
                            out var slot, out var effects))
                    {
                        Report("TRM3030",
                            "Every native COM method requires one NativeComMethod attribute with a slot.",
                            methodDeclaration.Span);
                        continue;
                    }
                    if (outputAttributes.Length > 1)
                    {
                        Report("TRM3030", "A native COM method can declare only one NativeOutput.",
                            methodDeclaration.Span);
                    }
                    if (!methodDeclaration.Children.OfType<SyntaxTokenElement>()
                        .Any(static element => element.Token.Kind == TokenKind.UnsafeKeyword))
                    {
                        Report("TRM3030",
                            "A native COM method declaration must be marked unsafe.",
                            methodDeclaration.Span);
                    }
                    if (methodDeclaration.Children[^1] is not SyntaxNode { Kind: SyntaxKind.ExternalBody }
                        || methodDeclaration.Children.OfType<SyntaxNode>()
                            .Any(static child => child.Kind is SyntaxKind.TypeParameterList
                                or SyntaxKind.ConstraintClause))
                    {
                        Report("TRM3030",
                            "Native COM methods must be non-generic bodyless declarations.",
                            methodDeclaration.Span);
                        continue;
                    }

                    var returnNode = methodDeclaration.Children.OfType<SyntaxNode>()
                        .FirstOrDefault(static child => child.Kind == SyntaxKind.TypeName);
                    var parameters = methodDeclaration.Children.OfType<SyntaxNode>()
                        .FirstOrDefault(static child => child.Kind == SyntaxKind.ParameterList);
                    var methodName = methodDeclaration.Children.OfType<SyntaxTokenElement>()
                        .SkipWhile(static element => element.Token.Kind != TokenKind.Identifier)
                        .Select(static element => element.Token)
                        .FirstOrDefault(static token => token.Kind == TokenKind.Identifier);
                    if (returnNode is null || parameters is null || methodName is null)
                    {
                        Report("TRM3030", "A native COM method has an incomplete signature.",
                            methodDeclaration.Span);
                        continue;
                    }
                    var parameterTypes = ImmutableArray.CreateBuilder<string>();
                    parameterTypes.Add(identity + "*");
                    var validParameters = true;
                    foreach (var parameter in parameters.Children.OfType<SyntaxNode>()
                        .Where(static child => child.Kind == SyntaxKind.Parameter))
                    {
                        var typeNode = parameter.Children.OfType<SyntaxNode>()
                            .FirstOrDefault(static child => child.Kind == SyntaxKind.TypeName);
                        if (typeNode is null
                            || !TryGetComSignatureType(typeNode, sourceInterfaceIdentities, out var typeIdentity))
                        {
                            validParameters = false;
                            break;
                        }
                        parameterTypes.Add(typeIdentity);
                    }
                    if (!validParameters
                        || !TryGetComSignatureType(returnNode, sourceInterfaceIdentities, out var returnIdentity))
                    {
                        Report("TRM3030",
                            "Native COM method parameters and results must have supported direct ABI types.",
                            methodDeclaration.Span);
                        continue;
                    }

                    var vtableIdentity = identity + "::" + methodName.Name;
                    var signature = returnIdentity + "(" + string.Join(",", parameterTypes) + ")";
                    var signatureTypes = _nativeImportCatalog?.ComSignatureTypes ?? [];
                    if (!NativeComCatalog.TryCreateAbi(signature, signatureTypes, out var abi))
                    {
                        Report("TRM3030",
                            "The native COM method signature has no supported Windows x64 ABI.",
                            methodDeclaration.Span);
                        continue;
                    }
                    var outputParameterIndex = -1;
                    string? outputInterfaceIdentity = null;
                    if (outputAttributes.Length == 1
                        && TryDecodeNativeOutputAttribute(outputAttributes[0], out outputParameterIndex))
                    {
                        var outputParameter = nativeParameters.FirstOrDefault(parameter =>
                            parameter.ParameterIndex == outputParameterIndex);
                        if (outputParameterIndex <= 0 || outputParameterIndex >= parameterTypes.Count
                            || !effects.HasFlag(NativeComEffects.SignedHResult)
                            || !effects.HasFlag(NativeComEffects.AcquiresReference)
                            || !parameterTypes[outputParameterIndex].EndsWith("**", StringComparison.Ordinal)
                            || outputParameter is null
                            || outputParameter.Access is not (NativeParameterAccess.Write
                                or NativeParameterAccess.ReadWrite)
                            || outputParameter.Retention != NativeParameterRetention.NoEscape)
                        {
                            Report("TRM3030",
                                "A NativeOutput on a COM method must identify a typed interface double-pointer with HRESULT acquisition effects.",
                                methodDeclaration.Span);
                            continue;
                        }
                        outputInterfaceIdentity = parameterTypes[outputParameterIndex][..^2];
                    }
                    var hasNoThrow = false;
                    foreach (var noThrowAttribute in methodAttributes.Where(static attribute =>
                        AttributeName(attribute) == "NoThrow"))
                    {
                        if (noThrowAttribute.Children is not [
                            SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket },
                            SyntaxTokenElement { Token.Name: "NoThrow" },
                            SyntaxTokenElement { Token.Kind: TokenKind.CloseBracket }])
                        {
                            Report("TRM3030", "NoThrow on a native COM method is parameterless.",
                                noThrowAttribute.Span);
                        }
                        hasNoThrow = true;
                    }
                    var borrowedOwnerParameters = nativeParameters
                        .Where(parameter => parameter.Access == NativeParameterAccess.Read
                            && parameter.Retention == NativeParameterRetention.NoEscape
                            && parameter.Extent == 1
                            && parameter.ParameterIndex > 0
                            && parameter.ParameterIndex < parameterTypes.Count
                            && parameterTypes[parameter.ParameterIndex]
                                .EndsWith("*", StringComparison.Ordinal)
                            && !parameterTypes[parameter.ParameterIndex]
                                .EndsWith("**", StringComparison.Ordinal)
                            && ownedInterfaceIdentities.Contains(
                                parameterTypes[parameter.ParameterIndex][..^1].Trim()))
                        .Select(static parameter => parameter.ParameterIndex)
                        .ToImmutableArray();
                    var method = new NativeComMethodDefinition(vtableIdentity, identity, identity,
                        slot, signature, abi, effects, IsCallable: true,
                        OutputParameterIndex: outputParameterIndex,
                        OutputInterfaceIdentity: outputInterfaceIdentity) {
                        VTableMethodIdentity = vtableIdentity,
                        BoundMethodName = methodName.Name,
                        IsNoThrow = hasNoThrow,
                        RequiresNativeParameterMetadata = true,
                        NativeParameters = nativeParameters.ToImmutable(),
                        BorrowedOwnerParameters = borrowedOwnerParameters,
                    };
                    declaredMethods.Add(method);
                }

                var orderedMethods = declaredMethods.OrderBy(static method => method.Slot).ToImmutableArray();
                var declaredMethodIdentities = orderedMethods
                    .Select(static method => method.VTableMethodIdentity).ToImmutableArray();
                if (!sourceInterfaces.TryAdd(identity, new(iid, baseIdentity,
                        declaredMethodIdentities, orderedMethods)))
                {
                    Report("TRM3003", $"Duplicate native COM interface '{name}'.", declaration.Span);
                }
            }
        }

        if (hasNativeComMethodAttribute && sourceInterfaces.Count == 0)
        {
            Report("TRM3030",
                "NativeComMethod may appear only inside a NativeComInterface declaration.",
                _trees[0].Root.Span);
        }
        foreach (var (identity, definition) in sourceInterfaces)
        {
            var inheritedSlotCount = GetBaseVTableSlotCount(definition.BaseIdentity, sourceInterfaces);
            var declaredSlots = new HashSet<int>();
            foreach (var method in definition.Methods)
            {
                if (method.Slot < inheritedSlotCount
                    || method.Slot > int.MaxValue / NativeTargetProfile.PointerSize
                    || !declaredSlots.Add(method.Slot))
                {
                    Report("TRM3030",
                        "Native COM method slots must be unique and outside the inherited vtable extent.",
                        _trees[0].Root.Span);
                }
            }
            if (_nativeImportCatalog?.ComInterfaces.Any(existing =>
                existing.NominalIdentity == identity) == true)
            {
                Report("TRM3003", $"Native COM interface '{identity}' is already defined by the selected catalog.",
                    _trees[0].Root.Span);
            }
        }
        if (sourceInterfaces.Count == 0 && sourceOwners.Count == 0 || DiagnosticCount != 0)
        {
            return;
        }

        var existingInterfaces = _nativeImportCatalog?.ComInterfaces ?? [];
        var interfacesByIdentity = existingInterfaces.ToDictionary(
            static definition => definition.NominalIdentity, StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var interfaces = ImmutableArray.CreateBuilder<NativeComInterfaceDefinition>(
            existingInterfaces.Length + sourceInterfaces.Count);
        interfaces.AddRange(existingInterfaces);

        NativeComInterfaceDefinition? BindInterface(string identity)
        {
            if (interfacesByIdentity.TryGetValue(identity, out var existing))
            {
                return existing;
            }
            if (!sourceInterfaces.TryGetValue(identity, out var source) || !visiting.Add(identity))
            {
                return null;
            }
            NativeComInterfaceDefinition? baseDefinition = null;
            if (source.BaseIdentity is string baseIdentity)
            {
                baseDefinition = BindInterface(baseIdentity);
                if (baseDefinition is null)
                {
                    return null;
                }
            }
            var flattened = (baseDefinition?.FlattenedMethods ?? []).AddRange(source.DeclaredMethodIdentities);
            var flattenedSlots = (baseDefinition?.GetFlattenedMethodSlots() ?? [])
                .AddRange(source.Methods.Select(static method => method.Slot));
            var definition = new NativeComInterfaceDefinition(identity, source.Iid,
                source.BaseIdentity, flattened, source.DeclaredMethodIdentities) {
                FlattenedMethodSlots = flattenedSlots,
            };
            visiting.Remove(identity);
            interfacesByIdentity.Add(identity, definition);
            interfaces.Add(definition);
            return definition;
        }

        foreach (var identity in sourceInterfaces.Keys)
        {
            if (BindInterface(identity) is null)
            {
                Report("TRM3030",
                    $"Native COM interface '{identity}' has an unknown base or cyclic inheritance.",
                    _trees[0].Root.Span);
                return;
            }
        }

        var methods = ImmutableArray.CreateBuilder<NativeComMethodDefinition>(
            (_nativeImportCatalog?.ComMethods.Length ?? 0)
                + sourceInterfaces.Values.Sum(static definition => definition.Methods.Length));
        methods.AddRange(_nativeImportCatalog?.ComMethods ?? []);
        foreach (var definition in sourceInterfaces.Values)
        {
            methods.AddRange(definition.Methods);
        }
        var baseEdges = ImmutableArray.CreateBuilder<(string Source, string Target)>(
            (_nativeImportCatalog?.ComBaseEdges.Length ?? 0) + sourceInterfaces.Count);
        baseEdges.AddRange(_nativeImportCatalog?.ComBaseEdges ?? []);
        foreach (var (identity, definition) in sourceInterfaces)
        {
            if (definition.BaseIdentity is string baseIdentity)
            {
                baseEdges.Add((identity, baseIdentity));
            }
        }

        var sourceAcquisitions = ImmutableArray.CreateBuilder<NativeComAcquisitionSourceDefinition>();
        foreach (var tree in _trees)
        {
            SetContext(tree);
            foreach (var declaration in tree.Root.DescendantNodes().OfType<SyntaxNode>()
                .Where(static node => node.Kind == SyntaxKind.FunctionDeclaration))
            {
                foreach (var attribute in declaration.Children.OfType<SyntaxNode>()
                    .Where(static child => child.Kind == SyntaxKind.Attribute
                        && AttributeName(child) == "Terrum.Interop.NativeComAcquisition"))
                {
                    if (TryDecodeNativeComAcquisitionAttribute(attribute, out _, out var source)
                        && source is not null)
                    {
                        sourceAcquisitions.Add(source with {
                            OwnerTypeIdentity = ResolveNativeComOwnerIdentity(source.OwnerTypeIdentity),
                            SourceMethodIdentity = ResolveNativeComAcquisitionIdentity(source.SourceMethodIdentity),
                        });
                    }
                }
            }
        }

        var ownerDefinitions = ImmutableArray.CreateBuilder<NativeComOwnerDefinition>(
            (_nativeImportCatalog?.ComOwners.Length ?? 0) + sourceOwners.Count);
        var sourceOwnerInterfaces = sourceOwners.Values
            .Select(static owner => owner.InterfaceIdentity).ToHashSet(StringComparer.Ordinal);
        ownerDefinitions.AddRange(_nativeImportCatalog?.ComOwners
            .Where(owner => !sourceOwnerInterfaces.Contains(owner.InterfaceIdentity)) ?? []);
        var combinedMethods = methods.ToImmutable();
        foreach (var (ownerName, sourceOwner) in sourceOwners)
        {
            var acquisitions = sourceAcquisitions.Where(acquisition =>
                acquisition.OwnerTypeIdentity == ownerName).ToArray();
            if (acquisitions.Length == 0
                || acquisitions.Any(acquisition =>
                    acquisition.SourceMethodIdentity != acquisitions[0].SourceMethodIdentity
                    || acquisition.SourceOutputParameterIndex != acquisitions[0].SourceOutputParameterIndex
                    || acquisition.OutputMayBeWrittenOnFailure
                        != acquisitions[0].OutputMayBeWrittenOnFailure))
            {
                Report("TRM3030",
                    $"NativeComOwner '{ownerName}' requires one consistent NativeComAcquisition source.",
                    sourceOwner.Span);
                continue;
            }

            var acquisition = acquisitions[0];
            var isComMethod = acquisition.SourceMethodIdentity.Contains("::", StringComparison.Ordinal);
            var acquisitionMethod = isComMethod
                ? combinedMethods.FirstOrDefault(method => method.Identity == acquisition.SourceMethodIdentity)
                : null;
            if (isComMethod
                && (acquisitionMethod is null
                    || acquisitionMethod.OutputParameterIndex != acquisition.SourceOutputParameterIndex
                    || acquisitionMethod.OutputInterfaceIdentity != sourceOwner.InterfaceIdentity
                    || !acquisitionMethod.Effects.HasFlag(NativeComEffects.AcquiresReference)
                    || acquisitionMethod.Effects.HasFlag(NativeComEffects.OutputMayBeWrittenOnFailure)
                        != acquisition.OutputMayBeWrittenOnFailure))
            {
                Report("TRM3030",
                    $"NativeComAcquisition for '{ownerName}' does not match its COM method output contract.",
                    sourceOwner.Span);
                continue;
            }

            var releaseMethod = combinedMethods
                .Where(method => method.Effects == NativeComEffects.ReleasesReference
                    && NativeComCatalog.IsAssignable(sourceOwner.InterfaceIdentity,
                        method.OwnerInterfaceIdentity, interfacesByIdentity))
                .OrderBy(method => method.OwnerInterfaceIdentity == sourceOwner.InterfaceIdentity ? 0 : 1)
                .FirstOrDefault();
            if (releaseMethod is null)
            {
                Report("TRM3030",
                    $"NativeComOwner '{ownerName}' has no declared no-throw release method.",
                    sourceOwner.Span);
                continue;
            }

            ownerDefinitions.Add(new NativeComOwnerDefinition(
                ownerName, sourceOwner.InterfaceIdentity,
                isComMethod ? NativeComOwnerAcquisitionKind.ComMethodOutput
                    : NativeComOwnerAcquisitionKind.DirectNativeImportOutput,
                acquisition.SourceMethodIdentity, acquisition.SourceOutputParameterIndex,
                IsUnique: true, CellSize: sizeof(long), CellAlignment: sizeof(long)) {
                ReleaseMethodIdentity = releaseMethod.Identity,
                OutputMayBeWrittenOnFailure = acquisition.OutputMayBeWrittenOnFailure,
            });
        }

        try
        {
            _nativeImportCatalog = new NativeImportCatalog(
                _nativeImportCatalog?.Identity ?? "source-native-com.v1",
                _nativeImportCatalog?.ImportDefinitions ?? [],
                _nativeImportCatalog?.IsForTesting ?? false,
                interfaces.MoveToImmutable(), ownerDefinitions.ToImmutable(), methods.MoveToImmutable(), baseEdges.ToImmutable(),
                _nativeImportCatalog?.ComSignatureTypes ?? [],
                _nativeImportCatalog?.FixedArrayStorageLayouts ?? [],
                _nativeImportCatalog?.StorageRecordLayouts ?? [],
                _nativeImportCatalog?.AggregateStorageLayouts ?? []);
            _usesNativeImportCatalog = true;
        }
        catch (ArgumentException exception)
        {
            Report("TRM3030", exception.Message, _trees[0].Root.Span);
        }
    }

    private string? InterfaceName(SyntaxNode declaration)
    {
        var nameToken = declaration.Children.OfType<SyntaxTokenElement>()
            .SkipWhile(static element => element.Token.Kind != TokenKind.InterfaceKeyword)
            .Skip(1)
            .Select(static element => element.Token)
            .FirstOrDefault(static token => token.Kind == TokenKind.Identifier);
        return nameToken?.Name;
    }

    private string ResolveComTypeIdentity(string identity, IReadOnlySet<string> sourceInterfaceIdentities)
    {
        if (identity is "void" or "int32" or "HRESULT" or "uint32" or "UINT" or "ULONG"
            or "int64" or "uint64" or "SIZE_T" or "float" or "FLOAT" or "double" or "DOUBLE"
            or "LPVOID" or "REFIID")
        {
            return identity;
        }
        if (identity == "float32")
        {
            return "float";
        }
        if (identity == "float64")
        {
            return "double";
        }
        if (identity.Contains('.', StringComparison.Ordinal))
        {
            return identity;
        }
        var qualified = Qualify(identity);
        if (sourceInterfaceIdentities.Contains(qualified)
            || _nativeImportCatalog?.TryGetComInterface(qualified, out _) == true)
        {
            return qualified;
        }
        if (_nativeImportCatalog?.TryGetComInterface(identity, out _) == true)
        {
            return identity;
        }
        foreach (var importedNamespace in _usings)
        {
            var imported = importedNamespace + "." + identity;
            if (sourceInterfaceIdentities.Contains(imported)
                || _nativeImportCatalog?.TryGetComInterface(imported, out _) == true)
            {
                return imported;
            }
        }
        return qualified;
    }

    private bool TryGetComSignatureType(SyntaxNode typeNode,
        IReadOnlySet<string> sourceInterfaceIdentities, out string identity)
    {
        if (TypeNameIdentifier(typeNode) == "ptr"
            && typeNode.Children.OfType<SyntaxNode>().ToArray() is [var pointee])
        {
            if (!TryGetComSignatureType(pointee, sourceInterfaceIdentities, out var pointeeIdentity))
            {
                identity = "";
                return false;
            }
            identity = pointeeIdentity + "*";
            return true;
        }

        var name = NameText(typeNode);
        if (name.Length == 0 || name.Contains('<', StringComparison.Ordinal))
        {
            identity = "";
            return false;
        }
        identity = ResolveComTypeIdentity(name, sourceInterfaceIdentities);
        return true;
    }

    private int GetBaseVTableSlotCount(string? identity,
        IReadOnlyDictionary<string, SourceNativeComInterface> sourceInterfaces,
        HashSet<string>? visiting = null)
    {
        if (identity is null)
        {
            return 0;
        }
        visiting ??= new(StringComparer.Ordinal);
        if (!visiting.Add(identity))
        {
            return 0;
        }
        int slotCount;
        if (sourceInterfaces.TryGetValue(identity, out var source))
        {
            var inheritedSlotCount = GetBaseVTableSlotCount(source.BaseIdentity, sourceInterfaces, visiting);
            var highestDeclaredSlot = source.Methods.IsEmpty
                ? -1 : source.Methods.Max(static method => method.Slot);
            var declaredSlotCount = highestDeclaredSlot == int.MaxValue
                ? int.MaxValue : highestDeclaredSlot + 1;
            slotCount = Math.Max(inheritedSlotCount, declaredSlotCount);
        }
        else
        {
            slotCount = _nativeImportCatalog?.ComInterfaces.FirstOrDefault(definition =>
                definition.NominalIdentity == identity)?.EffectiveVTableSlotCount ?? 0;
        }
        visiting.Remove(identity);
        return slotCount;
    }

    private static bool TryDecodeNativeComInterfaceAttribute(SyntaxNode attribute, out Guid iid)
    {
        iid = Guid.Empty;
        return attribute.Children is [
            SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket },
            SyntaxTokenElement { Token.Name: "Terrum" },
            SyntaxTokenElement { Token.Kind: TokenKind.Dot },
            SyntaxTokenElement { Token.Name: "Interop" },
            SyntaxTokenElement { Token.Kind: TokenKind.Dot },
            SyntaxTokenElement { Token.Name: "NativeComInterface" },
            SyntaxTokenElement { Token.Kind: TokenKind.OpenParen },
            SyntaxTokenElement iidToken,
            SyntaxTokenElement { Token.Kind: TokenKind.CloseParen },
            SyntaxTokenElement { Token.Kind: TokenKind.CloseBracket }]
            && iidToken.Token.Kind == TokenKind.StringLiteral
            && StringLiteralDecoder.TryDecode(iidToken.Token.Text.AsSpan(), out var iidText)
            && Guid.TryParse(iidText, out iid)
            && iid != Guid.Empty;
    }

    private static bool TryDecodeNativeComOwnerAttribute(SyntaxNode attribute, out string interfaceIdentity)
    {
        interfaceIdentity = "";
        return attribute.Children is [
            SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket },
            SyntaxTokenElement { Token.Name: "Terrum" },
            SyntaxTokenElement { Token.Kind: TokenKind.Dot },
            SyntaxTokenElement { Token.Name: "Interop" },
            SyntaxTokenElement { Token.Kind: TokenKind.Dot },
            SyntaxTokenElement { Token.Name: "NativeComOwner" },
            SyntaxTokenElement { Token.Kind: TokenKind.OpenParen },
            SyntaxTokenElement interfaceToken,
            SyntaxTokenElement { Token.Kind: TokenKind.CloseParen },
            SyntaxTokenElement { Token.Kind: TokenKind.CloseBracket }]
            && interfaceToken.Token.Kind == TokenKind.StringLiteral
            && StringLiteralDecoder.TryDecode(interfaceToken.Token.Text.AsSpan(), out interfaceIdentity)
            && !string.IsNullOrWhiteSpace(interfaceIdentity);
    }

    private static bool TryDecodeNativeComMethodAttribute(SyntaxNode attribute,
        out int slot, out NativeComEffects effects)
    {
        slot = -1;
        effects = NativeComEffects.None;
        if (attribute.Children.Length < 10
            || attribute.Children[0] is not SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket }
            || attribute.Children[1] is not SyntaxTokenElement { Token.Name: "Terrum" }
            || attribute.Children[2] is not SyntaxTokenElement { Token.Kind: TokenKind.Dot }
            || attribute.Children[3] is not SyntaxTokenElement { Token.Name: "Interop" }
            || attribute.Children[4] is not SyntaxTokenElement { Token.Kind: TokenKind.Dot }
            || attribute.Children[5] is not SyntaxTokenElement { Token.Name: "NativeComMethod" }
            || attribute.Children[6] is not SyntaxTokenElement { Token.Kind: TokenKind.OpenParen }
            || attribute.Children[7] is not SyntaxTokenElement slotToken
            || slotToken.Token.Kind != TokenKind.NumericLiteral
            || !ScannerHost.TryParseIntegerMagnitude(slotToken.Token.Text.AsSpan(), out var slotValue)
            || slotValue > int.MaxValue
            || attribute.Children[^2] is not SyntaxTokenElement { Token.Kind: TokenKind.CloseParen }
            || attribute.Children[^1] is not SyntaxTokenElement { Token.Kind: TokenKind.CloseBracket })
        {
            return false;
        }

        var previous = 8;
        while (previous < attribute.Children.Length - 2)
        {
            if (attribute.Children[previous] is not SyntaxTokenElement { Token.Kind: TokenKind.Comma }
                || previous + 1 >= attribute.Children.Length - 2
                || attribute.Children[previous + 1] is not SyntaxTokenElement effectToken
                || effectToken.Token.Kind != TokenKind.Identifier
                || !TryParseNativeComEffect(effectToken.Token.Name, out var effect))
            {
                return false;
            }
            if (effects.HasFlag(effect) || effect == NativeComEffects.None)
            {
                return false;
            }
            effects |= effect;
            previous += 2;
        }

        slot = (int)slotValue;
        return true;
    }

    private static bool TryDecodeNativeParameterAttribute(SyntaxNode attribute,
        out NativeParameterDefinition parameter)
    {
        parameter = new(-1, default, default, -1);
        var children = attribute.Children;
        if (children.Length != 16
            || children[0] is not SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket }
            || children[1] is not SyntaxTokenElement { Token.Name: "Terrum" }
            || children[2] is not SyntaxTokenElement { Token.Kind: TokenKind.Dot }
            || children[3] is not SyntaxTokenElement { Token.Name: "Interop" }
            || children[4] is not SyntaxTokenElement { Token.Kind: TokenKind.Dot }
            || children[5] is not SyntaxTokenElement { Token.Name: "NativeParameter" }
            || children[6] is not SyntaxTokenElement { Token.Kind: TokenKind.OpenParen }
            || children[7] is not SyntaxTokenElement ordinalToken
            || ordinalToken.Token.Kind != TokenKind.NumericLiteral
            || !ScannerHost.TryParseIntegerMagnitude(ordinalToken.Token.Text.AsSpan(), out var ordinal)
            || ordinal is 0 or > int.MaxValue
            || children[8] is not SyntaxTokenElement { Token.Kind: TokenKind.Comma }
            || children[9] is not SyntaxTokenElement accessToken
            || children[9] is not SyntaxTokenElement { Token.Kind: TokenKind.Identifier }
            || children[10] is not SyntaxTokenElement { Token.Kind: TokenKind.Comma }
            || children[11] is not SyntaxTokenElement retentionToken
            || children[11] is not SyntaxTokenElement { Token.Kind: TokenKind.Identifier }
            || children[12] is not SyntaxTokenElement { Token.Kind: TokenKind.Comma }
            || children[13] is not SyntaxTokenElement extentToken
            || extentToken.Token.Kind != TokenKind.NumericLiteral
            || !ScannerHost.TryParseIntegerMagnitude(extentToken.Token.Text.AsSpan(), out var extent)
            || extent > int.MaxValue
            || children[14] is not SyntaxTokenElement { Token.Kind: TokenKind.CloseParen }
            || children[15] is not SyntaxTokenElement { Token.Kind: TokenKind.CloseBracket })
        {
            return false;
        }

        var access = accessToken.Token.Name switch {
            "Read" => NativeParameterAccess.Read,
            "Write" => NativeParameterAccess.Write,
            "ReadWrite" => NativeParameterAccess.ReadWrite,
            _ => (NativeParameterAccess)byte.MaxValue,
        };
        var retention = retentionToken.Token.Name switch {
            "NoEscape" => NativeParameterRetention.NoEscape,
            "MayEscape" => NativeParameterRetention.MayEscape,
            _ => (NativeParameterRetention)byte.MaxValue,
        };
        if ((byte)access == byte.MaxValue || (byte)retention == byte.MaxValue)
        {
            return false;
        }

        parameter = new((int)ordinal, access, retention, (int)extent);
        return true;
    }

    private static bool TryParseNativeComEffect(string name, out NativeComEffects effect)
    {
        effect = name switch {
            "SignedHResult" => NativeComEffects.SignedHResult,
            "AcquiresReference" => NativeComEffects.AcquiresReference,
            "OutputMayBeWrittenOnFailure" => NativeComEffects.OutputMayBeWrittenOnFailure,
            "ReleasesReference" => NativeComEffects.ReleasesReference,
            _ => (NativeComEffects)byte.MaxValue,
        };
        return (byte)effect != byte.MaxValue;
    }

    private sealed record SourceNativeComInterface(Guid Iid, string? BaseIdentity,
        ImmutableArray<string> DeclaredMethodIdentities, ImmutableArray<NativeComMethodDefinition> Methods);

    private sealed record SourceNativeComOwner(string InterfaceIdentity, TextSpan Span);
}
