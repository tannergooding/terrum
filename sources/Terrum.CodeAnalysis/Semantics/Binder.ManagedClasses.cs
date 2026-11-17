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
    private readonly List<BoundInterfaceDefinition> _interfaces = [];
    private readonly Dictionary<string, PrimitiveType> _classNames = new(StringComparer.Ordinal);
    private readonly Dictionary<int, (SyntaxTree Tree, SyntaxNode Declaration)> _classDeclarations = [];
    private readonly Dictionary<int,
        List<(SyntaxTree Tree, SyntaxNode Declaration)>>
        _classDeclarationParts = [];

    private sealed record PrivateClassSnapshotMaps(
        ImmutableArray<int> Structures, ImmutableArray<int> Classes,
        ImmutableArray<int> ExpectedClasses,
        ImmutableArray<bool> AddedStructures,
        ImmutableArray<int> CheckedReferences,
        ImmutableArray<int> FunctionPointers,
        ImmutableArray<int> NativePointers,
        ImmutableArray<int> FixedArrays,
        ImmutableArray<int> Enums);

    private void ImportPrivateClassReferences()
    {
        if (_reference is null)
        {
            return;
        }

        _classes.AddRange(_reference.Classes);
        var sourceLookupRootClasses = _reference.PrivateClassReferences
            .Where(static reference => reference.IsSourceLookupRoot)
            .Select(static reference =>
                ManagedClassTypes.ClassIndex(reference.ClassType))
            .ToHashSet();
        var snapshotMaps = new Dictionary<BoundPrivateClassSnapshot,
            PrivateClassSnapshotMaps>(ReferenceEqualityComparer.Instance);
        var snapshotOrdinal = 0;
        foreach (var reference in _reference.PrivateClassReferences)
        {
            var snapshot = reference.ExpectedSnapshot;
            if (!snapshotMaps.TryGetValue(snapshot, out var maps))
            {
                var structureMap = new int[snapshot.Structures.Length];
                var addedStructures = new bool[snapshot.Structures.Length];
                var checkedReferenceMap
                    = reference.ExpectedCheckedReferenceMapping;
                var functionPointerMap
                    = reference.ExpectedNativeFunctionPointerMapping;
                var nativePointerMap
                    = reference.ExpectedNativePointerMapping;
                var fixedArrayMap
                    = reference.ExpectedNativeFixedArrayMapping;
                var enumMap = reference.ExpectedEnumMapping;
                if (checkedReferenceMap.IsDefault
                    || checkedReferenceMap.Length
                        != snapshot.CheckedReferenceTypes.Length
                    || functionPointerMap.IsDefault
                    || functionPointerMap.Length
                        != snapshot.NativeFunctionPointerSignatures.Length
                    || nativePointerMap.IsDefault
                    || nativePointerMap.Length
                        != snapshot.NativePointerTypes.Length
                    || fixedArrayMap.IsDefault
                    || fixedArrayMap.Length
                        != snapshot.NativeFixedArrayTypes.Length
                    || enumMap.IsDefault
                    || enumMap.Length != snapshot.EnumDefinitions.Length
                    || enumMap.Any(index => index < -1
                        || (index >= 0
                            && (uint)index
                                >= (uint)_reference.EnumDefinitions.Length)))
                {
                    Report("TRM3005",
                        "Private class reference has an invalid snapshot type-family map.",
                        _trees[0].Root.Span);
                    return;
                }
                var projectedStructures = _reference.Structures;
                for (var index = 0; index < snapshot.Structures.Length;
                    index++)
                {
                    var source = snapshot.Structures[index];
                    var existingIndex = FindEquivalentReferenceStructure(
                        source, projectedStructures);
                    if (existingIndex >= 0)
                    {
                        structureMap[index] = existingIndex;
                        continue;
                    }

                    if (_structs.Count >= SemanticVerifier.MaximumVariables)
                    {
                        Report("TRM3005",
                            "Private class reference closure exceeds the supported structure limit.",
                            _trees[0].Root.Span);
                        return;
                    }
                    var hiddenName = _structs.Any(structure =>
                            structure.Name == source.Name)
                        ? PrivateClassTypeName(snapshotOrdinal, index,
                            source.Name) : source.Name;
                    structureMap[index] = _structs.Count;
                    addedStructures[index] = true;
                    _structs.Add(source with {
                        Name = hiddenName,
                        Fields = [],
                    });
                    _ = _laidOut.Add(_structs.Count - 1);
                }

                var classMap = new int[snapshot.Classes.Length];
                Array.Fill(classMap, -1);
                var expectedClassMap = reference.ExpectedClassMapping;
                if (expectedClassMap.IsDefault
                    || expectedClassMap.Length != snapshot.Classes.Length
                    || expectedClassMap.Any(index => index < -1
                        || (index >= 0
                            && (uint)index
                                >= (uint)_reference.Classes.Length)))
                {
                    Report("TRM3005",
                        "Private class reference has an invalid expected class mapping.",
                        _trees[0].Root.Span);
                    return;
                }
                for (var index = 0; index < expectedClassMap.Length; index++)
                {
                    classMap[index] = expectedClassMap[index];
                }
                foreach (var rootReference in _reference
                    .PrivateClassReferences.Where(candidate =>
                        ReferenceEquals(candidate.ExpectedSnapshot,
                            snapshot)))
                {
                    var expectedIndex = ManagedClassTypes.ClassIndex(
                        rootReference.ExpectedClassType);
                    var currentIndex = ManagedClassTypes.ClassIndex(
                        rootReference.ClassType);
                    if ((uint)expectedIndex >= (uint)classMap.Length
                        || (uint)currentIndex >= (uint)_reference.Classes.Length
                        || (classMap[expectedIndex] >= 0
                            && classMap[expectedIndex] != currentIndex))
                    {
                        Report("TRM3005",
                            "Private class reference has an inconsistent class selector.",
                            _trees[0].Root.Span);
                        return;
                    }
                    classMap[expectedIndex] = currentIndex;
                }
                for (var index = 0; index < classMap.Length; index++)
                {
                    if (classMap[index] >= 0)
                    {
                        continue;
                    }
                    if (_classes.Count
                        >= ManagedClassTypes.OptionalFirst
                            - ManagedClassTypes.First)
                    {
                        Report("TRM3005",
                            "Private class reference closure exceeds the supported class limit.",
                            _trees[0].Root.Span);
                        return;
                    }
                    classMap[index] = _classes.Count;
                    var sourceClassName
                        = snapshot.Classes[index].Name;
                    _classes.Add(new BoundClass(
                        _classes.Any(@class =>
                            @class.Name == sourceClassName)
                            ? PrivateClassTypeName(snapshotOrdinal, index,
                                sourceClassName)
                            : sourceClassName, [], 0) {
                        IsInternal = true,
                        IsMutable = snapshot.Classes[index].IsMutable,
                        IsUnique = snapshot.Classes[index].IsUnique,
                        HasCleanup = snapshot.Classes[index].HasCleanup,
                        CleanupMethod = snapshot.Classes[index].CleanupMethod,
                    });
                }

                for (var index = 0; index < snapshot.Structures.Length;
                    index++)
                {
                    if (!addedStructures[index])
                    {
                        continue;
                    }
                    var source = snapshot.Structures[index];
                    _structs[structureMap[index]] = source with {
                        Name = _structs[structureMap[index]].Name,
                        Fields = [.. source.Fields.Select(field =>
                            field with {
                                Type = RemapPrivateClassSnapshotType(
                                    field.Type, structureMap,
                                    classMap, checkedReferenceMap,
                                    functionPointerMap, nativePointerMap,
                                    fixedArrayMap, enumMap),
                            })],
                    };
                }
                for (var index = 0; index < snapshot.Classes.Length;
                    index++)
                {
                    var source = snapshot.Classes[index];
                    var targetIndex = classMap[index];
                    var isRoot
                        = sourceLookupRootClasses.Contains(targetIndex);
                    _classes[targetIndex] = source with {
                        Name = isRoot ? source.Name
                            : PrivateClassTypeName(snapshotOrdinal, index,
                                source.Name),
                        IsInternal = !isRoot || source.IsInternal,
                        Fields = [.. source.Fields.Select(field =>
                            field with {
                                Type = RemapPrivateClassSnapshotType(
                                    field.Type, structureMap,
                                    classMap, checkedReferenceMap,
                                    functionPointerMap, nativePointerMap,
                                    fixedArrayMap, enumMap),
                            })],
                    };
                }

                maps = new PrivateClassSnapshotMaps(
                    [.. structureMap], [.. classMap],
                    reference.ExpectedClassMapping,
                    [.. addedStructures], checkedReferenceMap,
                    functionPointerMap, nativePointerMap, fixedArrayMap,
                    enumMap);
                snapshotMaps.Add(snapshot, maps);
                AddPrivateClassSnapshotOrigins(snapshot, maps,
                    _reference.EnumDefinitions);
                snapshotOrdinal++;
            }
            else if (!maps.CheckedReferences.SequenceEqual(
                    reference.ExpectedCheckedReferenceMapping)
                || !maps.ExpectedClasses.SequenceEqual(
                    reference.ExpectedClassMapping)
                || !maps.FunctionPointers.SequenceEqual(
                    reference.ExpectedNativeFunctionPointerMapping)
                || !maps.NativePointers.SequenceEqual(
                    reference.ExpectedNativePointerMapping)
                || !maps.FixedArrays.SequenceEqual(
                    reference.ExpectedNativeFixedArrayMapping)
                || !maps.Enums.SequenceEqual(
                    reference.ExpectedEnumMapping))
            {
                Report("TRM3005",
                    "Private class references contain inconsistent type-family maps for one snapshot.",
                    _trees[0].Root.Span);
                return;
            }

            var expectedClassIndex = ManagedClassTypes.ClassIndex(
                reference.ExpectedClassType);
            if ((uint)expectedClassIndex >= (uint)maps.Classes.Length)
            {
                Report("TRM3005",
                    "Private class reference has an absent expected class.",
                    _trees[0].Root.Span);
                return;
            }
            var classType = (PrimitiveType)(ManagedClassTypes.First
                + maps.Classes[expectedClassIndex]);
            var origin = reference.Origin with {
                PrivateType = classType,
            };
            _privateClassReferences.Add(reference with {
                ClassType = classType,
                Origin = origin,
                ExpectedStructureMapping = maps.Structures,
                ExpectedClassMapping = maps.Classes,
                ExpectedCheckedReferenceMapping = maps.CheckedReferences,
                ExpectedNativeFunctionPointerMapping
                    = maps.FunctionPointers,
                ExpectedNativePointerMapping = maps.NativePointers,
                ExpectedNativeFixedArrayMapping = maps.FixedArrays,
                ExpectedEnumMapping = maps.Enums,
            });
        }

        foreach (var reference in _privateClassReferences)
        {
            if (!reference.IsSourceLookupRoot)
            {
                continue;
            }
            var index = ManagedClassTypes.ClassIndex(reference.ClassType);
            if ((uint)index >= (uint)_classes.Count)
            {
                Report("TRM3005",
                    "Private class reference has an absent source lookup root.",
                    _trees[0].Root.Span);
                return;
            }
            var @class = _classes[index];
            if (_structNames.ContainsKey(@class.Name))
            {
                Report("TRM3003",
                    $"Referenced nominal type '{@class.Name}' is ambiguous.",
                    _trees[0].Root.Span);
                continue;
            }
            if (_classNames.TryGetValue(@class.Name,
                    out var existingClassType))
            {
                if (existingClassType != reference.ClassType)
                {
                    Report("TRM3003",
                        $"Referenced nominal type '{@class.Name}' is ambiguous.",
                        _trees[0].Root.Span);
                }
                continue;
            }
            _classNames.Add(@class.Name, reference.ClassType);
        }
    }

    private void AddPrivateClassSnapshotOrigins(
        BoundPrivateClassSnapshot snapshot,
        PrivateClassSnapshotMaps maps,
        ImmutableArray<BoundEnumDefinition> enumDefinitions)
    {
        foreach (var origin in snapshot.NominalTypeOrigins)
        {
            var type = origin.PrivateType;
            if (ManagedClassTypes.IsClass(type)
                || (ManagedClassTypes.IsOptional(type)
                    && ManagedClassTypes.IsClass(
                        ManagedClassTypes.Required(type))))
            {
                var index = ManagedClassTypes.ClassIndex(type);
                if ((uint)index >= (uint)maps.Classes.Length)
                {
                    Report("TRM3005",
                        "Private class snapshot has an invalid class origin.",
                        _trees[0].Root.Span);
                    return;
                }
                type = (PrimitiveType)(ManagedClassTypes.First
                    + maps.Classes[index]);
            }
            else
            {
                if (BoundEnumTypes.IsType(type))
                {
                    var enumIndex = BoundEnumTypes.Index(type);
                    if ((uint)enumIndex >= (uint)maps.Enums.Length
                        || maps.Enums[enumIndex] < 0
                        || (uint)maps.Enums[enumIndex]
                            >= (uint)enumDefinitions.Length)
                    {
                        Report("TRM3005",
                            "Private class snapshot has an invalid enum origin.",
                            _trees[0].Root.Span);
                        return;
                    }
                    var destinationEnumIndex = maps.Enums[enumIndex];
                    var expectedDefinition
                        = snapshot.EnumDefinitions[enumIndex];
                    var actualDefinition
                        = enumDefinitions[destinationEnumIndex];
                    if (origin.OriginKind
                            == BoundPrivateNominalOriginKind
                                .ImportedExpectation
                        && actualDefinition.Identity
                            is BoundEnumDeclarationIdentity.Private
                                actualPrivate
                        && actualPrivate.Value.Origin.OriginKind
                            == BoundPrivateNominalOriginKind.OwnedDefinition
                        && origin.SelectedModuleIdentity
                            == actualPrivate.Value.Origin.UsingModuleIdentity
                        && origin.SelectedExportKey
                            == actualPrivate.Value.Origin.LocalDeclarationKey
                        && BoundEnumTypes.HasSameContractShape(
                            expectedDefinition, actualDefinition))
                    {
                        continue;
                    }
                    type = BoundEnumTypes.Type(destinationEnumIndex);
                }
                else
                {
                    var structureIndex = (int)type - 100;
                    if (structureIndex is >= 0
                        and < BoundEnumTypes.First - 100)
                    {
                        if ((uint)structureIndex
                            >= (uint)maps.Structures.Length)
                        {
                            Report("TRM3005",
                                "Private class snapshot has an invalid structure origin.",
                                _trees[0].Root.Span);
                            return;
                        }
                        type = (PrimitiveType)(100
                            + maps.Structures[structureIndex]);
                    }
                }
            }
            var remapped = origin with { PrivateType = type };
            var existing = _privateNominalTypeOrigins.FirstOrDefault(
                candidate => candidate.UsingModuleIdentity
                    == remapped.UsingModuleIdentity
                    && candidate.LocalDeclarationKey
                        == remapped.LocalDeclarationKey);
            if (existing is not null)
            {
                if (existing != remapped)
                {
                    Report("TRM3005",
                        "Private class snapshots contain conflicting original nominal origins.",
                        _trees[0].Root.Span);
                    return;
                }
                continue;
            }
            _privateNominalTypeOrigins.Add(remapped);
        }
    }

    private static PrimitiveType RemapPrivateClassSnapshotType(
        PrimitiveType type, int[] structureMap, int[] classMap,
        ImmutableArray<int> checkedReferenceMap,
        ImmutableArray<int> functionPointerMap,
        ImmutableArray<int> nativePointerMap,
        ImmutableArray<int> fixedArrayMap,
        ImmutableArray<int> enumMap)
    {
        if (ManagedClassTypes.IsClass(type)
            || (ManagedClassTypes.IsOptional(type)
                && ManagedClassTypes.IsClass(
                    ManagedClassTypes.Required(type))))
        {
            var classIndex = ManagedClassTypes.ClassIndex(type);
            if ((uint)classIndex >= (uint)classMap.Length)
            {
                throw new ArgumentException(
                    "Private class snapshot contains an invalid nested class type.");
            }
            var mapped = (PrimitiveType)(ManagedClassTypes.First
                + classMap[classIndex]);
            if (ManagedClassTypes.IsWritable(type))
            {
                mapped = ManagedClassTypes.Writable(mapped);
            }
            if (ManagedClassTypes.IsOptional(type))
            {
                mapped = ManagedClassTypes.Optional(mapped);
            }
            return mapped;
        }
        if (CheckedReferenceTypes.IsType(type))
        {
            var referenceIndex = CheckedReferenceTypes.Index(type);
            if ((uint)referenceIndex >= (uint)checkedReferenceMap.Length
                || checkedReferenceMap[referenceIndex] < 0)
            {
                throw new ArgumentException(
                    "Private class snapshot contains an unavailable checked-reference type.");
            }
            return CheckedReferenceTypes.Type(
                checkedReferenceMap[referenceIndex]);
        }
        var structureIndex = (int)type - 100;
        if (structureIndex is >= 0
            and < BoundEnumTypes.First - 100)
        {
            if ((uint)structureIndex >= (uint)structureMap.Length)
            {
                throw new ArgumentException(
                    "Private class snapshot contains an invalid nested structure type.");
            }
            return (PrimitiveType)(100 + structureMap[structureIndex]);
        }
        if (NativePointerTypes.IsType(type))
        {
            var pointerIndex = NativePointerTypes.Index(type);
            if ((uint)pointerIndex >= (uint)nativePointerMap.Length
                || nativePointerMap[pointerIndex] < 0)
            {
                throw new ArgumentException(
                    "Private class snapshot contains an unavailable native-pointer type.");
            }
            return NativePointerTypes.Type(nativePointerMap[pointerIndex]);
        }
        if (NativeFunctionPointerTypes.IsType(type))
        {
            var functionPointerIndex
                = NativeFunctionPointerTypes.Index(type);
            if ((uint)functionPointerIndex
                    >= (uint)functionPointerMap.Length
                || functionPointerMap[functionPointerIndex] < 0)
            {
                throw new ArgumentException(
                    "Private class snapshot contains an unavailable function-pointer type.");
            }
            return NativeFunctionPointerTypes.Type(
                functionPointerMap[functionPointerIndex]);
        }
        if (NativeFixedArrayTypes.IsType(type))
        {
            var fixedArrayIndex = NativeFixedArrayTypes.Index(type);
            if ((uint)fixedArrayIndex >= (uint)fixedArrayMap.Length
                || fixedArrayMap[fixedArrayIndex] < 0)
            {
                throw new ArgumentException(
                    "Private class snapshot contains an unavailable fixed-array type.");
            }
            return NativeFixedArrayTypes.Type(
                fixedArrayMap[fixedArrayIndex]);
        }
        if (BoundEnumTypes.IsType(type))
        {
            var enumIndex = BoundEnumTypes.Index(type);
            if ((uint)enumIndex >= (uint)enumMap.Length
                || enumMap[enumIndex] < 0)
            {
                throw new ArgumentException(
                    "Private class snapshot contains an unavailable enum type.");
            }
            return BoundEnumTypes.Type(enumMap[enumIndex]);
        }
        return type;
    }

    private static int FindEquivalentReferenceStructure(
        BoundStruct expected, ImmutableArray<BoundStruct> structures)
    {
        for (var index = 0; index < structures.Length; index++)
        {
            if (structures[index].Name == expected.Name
                && structures[index].Size == expected.Size
                && structures[index].Fields.Length
                    == expected.Fields.Length
                && structures[index].Fields.SequenceEqual(expected.Fields))
            {
                return index;
            }
        }
        return -1;
    }

    private static string PrivateClassTypeName(int snapshotIndex,
        int typeIndex, string name) =>
        "$privateclass."
        + snapshotIndex.ToString(
            System.Globalization.CultureInfo.InvariantCulture)
        + "."
        + typeIndex.ToString(
            System.Globalization.CultureInfo.InvariantCulture)
        + "."
        + name;

    private bool IsClass(PrimitiveType type) => ManagedClassTypes.IsClass(type)
        && ManagedClassTypes.ClassIndex(type) < _classes.Count;

    private void DeclareClassName(SyntaxTree tree, SyntaxNode declaration)
    {
        if (HasGenericTypeParameters(declaration))
        {
            Report("TRM3005",
                "Generic nominal type declarations are not supported by the current compiler profile.",
                declaration.Span);
            return;
        }

        _usesManagedNode = true;
        var name = StructName(declaration);
        if (TryPrimitiveType(name.Text, out _))
        {
            Report("TRM3003", $"The primitive type '{name.Text}' cannot be declared as a class.", declaration.Span);
            return;
        }
        var qualified = Qualify(name.Name);
        if (qualified == DirectNativeImportContract.AttributeFullyQualifiedName
            || _structNames.ContainsKey(qualified)
            || _genericStructTemplates.ContainsKey(qualified)
            || qualified is NativeManagedNodeIntrinsics.TypeName or NativeManagedNodeIntrinsics.HeapName)
        {
            Report("TRM3003", $"Duplicate or reserved managed class '{qualified}'.", declaration.Span);
            return;
        }
        var tokens = declaration.Children.OfType<SyntaxTokenElement>().ToArray();
        var isPartial = tokens.Any(static token =>
            token.Token.Kind == Terrum.CodeAnalysis.Lexing.TokenKind.Identifier
            && token.Token.Text == "partial");
        var hasVisibility = tokens.Any(static token =>
            token.Token.Kind is Terrum.CodeAnalysis.Lexing.TokenKind.PublicKeyword
                or Terrum.CodeAnalysis.Lexing.TokenKind.InternalKeyword
                or Terrum.CodeAnalysis.Lexing.TokenKind.PrivateKeyword
                || token.Token.Kind == Terrum.CodeAnalysis.Lexing.TokenKind.Identifier
                    && token.Token.Text is "file" or "friend");
        var isMutable = false;
        var isUnique = false;
        var isPublic = false;
        foreach (var token in tokens)
        {
            isMutable |= token.Token.Kind == Terrum.CodeAnalysis.Lexing.TokenKind.MutKeyword;
            isUnique |= token.Token.Kind
                == Terrum.CodeAnalysis.Lexing.TokenKind.UniqueKeyword;
            isPublic |= token.Token.Kind == Terrum.CodeAnalysis.Lexing.TokenKind.PublicKeyword;
            if (token.Token.Kind == Terrum.CodeAnalysis.Lexing.TokenKind.ExternKeyword)
            {
                Report("TRM3030", "Extern is only valid on a supported callable declaration.", token.Token.Span);
            }
        }

        if (_classNames.TryGetValue(qualified, out var existingType))
        {
            var existingIndex = ManagedClassTypes.ClassIndex(existingType);
            if (!_classDeclarationParts.TryGetValue(existingIndex,
                    out var parts)
                || !isPartial
                || parts.Any(part => !HasPartialClassModifier(
                    part.Declaration)))
            {
                Report("TRM3003",
                    $"Duplicate or non-partial managed class '{qualified}'.",
                    declaration.Span);
                return;
            }

            parts.Add((tree, declaration));
            return;
        }

        if (_classes.Count >= SemanticVerifier.MaximumVariables)
        {
            Report("TRM3016",
                "Source exceeds the supported number of managed classes.",
                declaration.Span);
            return;
        }

        var index = _classes.Count;
        _classNames.Add(qualified,
            (PrimitiveType)(ManagedClassTypes.First + index));
        _classes.Add(new BoundClass(qualified, [], 0) {
            IsInternal = !isPublic,
            IsMutable = isMutable,
            IsUnique = isUnique,
            HasCleanup = false,
        });
        _classDeclarationParts.Add(index, [(tree, declaration)]);
        if (hasVisibility || !isPartial)
        {
            _classDeclarations.Add(index, (tree, declaration));
        }
    }

    private static bool HasPartialClassModifier(SyntaxNode declaration)
        => declaration.Children.OfType<SyntaxTokenElement>().Any(
            static token =>
                token.Token.Kind == Terrum.CodeAnalysis.Lexing.TokenKind.Identifier
                && token.Token.Text == "partial");

    private static bool HasClassVisibilityModifier(
        SyntaxNode declaration)
        => declaration.Children.OfType<SyntaxTokenElement>().Any(
            static token =>
                token.Token.Kind is
                    Terrum.CodeAnalysis.Lexing.TokenKind.PublicKeyword
                    or Terrum.CodeAnalysis.Lexing.TokenKind.InternalKeyword
                    or Terrum.CodeAnalysis.Lexing.TokenKind.PrivateKeyword
                || token.Token.Kind
                    == Terrum.CodeAnalysis.Lexing.TokenKind.Identifier
                    && token.Token.Text is "file" or "friend");

    private static bool HasClassModifier(SyntaxNode declaration,
        Terrum.CodeAnalysis.Lexing.TokenKind kind)
        => declaration.Children.OfType<SyntaxTokenElement>().Any(token =>
            token.Token.Kind == kind);

    private void FinalizeClassDeclarationGroups()
    {
        foreach (var (classIndex, parts) in _classDeclarationParts
            .OrderBy(static pair => pair.Key))
        {
            if (parts.Count == 0)
            {
                Report("TRM3003",
                    "A managed class has no source declaration part.",
                    _tree.Root.Span);
                continue;
            }

            var visibilityParts = parts.Where(part =>
                HasClassVisibilityModifier(part.Declaration)).ToArray();
            if (parts.Count > 1
                && (parts.Any(part => !HasPartialClassModifier(
                        part.Declaration))
                    || visibilityParts.Length != 1))
            {
                SetContext(parts[0].Tree, parts[0].Declaration);
                Report("TRM3003",
                    "Partial class parts require one explicit-visibility primary and matching partial secondaries.",
                    parts[0].Declaration.Span);
            }

            var primary = visibilityParts.Length == 1
                ? visibilityParts[0] : parts[0];
            foreach (var secondary in parts)
            {
                if (ReferenceEquals(secondary.Declaration,
                        primary.Declaration))
                {
                    continue;
                }

                SetContext(secondary.Tree, secondary.Declaration);
                if (HasClassVisibilityModifier(secondary.Declaration))
                {
                    Report("TRM3003",
                        "A secondary partial class part cannot repeat visibility.",
                        secondary.Declaration.Span);
                }

                foreach (var modifier in new[] {
                    Terrum.CodeAnalysis.Lexing.TokenKind.MutKeyword,
                    Terrum.CodeAnalysis.Lexing.TokenKind.UniqueKeyword,
                    Terrum.CodeAnalysis.Lexing.TokenKind.StaticKeyword,
                })
                {
                    if (HasClassModifier(secondary.Declaration, modifier)
                        != HasClassModifier(primary.Declaration, modifier))
                    {
                        Report("TRM3003",
                            "A secondary partial class part must repeat the primary type modifiers.",
                            secondary.Declaration.Span);
                    }
                }
            }

            if (_classDeclarations.TryGetValue(classIndex,
                    out var existingPrimary)
                && !ReferenceEquals(existingPrimary.Declaration,
                    primary.Declaration))
            {
                _classDeclarations[classIndex] = primary;
            }
            else if (!_classDeclarations.ContainsKey(classIndex))
            {
                _classDeclarations.Add(classIndex, primary);
            }

            var primaryTokens = primary.Declaration.Children
                .OfType<SyntaxTokenElement>();
            _classes[classIndex] = _classes[classIndex] with {
                IsInternal = !primaryTokens.Any(static token =>
                    token.Token.Kind
                        == Terrum.CodeAnalysis.Lexing.TokenKind.PublicKeyword),
                IsMutable = primaryTokens.Any(static token =>
                    token.Token.Kind
                        == Terrum.CodeAnalysis.Lexing.TokenKind.MutKeyword),
                IsUnique = primaryTokens.Any(static token =>
                    token.Token.Kind
                        == Terrum.CodeAnalysis.Lexing.TokenKind.UniqueKeyword),
            };
        }
    }

    private enum DeclaredTypeResolution
    {
        Missing,
        Unique,
        Ambiguous,
        Invalid,
    }

    private DeclaredTypeResolution ResolveTypeName(string name,
        out PrimitiveType type) =>
        ResolveTypeName(name, null, default, out type);

    private DeclaredTypeResolution ResolveTypeName(string name, ImmutableArray<PrimitiveType>? typeArguments,
        TextSpan useSite, out PrimitiveType type,
        ImmutableArray<SyntaxNode> typeArgumentSyntaxes = default)
    {
        if (typeArguments is not null && _genericProofSource is not null
            && FindAliasDefinition(name) is AliasDefinition localAlias)
        {
            return ResolveAlias(localAlias, typeArguments, useSite, out type,
                typeArgumentSyntaxes);
        }
        if (_genericContext is GenericInstantiation generic
            && generic.Template.ParameterIndex(name) is var index && index >= 0)
        {
            type = generic.Types[index];
            return DeclaredTypeResolution.Unique;
        }
        if (_genericProofSource is not null
            && typeArguments is ImmutableArray<PrimitiveType>
                sourceGenericArguments
            && !HasNonGenericSourceTypeCandidate(name)
            && !HasImportedGenericNominalTypeCandidate(name)
            && TryFindGenericStructTemplateForTypeTerm(name,
                out var sourceGenericTemplate))
        {
            var diagnosticCount = DiagnosticCount;
            type = InstantiateGenericStruct(sourceGenericTemplate,
                sourceGenericArguments, useSite);
            return DiagnosticCount == diagnosticCount
                ? DeclaredTypeResolution.Unique
                : DeclaredTypeResolution.Invalid;
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
        var visibleAlias = FindAliasDefinition(name);
        var invalidAlias = false;
        var invalidGenericNominal = false;
        var genericArityMismatch = false;
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
            foreach (var imported in _usings.ToArray())
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
        if (invalidGenericNominal)
        {
            type = default;
            Report("TRM3005",
                "Terrum.Threading.Tasks.Task requires an explicitly selected exact Core Task definition.",
                useSite);
            return DeclaredTypeResolution.Invalid;
        }
        if (!hasCandidate && genericArityMismatch)
        {
            type = default;
            Report("TRM3005",
                $"Generic type '{name}' requires its declared type argument count.",
                useSite);
            return DeclaredTypeResolution.Invalid;
        }
        if (ambiguous)
        {
            type = default;
            return DeclaredTypeResolution.Ambiguous;
        }
        if (!hasCandidate)
        {
            type = default;
            return DeclaredTypeResolution.Missing;
        }
        return DeclaredTypeResolution.Unique;

        void AddCandidate(string qualified)
        {
            var hasNoTypeArguments = typeArguments is null
                || typeArguments.Value.IsEmpty;
            var hasNonGenericTypeDefinition
                = _structNames.ContainsKey(qualified)
                    || _classNames.ContainsKey(qualified)
                    || _enumNames.ContainsKey(qualified)
                    || _sourceEnumNames.ContainsKey((_tree, qualified))
                    || (_aliases.TryGetValue(qualified,
                            out var namedAlias)
                        && namedAlias.TypeParameters.IsEmpty)
                    || _genericNominalTypeDefinitions.Any(definition =>
                        definition.Formals.IsEmpty
                        && GetNominalTypeQualifiedName(definition)
                            == qualified);
            if (hasNoTypeArguments
                && _ambiguousStructNames.Contains(qualified))
            {
                ambiguous = true;
            }
            else if (hasNoTypeArguments
                && _structNames.TryGetValue(qualified,
                    out var structure))
            {
                AddTypeCandidate(structure);
            }
            if (hasNoTypeArguments
                && _classNames.TryGetValue(qualified, out var @class))
            {
                AddTypeCandidate(@class);
            }
            if (hasNoTypeArguments
                && _ambiguousEnumNames.Contains(qualified))
            {
                ambiguous = true;
            }
            if (hasNoTypeArguments
                && _enumNames.TryGetValue(qualified, out var enumType))
            {
                AddTypeCandidate(enumType);
            }
            if (hasNoTypeArguments
                && _sourceEnumNames.TryGetValue((_tree, qualified),
                    out var sourceEnumType))
            {
                AddTypeCandidate(sourceEnumType);
            }
            if (_genericStructTemplates.TryGetValue(qualified,
                    out var genericStruct)
                && !(hasNoTypeArguments
                    && (hasNonGenericTypeDefinition || ambiguous)))
            {
                var arguments = typeArguments
                    ?? [];
                if (genericStruct.ConstantFormals.IsEmpty
                    && arguments.Length <= genericStruct.TypeParameters.Length
                    && genericStruct.TypeDefaults.Skip(arguments.Length)
                        .All(static defaultType => defaultType is not null))
                {
                    AddTypeCandidate(InstantiateGenericStruct(genericStruct, arguments, useSite));
                }
                else
                {
                    genericArityMismatch = true;
                }
            }
            var genericNominalCandidates = _genericNominalTypeDefinitions
                .Where(definition =>
                    (!definition.Formals.IsEmpty
                        || IsSourceLookupStandardClassDefinition(definition))
                    && GetNominalTypeQualifiedName(definition) == qualified)
                .ToArray();
            if (genericNominalCandidates.Length != 0)
            {
                if (BoundGenericNominalTypeContract.IsGenericNominalTypeName(qualified))
                {
                    if (_reference?.CoreTaskType is not CoreTaskTypeIdentity coreTask)
                    {
                        invalidGenericNominal = true;
                        return;
                    }
                    if (genericNominalCandidates.Length != 1
                        || genericNominalCandidates[0].Identity != coreTask.Definition)
                    {
                        ambiguous = true;
                        return;
                    }
                }
                else if (genericNominalCandidates.Length != 1)
                {
                    ambiguous = true;
                    return;
                }

                if (TryInstantiateGenericNominalType(
                    genericNominalCandidates[0], typeArguments, useSite,
                    out var genericNominalType))
                {
                    AddTypeCandidate(genericNominalType);
                }
                else
                {
                    genericArityMismatch = true;
                }
            }
            if (_aliases.TryGetValue(qualified, out var alias)
                && (visibleAlias is null
                    || ReferenceEquals(alias, visibleAlias)))
            {
                var aliasResolution = ResolveAlias(alias, typeArguments,
                    useSite, out var aliasType, typeArgumentSyntaxes);
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

    private bool HasImportedGenericNominalTypeCandidate(string name)
    {
        if (name.Contains('.', StringComparison.Ordinal))
        {
            return _genericNominalTypeDefinitions.Any(definition =>
                (!definition.Formals.IsEmpty
                    || IsSourceLookupStandardClassDefinition(definition))
                && GetNominalTypeQualifiedName(definition) == name);
        }

        if (_genericNominalTypeDefinitions.Any(definition =>
            (!definition.Formals.IsEmpty
                || IsSourceLookupStandardClassDefinition(definition))
            && (definition.Name == name
                || GetNominalTypeQualifiedName(definition) == name)))
        {
            return true;
        }

        var parent = _namespace;
        while (parent.Contains('.', StringComparison.Ordinal))
        {
            parent = parent[..parent.LastIndexOf('.', StringComparison.Ordinal)];
            if (_genericNominalTypeDefinitions.Any(definition =>
                (!definition.Formals.IsEmpty
                    || IsSourceLookupStandardClassDefinition(definition))
                && GetNominalTypeQualifiedName(definition)
                    == parent + "." + name))
            {
                return true;
            }
        }

        return _usings.Any(imported => _genericNominalTypeDefinitions.Any(definition =>
            (!definition.Formals.IsEmpty
                || IsSourceLookupStandardClassDefinition(definition))
            && definition.Namespace == imported && definition.Name == name));
    }

    private bool HasNonGenericSourceTypeCandidate(string name)
    {
        var source = _genericProofSource ?? this;

        var candidates = new HashSet<string>(StringComparer.Ordinal);
        void Add(string candidate) => _ = candidates.Add(candidate);
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
                parent = parent[..parent.LastIndexOf('.',
                    StringComparison.Ordinal)];
                Add(parent + "." + name);
            }

            foreach (var imported in _usings)
            {
                Add(imported + "." + name);
            }
        }

        return candidates.Any(qualified =>
            source._structNames.ContainsKey(qualified)
            || source._classNames.ContainsKey(qualified)
            || source._enumNames.ContainsKey(qualified)
            || source._sourceEnumNames.ContainsKey((_tree, qualified))
            || source._aliases.ContainsKey(qualified)
            || source._genericNominalTypeDefinitions.Any(definition =>
                definition.Formals.IsEmpty
                && GetNominalTypeQualifiedName(definition)
                    == qualified));
    }

    private bool IsSourceLookupStandardClassDefinition(
        BoundGenericNominalTypeDefinition definition)
        => definition.NominalKind == 2 && definition.Formals.IsEmpty
            && _standardClassDeclarations.Any(declaration =>
                declaration.Declaration.Identity == definition.Identity
                && !declaration.SelectedExportKeys.IsEmpty
                && declaration.Expectations.IsEmpty);

    private static string GetNominalTypeQualifiedName(
        BoundGenericNominalTypeDefinition definition)
        => definition.Namespace.Length == 0 ? definition.Name
            : definition.Namespace + "." + definition.Name;

    private bool TryInstantiateGenericNominalType(
        BoundGenericNominalTypeDefinition definition,
        ImmutableArray<PrimitiveType>? suppliedArguments,
        TextSpan useSite, out PrimitiveType type)
    {
        type = default;
        if (definition.Formals.IsEmpty)
        {
            return (suppliedArguments is null
                    || suppliedArguments.Value.IsEmpty)
                && TryInstantiateSourceLookupStandardClass(definition,
                    useSite, out type);
        }

        if (definition.Formals.Length > SemanticVerifier.MaximumArguments
            || definition.Formals.Any(static formal =>
                formal.Kind != BoundGenericArgumentKind.Type))
        {
            return false;
        }

        var supplied = suppliedArguments?.Length ?? 0;
        if (supplied > definition.Formals.Length)
        {
            return false;
        }

        var arguments = ImmutableArray.CreateBuilder<BoundGenericNominalTypeArgument>(
            definition.Formals.Length);
        for (var ordinal = 0; ordinal < definition.Formals.Length; ordinal++)
        {
            if (ordinal < supplied)
            {
                arguments.Add(BoundGenericNominalTypeArgument.ForType(
                    suppliedArguments!.Value[ordinal]));
                continue;
            }

            var defaultArgument = definition.Formals[ordinal].Default;
            if (defaultArgument is not
                {
                    Kind: BoundGenericArgumentKind.Type,
                    DecodedType: PrimitiveType decodedType,
                })
            {
                return false;
            }

            arguments.Add(BoundGenericNominalTypeArgument.ForType(
                decodedType, defaultArgument.TypeQualifiers));
        }

        var immutableArguments = arguments.MoveToImmutable();
        var key = BoundGenericNominalTypeContract.CreateUseKey(
            definition.Identity, immutableArguments, 0);
        if (_genericNominalTypeUses.TryGetValue(key, out type))
        {
            return true;
        }

        if (_genericNominalTypeInstances.Count >= BoundGenericNominalTypeTypes.MaximumCount)
        {
            Report("TRM3005", "The generic nominal type instance limit was exceeded.", useSite);
            return false;
        }

        type = BoundGenericNominalTypeTypes.Type(_genericNominalTypeInstances.Count);
        _genericNominalTypeInstances.Add(new BoundGenericNominalTypeInstance(
            definition.Identity, immutableArguments, 0));
        _genericNominalTypeUses.Add(key, type);
        return true;
    }

    private bool TryInstantiateSourceLookupStandardClass(
        BoundGenericNominalTypeDefinition definition, TextSpan useSite,
        out PrimitiveType type)
    {
        type = default;
        if (!IsSourceLookupStandardClassDefinition(definition))
        {
            return false;
        }

        var arguments
            = ImmutableArray<BoundGenericNominalTypeArgument>.Empty;
        var key = BoundGenericNominalTypeContract.CreateUseKey(
            definition.Identity, arguments, 0);
        if (_genericNominalTypeUses.TryGetValue(key, out type))
        {
            return true;
        }
        if (_genericNominalTypeInstances.Count
            >= BoundGenericNominalTypeTypes.MaximumCount)
        {
            Report("TRM3005",
                "The generic nominal type instance limit was exceeded.",
                useSite);
            return false;
        }

        type = BoundGenericNominalTypeTypes.Type(
            _genericNominalTypeInstances.Count);
        _genericNominalTypeInstances.Add(
            new BoundGenericNominalTypeInstance(definition.Identity,
                arguments, 0));
        _genericNominalTypeUses.Add(key, type);
        return true;
    }

    private DeclaredTypeResolution ResolveTypeName(string name,
        ImmutableArray<SyntaxNode> suppliedArguments, TextSpan useSite,
        out PrimitiveType type)
    {
        type = default;
        if (HasImportedGenericNominalTypeCandidate(name))
        {
            if (!TryFindGenericNominalTypeDefinitionForTypeTerm(name,
                out var definition))
            {
                var coreTaskCandidate = _genericNominalTypeDefinitions
                    .Any(candidate =>
                        BoundGenericNominalTypeContract
                            .IsGenericNominalTypeName(
                                candidate.Namespace + "."
                                    + candidate.Name)
                        && (name == candidate.Name
                            || name == candidate.Namespace + "."
                                + candidate.Name
                            || _usings.Contains(candidate.Namespace)));
                Report("TRM3005", coreTaskCandidate
                    ? $"Generic nominal type '{name}' requires an explicitly selected exact Core Task definition."
                    : $"Generic nominal type '{name}' is ambiguous or has no exact selected identity.",
                    useSite);
                return DeclaredTypeResolution.Invalid;
            }

            if (!TryInstantiateGenericNominalType(definition,
                suppliedArguments, useSite, out type))
            {
                type = default;
                Report("TRM3005",
                    $"Generic nominal type '{name}' has an invalid or unsupported type/constant argument list.",
                    useSite);
                return DeclaredTypeResolution.Invalid;
            }

            return DeclaredTypeResolution.Unique;
        }

        if (TryFindGenericStructTemplateForTypeTerm(name,
            out var sourceGenericTemplate)
            && !(suppliedArguments.IsEmpty
                && HasNonGenericSourceTypeCandidate(name)))
        {
            if (!TryInstantiateGenericStruct(sourceGenericTemplate,
                suppliedArguments, useSite, out type))
            {
                type = default;
                return DeclaredTypeResolution.Invalid;
            }

            return DeclaredTypeResolution.Unique;
        }

        var diagnosticCount = DiagnosticCount;
        var typeArguments = ImmutableArray.CreateBuilder<PrimitiveType>(
            suppliedArguments.Length);
        foreach (var argument in suppliedArguments)
        {
            typeArguments.Add(BindType(argument, allowVoid: false));
        }
        if (DiagnosticCount != diagnosticCount)
        {
            return DeclaredTypeResolution.Invalid;
        }

        return ResolveTypeName(name, typeArguments.MoveToImmutable(),
            useSite, out type, suppliedArguments);
    }

    private bool TryInstantiateGenericNominalType(
        BoundGenericNominalTypeDefinition definition,
        ImmutableArray<SyntaxNode> suppliedArguments,
        TextSpan useSite, out PrimitiveType type)
    {
        type = default;
        if (definition.Formals.IsEmpty)
        {
            return suppliedArguments.IsEmpty
                && TryInstantiateSourceLookupStandardClass(definition,
                    useSite, out type);
        }

        if (definition.Formals.IsDefaultOrEmpty
            || definition.Formals.Length
                > SemanticVerifier.MaximumArguments
            || suppliedArguments.Length > definition.Formals.Length)
        {
            return false;
        }

        var arguments = ImmutableArray.CreateBuilder<
            BoundGenericNominalTypeArgument>(definition.Formals.Length);
        for (var ordinal = 0; ordinal < definition.Formals.Length;
            ordinal++)
        {
            var formal = definition.Formals[ordinal];
            var syntax = ordinal < suppliedArguments.Length
                ? suppliedArguments[ordinal] : null;
            if (syntax?.Kind == SyntaxKind.GenericInferenceArgument)
            {
                return false;
            }

            var useDefault = syntax is null
                || syntax.Kind == SyntaxKind.GenericDefaultArgument;
            if (formal.Kind == BoundGenericArgumentKind.Type)
            {
                if (!formal.Constraints.IsEmpty)
                {
                    return false;
                }

                if (useDefault)
                {
                    if (formal.Default is not
                        {
                            Kind: BoundGenericArgumentKind.Type,
                            DecodedType: PrimitiveType decodedType,
                        } defaultType
                        || defaultType.ProviderIdentity
                            != definition.Identity.ProviderIdentity)
                    {
                        return false;
                    }

                    arguments.Add(
                        BoundGenericNominalTypeArgument.ForMetadataType(
                            decodedType, defaultType.TypeQualifiers,
                            defaultType.Reference,
                            defaultType.ProviderIdentity));
                    continue;
                }

                if (syntax!.Kind == SyntaxKind.ConstantGenericArgument)
                {
                    return false;
                }
                var diagnosticCount = DiagnosticCount;
                var argumentType = BindType(syntax, allowVoid: true);
                if (DiagnosticCount != diagnosticCount)
                {
                    return false;
                }
                var argument
                    = BoundGenericNominalTypeArgument.ForType(argumentType);
                if (_genericContext is GenericInstantiation generic
                    && generic.Template.FormalTypeOwnerOrdinal >= 0)
                {
                    var remaining
                        = SemanticVerifier.MaximumAnalysisVisits;
                    if (TryBuildGenericQueryTypeIdentityTerm(
                            argumentType, generic, ref remaining, 0,
                            out var typeArgumentTerm)
                        && BoundGenericMethodTypeTerms
                            .TryGetFormalDependencies(typeArgumentTerm,
                                out var formalDependencies))
                    {
                        if (typeArgumentTerm.Kind
                            == BoundGenericMethodTypeTermKind.Formal
                            && formalDependencies.Length == 1)
                        {
                            argument = argument with {
                                TypeFormalDefinitionOrdinal =
                                    generic.Template.FormalTypeOwnerOrdinal,
                                TypeFormalOrdinal = formalDependencies[0],
                                TypeFormalOwnerKind =
                                    BoundGenericTypeFormalOwnerKind
                                        .GenericMethod,
                            };
                        }
                        else if (typeArgumentTerm.Kind
                                != BoundGenericMethodTypeTermKind.Concrete)
                        {
                            argument = argument with {
                                TypeArgumentTerm = typeArgumentTerm,
                                TypeFormalDefinitionOrdinal =
                                    formalDependencies.IsEmpty
                                        ? -1
                                        : generic.Template
                                            .FormalTypeOwnerOrdinal,
                                TypeFormalOrdinal = -1,
                                TypeFormalOwnerKind =
                                    BoundGenericTypeFormalOwnerKind
                                        .GenericMethod,
                            };
                        }
                    }
                }
                arguments.Add(argument);
                continue;
            }

            if (formal.Kind != BoundGenericArgumentKind.Constant
                || !formal.Constraints.IsEmpty)
            {
                return false;
            }

            if (useDefault)
            {
                if (formal.Default is not
                    {
                        Kind: BoundGenericArgumentKind.Constant,
                    } defaultConstant)
                {
                    return false;
                }

                if (defaultConstant.DecodedConstantArgument
                    is BoundGenericNominalTypeArgument decodedConstant)
                {
                    arguments.Add(decodedConstant);
                }
                else if (defaultConstant.EarlierFormalOrdinal
                    is int formalOrdinal)
                {
                    if ((uint)formalOrdinal >= (uint)arguments.Count
                        || arguments[formalOrdinal].Kind
                            != BoundGenericArgumentKind.Constant)
                    {
                        return false;
                    }

                    arguments.Add(arguments[formalOrdinal]);
                }
                else
                {
                    arguments.Add(
                        BoundGenericNominalTypeArgument.ForConstantSource(
                            defaultConstant.ProviderIdentity,
                            defaultConstant.Reference));
                }
                continue;
            }

            if (syntax!.Kind == SyntaxKind.ConstantGenericArgument
                || syntax.Kind == SyntaxKind.TypeName)
            {
                if (!TryBindGenericNominalTypeConstantArgument(formal,
                    syntax, out var constantArgument))
                {
                    return false;
                }
                arguments.Add(constantArgument);
                continue;
            }

            return false;
        }

        return TryInstantiateGenericNominalTypeArguments(definition,
            arguments.MoveToImmutable(), useSite, out type);
    }

    private bool TryBindGenericNominalTypeConstantArgument(
        BoundGenericNominalTypeFormal formal, SyntaxNode syntax,
        out BoundGenericNominalTypeArgument argument)
    {
        argument = null!;
        if (formal.ConstantDomainType is not PrimitiveType domainType
            || !TryGetGenericMethodConstantCarrierType(domainType,
                out var carrierType))
        {
            return false;
        }
        if (formal.ConstantCarrierType is PrimitiveType formalCarrier
            && formalCarrier != carrierType)
        {
            return false;
        }

        var domainTerm = new BoundGenericMethodTypeTerm(
            BoundGenericMethodTypeTermKind.Concrete, domainType, -1, -1,
            [], false, false, false, false);
        var temporaryFormal = new GenericMethodConstantFormal(-1, -1,
            formal.Name, domainTerm, domainType, null, []);
        if (!TryBindGenericMethodConstantArgument(temporaryFormal,
            domainTerm, domainType, carrierType, syntax,
            out var constantArgument))
        {
            return false;
        }

        if (constantArgument.Kind
            == BoundGenericMethodConstantArgumentKind.Value)
        {
            argument = BoundGenericNominalTypeArgument.ForConstant(
                domainType, carrierType, constantArgument.Bits.Low,
                constantArgument.Bits.High);
            return true;
        }

        if (constantArgument.Kind
                != BoundGenericMethodConstantArgumentKind.Formal
            || constantArgument.CallerDefinitionOrdinal < 0
            || constantArgument.CallerConstantFormalOrdinal < 0
            || !AreGenericMethodTypeTermsEquivalent(
                constantArgument.Domain, domainTerm))
        {
            return false;
        }

        argument = BoundGenericNominalTypeArgument.ForConstantFormal(
            constantArgument.CallerDefinitionOrdinal,
            constantArgument.CallerConstantFormalOrdinal,
            constantArgument.Domain,
            constantArgument.CallerFormalOwnerKind);
        return true;
    }

    private bool TryResolveClass(string name, out PrimitiveType type) =>
        ResolveTypeName(name, out type) == DeclaredTypeResolution.Unique && IsClass(type);

    private DeclaredTypeResolution ResolveStaticMember(SyntaxNode receiver, string member, out string name)
    {
        var receiverName = NameText(receiver);
        DeclaredTypeResolution resolution;
        PrimitiveType type;
        if (receiver.Kind == SyntaxKind.GenericNameExpression
            && receiver.Children[0] is SyntaxTokenElement genericName
            && receiver.Children[1] is SyntaxNode typeArgumentsNode)
        {
            var typeArguments = ImmutableArray.CreateBuilder<PrimitiveType>();
            foreach (var typeArgument in typeArgumentsNode.Children
                         .OfType<SyntaxNode>())
            {
                typeArguments.Add(BindType(typeArgument,
                    allowVoid: false));
            }
            receiverName = genericName.Token.Name;
            resolution = ResolveTypeName(receiverName,
                typeArguments.ToImmutable(), receiver.Span, out type);
        }
        else
        {
            resolution = ResolveTypeName(receiverName, out type);
        }
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
            || _structNames.TryGetValue(qualified, out type)
            || _enumNames.TryGetValue(qualified, out type))
        {
            return type;
        }
        Report("TRM3005", $"Declaring type '{name}' is missing or ambiguous.", _tree.Root.Span);
        return PrimitiveType.Int32;
    }

    private void DeclareClass(int index)
    {
        var mutableClass = _classes[index].IsMutable;
        var fields = ImmutableArray.CreateBuilder<BoundStructField>();
        var offset = 0;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (tree, declaration) in _classDeclarationParts[index])
        {
            SetContext(tree, declaration);
            DeclareClassPart(index, declaration, mutableClass, fields,
                names, ref offset);
        }

        var (primaryTree, primaryDeclaration) = _classDeclarations[index];
        SetContext(primaryTree, primaryDeclaration);
        if (fields.Count == 0)
        {
            Report("TRM3005",
                "A managed class requires initialized instance fields.",
                primaryDeclaration.Span);
        }
        if (fields.Count > ManagedClassTypes.MaximumFields)
        {
            Report("TRM3005",
                $"A managed class supports at most {ManagedClassTypes.MaximumFields} fields.",
                primaryDeclaration.Span);
        }

        _classes[index] = new BoundClass(_classes[index].Name,
            fields.ToImmutable(), (offset + 7) & ~7) {
            IsInternal = _classes[index].IsInternal,
            IsMutable = mutableClass,
            IsUnique = _classes[index].IsUnique,
            HasCleanup = _classes[index].HasCleanup,
            CleanupMethod = _classes[index].CleanupMethod,
        };
    }

    private void DeclareClassPart(int index, SyntaxNode declaration,
        bool mutableClass, ImmutableArray<BoundStructField>.Builder fields,
        HashSet<string> names, ref int offset)
    {
        foreach (var child in declaration.Children)
        {
            if (child is SyntaxNode {
                    Kind: SyntaxKind.Attribute,
                } attribute)
            {
                if (!IsRegisteredLazyFieldSelector(attribute))
                {
                    Report("TRM3001",
                        "Managed classes do not support this attribute or its target.",
                        child.Span);
                }
            }
            else if (child is SyntaxNode {
                    Kind: SyntaxKind.OperatorDeclaration,
                })
            {
                Report("TRM3001",
                    "Managed classes do not support operators.",
                    child.Span);
            }
            if (child is SyntaxTokenElement token)
            {
                if (token.Token.Kind is Terrum.CodeAnalysis.Lexing.TokenKind.StaticKeyword
                    or Terrum.CodeAnalysis.Lexing.TokenKind.PrivateKeyword
                    or Terrum.CodeAnalysis.Lexing.TokenKind.UnsafeKeyword)
                {
                    Report("TRM3001",
                        "Classes support public/internal visibility and optional mutability or uniqueness.",
                        child.Span);
                }
            }
        }
        foreach (var child in declaration.Children.OfType<SyntaxNode>())
        {
            if (child.Kind == SyntaxKind.PropertyDeclaration)
            {
                DeclareClassPropertyBacking(index, child, mutableClass,
                    fields, names, ref offset);
                continue;
            }
            if (child.Kind != SyntaxKind.FieldDeclaration)
            {
                continue;
            }
            if (child.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Kind
                    == Terrum.CodeAnalysis.Lexing.TokenKind.StaticKeyword))
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
            if (BoundGenericNominalTypeTypes.IsType(type))
            {
                Report("TRM3005",
                    "Generic nominal values do not have a supported class storage layout.",
                    child.Span);
                continue;
            }
            if (!_classes[index].IsInternal && ContainsGenericStructSpecialization(type))
            {
                Report("TRM3005",
                    "Public classes cannot expose compilation-local generic struct specializations.",
                    child.Span);
            }
            if (type is PrimitiveType.OwnedInt32Buffer or PrimitiveType.ReadonlyInt32BufferView
                or PrimitiveType.MutableInt32BufferView or PrimitiveType.TokenSpan || IsUnique(type))
            {
                Report("TRM3023",
                    "Class fields cannot contain unique, buffer, or non-value runtime storage.",
                    child.Span);
            }
            var width = ValueLayout.Size(type, _structs,
                [.. _checkedReferenceTypes], _nativeLoweringAgreement,
                _nativeFixedArrayTypes, _enumDefinitions);
            var alignment = width == 0 ? 1 : ValueAlignment(type);
            offset = (offset + alignment - 1) & -alignment;
            var maximumFieldSize = _nativeLoweringAgreement
                == CompilationOptions.WindowsX64NormalBackingAgreement
                ? ValueLayout.MaximumAddressStorageSize : ValueLayout.MaximumSize;
            if (width is < 1 || width > (ulong)maximumFieldSize
                || (ulong)offset + width
                    > (ulong)ManagedClassTypes.MaximumPayload
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
            fields.Add(new BoundStructField(fieldToken.Token.Name, type,
                checked((ulong)offset), width) {
                IsMutable = mutable,
                IsPrivate = !isPublic && !isInternal,
                IsInternal = isInternal,
                RequiresUnsafeCaller = unsafeModifiers != 0,
            });
            offset = checked(offset + (int)width);
        }
    }

    private void DeclareClassPropertyBacking(int classIndex,
        SyntaxNode declaration, bool mutableClass,
        ImmutableArray<BoundStructField>.Builder fields,
        HashSet<string> names, ref int offset)
    {
        var isStatic = declaration.Children
            .OfType<SyntaxTokenElement>()
            .Any(static token =>
                token.Token.Kind == TokenKind.StaticKeyword);
        if (isStatic || !HasPropertyBacking(declaration))
        {
            return;
        }

        var nameToken = FindStaticFieldNameToken(declaration);
        var typeNode = declaration.Children.OfType<SyntaxNode>()
            .FirstOrDefault(static child =>
                child.Kind is not (SyntaxKind.Attribute
                    or SyntaxKind.FriendVisibility));
        if (nameToken is null || typeNode is null)
        {
            Report("TRM3005",
                "A backed class property requires a valid type and name.",
                declaration.Span);
            return;
        }

        var propertyName = nameToken.Name;
        var type = BindType(typeNode, allowVoid: false);
        if (BoundGenericNominalTypeTypes.IsType(type))
        {
            Report("TRM3005",
                "Generic nominal values do not have a supported class storage layout.",
                declaration.Span);
            return;
        }
        if (!_classes[classIndex].IsInternal
            && ContainsGenericStructSpecialization(type))
        {
            Report("TRM3005",
                "Public classes cannot expose compilation-local generic struct specializations.",
                declaration.Span);
        }
        if (type is PrimitiveType.OwnedInt32Buffer
            or PrimitiveType.ReadonlyInt32BufferView
            or PrimitiveType.MutableInt32BufferView or PrimitiveType.TokenSpan
            || IsUnique(type))
        {
            Report("TRM3023",
                "Class property backing cannot contain unique, buffer, or non-value runtime storage.",
                declaration.Span);
            return;
        }

        var width = ValueLayout.Size(type, _structs,
            [.. _checkedReferenceTypes], _nativeLoweringAgreement,
            _nativeFixedArrayTypes, _enumDefinitions);
        var alignment = width == 0 ? 1 : ValueAlignment(type);
        offset = (offset + alignment - 1) & -alignment;
        var maximumFieldSize = _nativeLoweringAgreement
            == CompilationOptions.WindowsX64NormalBackingAgreement
            ? ValueLayout.MaximumAddressStorageSize : ValueLayout.MaximumSize;
        var backingName = "$property:" + propertyName;
        if (width is < 1 || width > (ulong)maximumFieldSize
            || (ulong)offset + width
                > (ulong)ManagedClassTypes.MaximumPayload
            || !names.Add(backingName))
        {
            Report("TRM3005",
                "Managed property backing must fit the bounded class payload and have a unique identity.",
                declaration.Span);
            return;
        }

        var mutableBacking = HasMutableOrdinaryPropertyAccessor(
            declaration);
        if (mutableBacking && !mutableClass)
        {
            Report("TRM3008",
                "Mutable property backing requires a mut class declaration.",
                declaration.Span);
        }

        fields.Add(new BoundStructField(backingName, type,
            checked((ulong)offset), width) {
            BackingPropertyName = propertyName,
            IsMutable = mutableBacking,
            IsPrivate = true,
        });
        offset = checked(offset + (int)width);
    }

    private int FindClassPropertyBackingField(PrimitiveType ownerType,
        string propertyName)
    {
        if (!ManagedClassTypes.IsClass(ownerType))
        {
            return -1;
        }

        var fields = _classes[ManagedClassTypes.ClassIndex(ownerType)]
            .Fields;
        for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
        {
            if (StringComparer.Ordinal.Equals(
                    fields[fieldIndex].BackingPropertyName, propertyName))
            {
                return fieldIndex;
            }
        }

        return -1;
    }

    private static bool HasPropertyFieldReference(SyntaxNode property)
    {
        foreach (var accessor in property.Children.OfType<SyntaxNode>()
            .Where(static child => child.Kind is SyntaxKind.GetAccessor
                or SyntaxKind.SetAccessor or SyntaxKind.InitAccessor))
        {
            var pending = new Stack<SyntaxNode>();
            pending.Push(accessor);
            while (pending.TryPop(out var current))
            {
                foreach (var child in current.Children)
                {
                    if (child is SyntaxTokenElement token
                        && token.Token.Text == "field")
                    {
                        return true;
                    }
                    if (child is SyntaxNode nested)
                    {
                        pending.Push(nested);
                    }
                }
            }
        }

        return false;
    }

    private static bool HasPropertyBacking(SyntaxNode property)
        => IsAutoGetterProperty(property)
            || HasPropertyFieldReference(property)
            || property.Children.OfType<SyntaxNode>().Any(
                static accessor =>
                    (accessor.Kind is SyntaxKind.SetAccessor
                        or SyntaxKind.InitAccessor)
                    && accessor.Children.LastOrDefault()
                        is SyntaxTokenElement {
                            Token.Kind: TokenKind.Semicolon,
                        });

    private static bool HasMutableOrdinaryPropertyAccessor(
        SyntaxNode property)
    {
        foreach (var accessor in property.Children.OfType<SyntaxNode>()
            .Where(static child => child.Kind is SyntaxKind.GetAccessor
                or SyntaxKind.SetAccessor))
        {
            if (!accessor.Children.OfType<SyntaxTokenElement>()
                .Any(static token =>
                    token.Token.Kind == TokenKind.MutKeyword))
            {
                continue;
            }

            var isAuto = accessor.Children.LastOrDefault()
                is SyntaxTokenElement {
                    Token.Kind: TokenKind.Semicolon,
                };
            if (isAuto
                ? accessor.Kind == SyntaxKind.SetAccessor
                : HasPropertyFieldReference(accessor))
            {
                return true;
            }
        }

        return false;
    }

    private void DeclareClassFactories()
    {
        foreach (var (index, (tree, declaration)) in _classDeclarations)
        {
            SetContext(tree, declaration);
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
                    || !SemanticVerifier.IsClassArgumentType(parameter.Type, _structs,
                        _enumDefinitions.Count)))
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
                name, false, signature.IsPrivate, !signature.IsPrivate, false, true, signature.NoFailureProof, false) {
                ClassConstructor = constructor,
                RequiresUnsafeCaller = signature.RequiresUnsafeCaller,
            });
        }
    }

    private BoundFunction CreateClassFactory(Signature signature)
    {
        var type = signature.StructName is not null
            && _classNames.TryGetValue(signature.StructName, out var classType)
            ? classType : (PrimitiveType)(ManagedClassTypes.First - 1);
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
            NoFailureProof = signature.NoFailureProof,
            RequiresUnsafeCaller = signature.RequiresUnsafeCaller,
            Intrinsic = BoundIntrinsic.ClassTryCreate,
            ClassConstructor = signature.ClassConstructor,
        };
    }
}
