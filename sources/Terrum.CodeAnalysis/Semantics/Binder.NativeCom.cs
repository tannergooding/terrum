// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private void DeclareNativeComDirectMethods(NativeImportCatalog catalog)
    {
        var usedMethodNames = InvokedMemberNames();
        foreach (var function in NativeComCallContract.DirectMethodFunctions(
            catalog, [.. _structs], usedMethodNames))
        {
            if (!_functions.TryAdd(function.Name, _signatures.Count))
            {
                continue;
            }
            var parameters = function.Variables.Skip(1).Select(static variable =>
                new Parameter(variable.Name, variable.Type, default, variable.IsOut));
            _signatures.Add(new Signature(function.Name, function.ReturnType, [.. parameters],
                null, null, _trees[0], function.DeclaringStruct, false, false, function.IsInternal,
                false, false, true, false, function) { IsCleanup = function.IsCleanup });
        }
    }

    private void DeclareNativeComOwners(NativeImportCatalog catalog)
    {
        var referencedOwners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in _trees)
        {
            SetContext(tree);
            foreach (var typeName in tree.Root.DescendantNodes().OfType<SyntaxNode>()
                .Where(static node => node.Kind == SyntaxKind.TypeName))
            {
                var name = NameText(typeName);
                foreach (var owner in catalog.ComOwners)
                {
                    if (name == owner.BoundOwnerName || Qualify(name) == owner.BoundOwnerName
                        || _usings.Any(imported => imported + "." + name == owner.BoundOwnerName))
                    {
                        _ = referencedOwners.Add(owner.BoundOwnerName);
                    }
                }
            }
        }

        var program = new BoundProgram([], -1) {
            Structures = [.. _structs],
            NativeImportCatalog = catalog,
        };
        foreach (var owner in catalog.ComOwners)
        {
            if (!referencedOwners.Contains(owner.BoundOwnerName))
            {
                continue;
            }
            if (_structNames.TryGetValue(owner.BoundOwnerName, out var ownerType))
            {
                var index = (int)ownerType - 100;
                if (index < 0 || index >= _structs.Count
                    || _structDeclarations.ContainsKey(index))
                {
                    Report("TRM3030", $"Catalogued COM owner '{owner.BoundOwnerName}' cannot be declared in source.",
                        _trees[0].Root.Span);
                    continue;
                }
                try
                {
                    if (NativeComCallContract.Owner(ownerType, program) is null)
                    {
                        Report("TRM3030", $"Catalogued COM owner '{owner.BoundOwnerName}' has no valid layout.",
                            _trees[0].Root.Span);
                    }
                }
                catch (ArgumentException)
                {
                    Report("TRM3030", $"Catalogued COM owner '{owner.BoundOwnerName}' has no valid layout.",
                        _trees[0].Root.Span);
                }
                continue;
            }

            ownerType = (PrimitiveType)(100 + _structs.Count);
            _structNames.Add(owner.BoundOwnerName, ownerType);
            _structs.Add(new BoundStruct(owner.BoundOwnerName, [], owner.CellSize) {
                IsUnique = true,
                CleanupMethod = owner.BoundOwnerName + ".Close",
            });
            _structureAlignments.Add(ownerType, owner.CellAlignment);
            _ = _laidOut.Add((int)ownerType - 100);
        }
    }

    private void MoveSelectedNativeComStorageAggregateToTail()
    {
        if (_nativeImportCatalog is not NativeImportCatalog catalog)
        {
            return;
        }
        var selectedTypes = catalog.ComMethods
            .Where(static method => !method.ParameterAdaptations.IsDefaultOrEmpty)
            .SelectMany(static method => method.ParameterAdaptations)
            .Where(static adaptation => adaptation is not null
                && adaptation.Kind == NativeComParameterAdaptationKind.AggregateStorageAddress
                && adaptation.NoEscape)
            .Select(adaptation => NativeAggregateContract.TryResolveSourceType(adaptation.SourceType, _structs,
                    out var type)
                && adaptation.SourceSize == ValueLayout.Size(type, _structs)
                && adaptation.SourceAlignment
                    == ValueLayout.Alignment(type, _structs, catalog)
                && NativeAggregateContract.IsPlainValueAggregate(type, _structs)
                && _structDeclarations.ContainsKey((int)type - 100)
                    ? (int)type - 100 : -1)
            .Where(static index => index >= 0)
            .Distinct()
            .OrderBy(static index => index)
            .ToArray();
        if (selectedTypes.Length == 0)
        {
            return;
        }

        var firstSelectedIndex = _structs.Count - selectedTypes.Length;
        if (selectedTypes.SequenceEqual(Enumerable.Range(firstSelectedIndex, selectedTypes.Length)))
        {
            return;
        }

        var selectedIndexMap = selectedTypes.Select((index, offset) =>
                (index, targetIndex: firstSelectedIndex + offset))
            .ToDictionary(static pair => pair.index, static pair => pair.targetIndex);
        int MapIndex(int index) => selectedIndexMap.TryGetValue(index, out var targetIndex)
            ? targetIndex : index - selectedTypes.Count(selectedIndex => selectedIndex < index);
        PrimitiveType MapType(PrimitiveType type)
        {
            var index = (int)type - 100;
            return index >= 0 && index < _structs.Count
                ? (PrimitiveType)(100 + MapIndex(index))
                : type;
        }

        var selectedSet = selectedTypes.ToHashSet();
        var orderedStructures = _structs.Where((_, index) => !selectedSet.Contains(index))
            .Concat(selectedTypes.Select(index => _structs[index])).ToArray();
        _structs.Clear();
        _structs.AddRange(orderedStructures);
        for (var index = 0; index < _structs.Count; index++)
        {
            var structure = _structs[index];
            if (!structure.Fields.IsDefaultOrEmpty)
            {
                _structs[index] = structure with {
                    Fields = [.. structure.Fields.Select(field => field with { Type = MapType(field.Type) })],
                };
            }
        }

        foreach (var name in _structNames.Keys.ToArray())
        {
            _structNames[name] = MapType(_structNames[name]);
        }
        foreach (var alias in _aliases.Values)
        {
            alias.Type = MapType(alias.Type);
        }
        RemapSet(_layingOut, MapIndex);
        RemapSet(_laidOut, MapIndex);

        var declarations = _structDeclarations.ToArray();
        _structDeclarations.Clear();
        foreach (var (index, declaration) in declarations)
        {
            _structDeclarations.Add(MapIndex(index), declaration);
        }
        var alignments = _structureAlignments.ToArray();
        _structureAlignments.Clear();
        foreach (var (type, alignment) in alignments)
        {
            _structureAlignments.Add(MapType(type), alignment);
        }
    }

    private static void RemapSet(HashSet<int> set, Func<int, int> map)
    {
        var values = set.Select(map).ToArray();
        set.Clear();
        set.UnionWith(values);
    }

    private HashSet<string> InvokedMemberNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in _trees)
        {
            foreach (var node in tree.Root.DescendantNodes())
            {
                if (node.Kind == SyntaxKind.InvocationExpression && node.Children.Length > 0
                    && node.Children[0] is SyntaxNode { Kind: SyntaxKind.MemberAccessExpression } memberAccess
                    && memberAccess.Children[^1] is SyntaxTokenElement {
                        Token.Kind: TokenKind.Identifier,
                    } memberName)
                {
                    _ = names.Add(memberName.Token.Name);
                }
            }
        }
        return names;
    }
}
