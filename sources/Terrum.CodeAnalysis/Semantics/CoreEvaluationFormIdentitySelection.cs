// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Terrum.CodeAnalysis.Lexing;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>Source declaration categories supported by a selected evaluation-form schema.</summary>
public enum CoreEvaluationFormTargetKind
{
    /// <summary>No target category; invalid in a schema.</summary>
    None = 0,
    /// <summary>An ordinary named callable.</summary>
    OrdinaryNamedCallable = 1,
    /// <summary>One selected property accessor.</summary>
    IndividualAccessor = 2,
    /// <summary>An eligible constructor that initializes a vacant result receiver.</summary>
    EligibleInitializationConstructor = 3,
}

/// <summary>An exact provider Module and export key for one evaluation-form schema.</summary>
/// <param name="ProviderIdentity">The selected provider Module identity.</param>
/// <param name="ExportKey">The exact exported schema key.</param>
public readonly record struct CoreEvaluationFormSchemaSelector(
    string ProviderIdentity, string ExportKey);

/// <summary>An actual immutable zero-argument Pure or ConstEval schema declaration.</summary>
public sealed class CoreEvaluationFormSchemaDeclaration
{
    /// <summary>Creates a schema descriptor; its complete contract is validated by the owning catalog.</summary>
    /// <param name="qualifiedName">The source-qualified declaration name.</param>
    /// <param name="genericArity">The schema generic arity.</param>
    /// <param name="argumentCount">The complete schema argument count.</param>
    /// <param name="targets">The complete set of supported declaration target categories.</param>
    /// <param name="isPublic">Whether the schema declaration is public.</param>
    /// <param name="isPrimary">Whether the schema declaration is primary.</param>
    /// <param name="isPrimaryTargetOnly">Whether the annotated declaration must be primary-authored.</param>
    /// <param name="isRepeatable">Whether the schema may be repeated on one declaration.</param>
    public CoreEvaluationFormSchemaDeclaration(string qualifiedName,
        int genericArity, int argumentCount,
        ImmutableArray<CoreEvaluationFormTargetKind> targets,
        bool isPublic, bool isPrimary, bool isPrimaryTargetOnly,
        bool isRepeatable)
    {
        QualifiedName = qualifiedName;
        GenericArity = genericArity;
        ArgumentCount = argumentCount;
        Targets = targets;
        IsPublic = isPublic;
        IsPrimary = isPrimary;
        IsPrimaryTargetOnly = isPrimaryTargetOnly;
        IsRepeatable = isRepeatable;
    }

    /// <summary>The explicitly supplied source-qualified declaration name.</summary>
    public string QualifiedName { get; }
    /// <summary>The schema generic arity.</summary>
    public int GenericArity { get; }
    /// <summary>The complete schema argument count.</summary>
    public int ArgumentCount { get; }
    /// <summary>The complete set of supported declaration target categories.</summary>
    public ImmutableArray<CoreEvaluationFormTargetKind> Targets { get; }
    /// <summary>Whether the schema declaration is public.</summary>
    public bool IsPublic { get; }
    /// <summary>Whether the schema declaration is primary.</summary>
    public bool IsPrimary { get; }
    /// <summary>Whether the annotated declaration must be primary-authored.</summary>
    public bool IsPrimaryTargetOnly { get; }
    /// <summary>Whether the schema may be repeated on one declaration.</summary>
    public bool IsRepeatable { get; }
}

/// <summary>An immutable host-declared evaluation-form schema catalog for one provider Module.</summary>
public sealed class CoreEvaluationFormSchemaDeclarations
{
    private CoreEvaluationFormSchemaDeclarations(
        CoreProviderModuleOrigin origin,
        ImmutableArray<CoreEvaluationFormSchemaDeclaration> declarations,
        ImmutableArray<KeyValuePair<string,
            CoreEvaluationFormSchemaDeclaration>> exports,
        ImmutableDictionary<string, CoreEvaluationFormSchemaDeclaration>
            declarationsByExportKey)
    {
        Origin = origin;
        Declarations = declarations;
        Exports = exports;
        _declarationsByExportKey = declarationsByExportKey;
    }

    private readonly ImmutableDictionary<string,
        CoreEvaluationFormSchemaDeclaration> _declarationsByExportKey;

    /// <summary>The exact provider Module and complete schema/catalog requirements.</summary>
    public CoreProviderModuleOrigin Origin { get; }
    /// <summary>The actual schema declaration objects owned by this catalog.</summary>
    public ImmutableArray<CoreEvaluationFormSchemaDeclaration>
        Declarations { get; }
    /// <summary>The exact export keys and their actual catalog declaration objects.</summary>
    public ImmutableArray<KeyValuePair<string,
        CoreEvaluationFormSchemaDeclaration>> Exports { get; }

    /// <summary>Creates a complete catalog after validating declarations, aliases and export ownership.</summary>
    /// <exception cref="ArgumentException">The origin, declarations or exports are invalid or incomplete.</exception>
    public static CoreEvaluationFormSchemaDeclarations Create(
        CoreProviderModuleOrigin origin,
        ImmutableArray<CoreEvaluationFormSchemaDeclaration> declarations,
        ImmutableArray<KeyValuePair<string,
            CoreEvaluationFormSchemaDeclaration>> exports)
    {
        if (!IsValidOrigin(origin) || declarations.IsDefaultOrEmpty
            || exports.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "The evaluation-form schema catalog is incomplete.",
                nameof(declarations));
        }

        var declarationSet = new HashSet<
            CoreEvaluationFormSchemaDeclaration>(
            ReferenceEqualityComparer.Instance);
        var declarationNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            if (declaration is null
                || !IsValidDeclaration(declaration)
                || !declarationSet.Add(declaration)
                || !declarationNames.Add(declaration.QualifiedName))
            {
                throw new ArgumentException(
                    "The evaluation-form schema catalog has an invalid or duplicate declaration.",
                    nameof(declarations));
            }
        }

        var exportsByKey = ImmutableDictionary.CreateBuilder<string,
            CoreEvaluationFormSchemaDeclaration>(StringComparer.Ordinal);
        var exportedDeclarations = new HashSet<
            CoreEvaluationFormSchemaDeclaration>(
            ReferenceEqualityComparer.Instance);
        foreach (var (exportKey, declaration) in exports)
        {
            if (!IsValidIdentity(exportKey) || declaration is null
                || !declarationSet.Contains(declaration)
                || !exportsByKey.TryAdd(exportKey, declaration))
            {
                throw new ArgumentException(
                    "The evaluation-form schema catalog has an invalid or duplicate export.",
                    nameof(exports));
            }

            _ = exportedDeclarations.Add(declaration);
        }

        if (exportedDeclarations.Count != declarationSet.Count)
        {
            throw new ArgumentException(
                "Every evaluation-form schema declaration must have an export.",
                nameof(exports));
        }

        return new CoreEvaluationFormSchemaDeclarations(origin,
            declarations, exports, exportsByKey.ToImmutable());
    }

    /// <summary>Resolves an exact export key to its actual declaration object.</summary>
    public bool TryGetDeclaration(string exportKey,
        out CoreEvaluationFormSchemaDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(exportKey);
        return _declarationsByExportKey.TryGetValue(exportKey,
            out declaration!);
    }

    private static bool IsValidOrigin(CoreProviderModuleOrigin origin)
        => IsValidIdentity(origin.ModuleIdentity)
            && IsValidIdentity(origin.RevisionIdentity)
            && IsValidIdentity(origin.Schema.Identity)
            && origin.Schema.Revision != 0
            && origin.GoverningSemanticEdition == 1
            && origin.TargetRequirement <= 1
            && origin.RuntimePolicy <= 1;

    private static bool IsValidDeclaration(
        CoreEvaluationFormSchemaDeclaration declaration)
        => IsValidQualifiedName(declaration.QualifiedName)
            && declaration.GenericArity == 0
            && declaration.ArgumentCount == 0
            && IsExactTargetSet(declaration.Targets)
            && declaration.IsPublic
            && declaration.IsPrimary
            && declaration.IsPrimaryTargetOnly
            && !declaration.IsRepeatable;

    private static bool IsExactTargetSet(
        ImmutableArray<CoreEvaluationFormTargetKind> targets)
    {
        if (targets.IsDefault || targets.Length != 3)
        {
            return false;
        }

        var observed = new HashSet<CoreEvaluationFormTargetKind>();
        foreach (var target in targets)
        {
            if (target is not (CoreEvaluationFormTargetKind
                    .OrdinaryNamedCallable
                or CoreEvaluationFormTargetKind.IndividualAccessor
                or CoreEvaluationFormTargetKind
                    .EligibleInitializationConstructor)
                || !observed.Add(target))
            {
                return false;
            }
        }

        return observed.Count == 3;
    }

    internal static bool IsValidIdentity(string? identity)
    {
        if (string.IsNullOrEmpty(identity)
            || identity.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            _ = s_utf8.GetByteCount(identity);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static readonly UTF8Encoding s_utf8 = new(false, true);

    private static bool IsValidQualifiedName(string? qualifiedName)
    {
        if (!IsValidIdentity(qualifiedName))
        {
            return false;
        }

        var segments = qualifiedName!.Split('.');
        foreach (var segment in segments)
        {
            if (!IsValidNameSegment(segment))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidNameSegment(string segment)
    {
        var spelling = segment.AsSpan();
        if (spelling.IsEmpty)
        {
            return false;
        }

        if (spelling[0] == '@')
        {
            if (!IdentifierName.IsEscape(spelling))
            {
                return false;
            }

            spelling = spelling[1..];
        }
        else if (IdentifierName.CanEscape(spelling))
        {
            return false;
        }

        for (var position = 0; position < spelling.Length;)
        {
            var status = Rune.DecodeFromUtf16(spelling[position..],
                out var rune, out var width);
            if (status != OperationStatus.Done
                || (position == 0
                    ? rune.Value != '_' && !UnicodeXid.IsStart(rune.Value)
                    : rune.Value != '_' && !UnicodeXid.IsContinue(rune.Value)))
            {
                return false;
            }

            position += width;
        }

        return true;
    }
}

internal readonly record struct CoreEvaluationFormSchemaDefinitionIdentity(
    CoreEvaluationFormSchemaDeclarations Catalog,
    CoreEvaluationFormSchemaDeclaration Declaration);

internal sealed record BoundEvaluationFormSchemaBinding(
    string UsingModuleIdentity,
    CoreEvaluationFormSchemaSelector Selector,
    CoreEvaluationFormSchemaDefinitionIdentity Definition);

/// <summary>Exact host-selected Pure and ConstEval schema identities for one using Module.</summary>
/// <param name="UsingModuleIdentity">The logical Module whose source binding uses this selection.</param>
/// <param name="PureSchema">The optional exact Pure provider/export selection.</param>
/// <param name="ConstEvalSchema">The optional exact ConstEval provider/export selection.</param>
/// <param name="LocalSchemaDeclarations">The actual local declaration catalog, when a selected role is local.</param>
public sealed record CoreEvaluationFormIdentitySelection(
    string UsingModuleIdentity,
    CoreEvaluationFormSchemaSelector? PureSchema = null,
    CoreEvaluationFormSchemaSelector? ConstEvalSchema = null,
    CoreEvaluationFormSchemaDeclarations? LocalSchemaDeclarations = null);

internal static class CoreEvaluationFormIdentitySelectionContract
{
    internal static bool IsValidTable(
        ImmutableArray<CoreEvaluationFormIdentitySelection> selections)
    {
        if (selections.IsDefaultOrEmpty)
        {
            return true;
        }

        var usingModules = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selection in selections)
        {
            if (selection is null
                || !CoreEvaluationFormSchemaDeclarations.IsValidIdentity(
                    selection.UsingModuleIdentity)
                || (selection.PureSchema is null
                    && selection.ConstEvalSchema is null)
                || !usingModules.Add(selection.UsingModuleIdentity))
            {
                return false;
            }

            var localPure = selection.PureSchema is
                CoreEvaluationFormSchemaSelector pure
                && StringComparer.Ordinal.Equals(
                    pure.ProviderIdentity, selection.UsingModuleIdentity);
            var localConstEval = selection.ConstEvalSchema is
                CoreEvaluationFormSchemaSelector constEval
                && StringComparer.Ordinal.Equals(
                    constEval.ProviderIdentity,
                    selection.UsingModuleIdentity);
            if (selection.LocalSchemaDeclarations
                    is CoreEvaluationFormSchemaDeclarations localSchemas)
            {
                if ((!localPure && !localConstEval)
                    || !StringComparer.Ordinal.Equals(
                        localSchemas.Origin.ModuleIdentity,
                        selection.UsingModuleIdentity))
                {
                    return false;
                }

                var selectedDeclarations = new HashSet<
                    CoreEvaluationFormSchemaDeclaration>(
                    ReferenceEqualityComparer.Instance);
                if (localPure
                    && (!IsValidSelector(selection.PureSchema!.Value)
                        || !localSchemas.TryGetDeclaration(
                            selection.PureSchema.Value.ExportKey,
                            out var pureDeclaration)
                        || !selectedDeclarations.Add(pureDeclaration)))
                {
                    return false;
                }
                if (localConstEval
                    && (!IsValidSelector(selection.ConstEvalSchema!.Value)
                        || !localSchemas.TryGetDeclaration(
                            selection.ConstEvalSchema.Value.ExportKey,
                            out var constEvalDeclaration)
                        || !selectedDeclarations.Add(constEvalDeclaration)))
                {
                    return false;
                }
                if (selectedDeclarations.Count
                    != localSchemas.Declarations.Length)
                {
                    return false;
                }
            }
            else if (localPure || localConstEval)
            {
                return false;
            }

            if ((selection.PureSchema is
                    CoreEvaluationFormSchemaSelector selectedPure
                && !IsValidSelector(selectedPure))
                || (selection.ConstEvalSchema is
                    CoreEvaluationFormSchemaSelector selectedConstEval
                    && !IsValidSelector(selectedConstEval)))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsValidProviderTable(
        ImmutableArray<CoreEvaluationFormSchemaDeclarations> providers)
    {
        if (providers.IsDefaultOrEmpty)
        {
            return true;
        }

        var providerModules = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            if (provider is null
                || !CoreEvaluationFormSchemaDeclarations.IsValidIdentity(
                    provider.Origin.ModuleIdentity)
                || !providerModules.Add(provider.Origin.ModuleIdentity))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsValidAgainstProviders(
        ImmutableArray<CoreEvaluationFormIdentitySelection> selections,
        ImmutableArray<CoreEvaluationFormSchemaDeclarations> providers)
    {
        selections = selections.IsDefault ? [] : selections;
        providers = providers.IsDefault ? [] : providers;
        if (!IsValidTable(selections) || !IsValidProviderTable(providers))
        {
            return false;
        }

        var providersByModule = new Dictionary<string,
            CoreEvaluationFormSchemaDeclarations>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            if (!providersByModule.TryAdd(provider.Origin.ModuleIdentity,
                provider))
            {
                return false;
            }
        }

        foreach (var selection in selections)
        {
            if (selection.LocalSchemaDeclarations
                    is CoreEvaluationFormSchemaDeclarations localSchemas
                && providersByModule.TryGetValue(
                    localSchemas.Origin.ModuleIdentity,
                    out var providerCatalog)
                && !ReferenceEquals(providerCatalog, localSchemas))
            {
                return false;
            }

            CoreEvaluationFormSchemaDefinitionIdentity? pureDefinition
                = null;
            CoreEvaluationFormSchemaDefinitionIdentity? constEvalDefinition
                = null;
            if (selection.PureSchema is
                    CoreEvaluationFormSchemaSelector pure
                && !TryResolveDefinition(selection, pure, providersByModule,
                    out pureDefinition))
            {
                return false;
            }
            if (selection.ConstEvalSchema is
                    CoreEvaluationFormSchemaSelector constEval
                && !TryResolveDefinition(selection, constEval, providersByModule,
                    out constEvalDefinition))
            {
                return false;
            }
            if (pureDefinition is CoreEvaluationFormSchemaDefinitionIdentity
                    selectedPureDefinition
                && constEvalDefinition
                    is CoreEvaluationFormSchemaDefinitionIdentity
                    selectedConstEvalDefinition
                && ReferenceEquals(selectedPureDefinition.Catalog,
                    selectedConstEvalDefinition.Catalog)
                && ReferenceEquals(selectedPureDefinition.Declaration,
                    selectedConstEvalDefinition.Declaration))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsValidBoundFormBinding(
        BoundEvaluationFormSchemaBinding? binding, bool constEval,
        ImmutableArray<CoreEvaluationFormIdentitySelection> selections,
        ImmutableArray<CoreEvaluationFormSchemaDeclarations> providers)
    {
        if (binding is null)
        {
            return true;
        }

        selections = selections.IsDefault ? [] : selections;
        providers = providers.IsDefault ? [] : providers;
        if (!IsValidTable(selections) || !IsValidProviderTable(providers)
            || !CoreEvaluationFormSchemaDeclarations.IsValidIdentity(
                binding.UsingModuleIdentity))
        {
            return false;
        }

        CoreEvaluationFormIdentitySelection? selectedModule = null;
        foreach (var selection in selections)
        {
            if (StringComparer.Ordinal.Equals(
                selection.UsingModuleIdentity,
                binding.UsingModuleIdentity))
            {
                selectedModule = selection;
                break;
            }
        }

        if (selectedModule is null)
        {
            return false;
        }

        var selector = constEval
            ? selectedModule.ConstEvalSchema
            : selectedModule.PureSchema;
        if (selector is not CoreEvaluationFormSchemaSelector selected
            || selected != binding.Selector)
        {
            return false;
        }

        var providersByModule = new Dictionary<string,
            CoreEvaluationFormSchemaDeclarations>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            if (!providersByModule.TryAdd(provider.Origin.ModuleIdentity,
                provider))
            {
                return false;
            }
        }

        return TryResolveDefinition(selectedModule, selected,
                providersByModule, out var definition)
            && definition is CoreEvaluationFormSchemaDefinitionIdentity
                resolved
            && ReferenceEquals(resolved.Catalog,
                binding.Definition.Catalog)
            && ReferenceEquals(resolved.Declaration,
                binding.Definition.Declaration);
    }

    internal static bool AreSelectedRolesUsed(
        CoreEvaluationFormIdentitySelection? selection,
        BoundProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (selection is null)
        {
            return true;
        }

        var pureUsed = selection.PureSchema is null
            || HasSelectedRoleUse(constEval: false);
        var constEvalUsed = selection.ConstEvalSchema is null
            || HasSelectedRoleUse(constEval: true);
        return pureUsed && constEvalUsed;

        bool HasSelectedRoleUse(bool constEval)
        {
            foreach (var function in program.Functions)
            {
                var binding = constEval
                    ? function.ConstEvalSchema
                    : function.PureEvaluationFormSchema;
                if (binding is not null
                    && StringComparer.Ordinal.Equals(
                        binding.UsingModuleIdentity,
                        selection.UsingModuleIdentity)
                    && IsValidBoundFormBinding(binding, constEval,
                        program.CoreEvaluationFormIdentitySelections,
                        program.CoreEvaluationFormSchemaProviders))
                {
                    return true;
                }
            }

            foreach (var signature in program.GenericMethodSignatures)
            {
                var binding = constEval
                    ? signature.ConstEvalSchema
                    : signature.PureEvaluationFormSchema;
                if (binding is not null
                    && StringComparer.Ordinal.Equals(
                        binding.UsingModuleIdentity,
                        selection.UsingModuleIdentity)
                    && IsValidBoundFormBinding(binding, constEval,
                        program.CoreEvaluationFormIdentitySelections,
                        program.CoreEvaluationFormSchemaProviders))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static bool TryResolveDefinition(
        CoreEvaluationFormIdentitySelection selection,
        CoreEvaluationFormSchemaSelector selector,
        Dictionary<string, CoreEvaluationFormSchemaDeclarations>
            providersByModule,
        out CoreEvaluationFormSchemaDefinitionIdentity? definition)
    {
        definition = null;
        CoreEvaluationFormSchemaDeclarations? catalog;
        if (StringComparer.Ordinal.Equals(selector.ProviderIdentity,
            selection.UsingModuleIdentity))
        {
            catalog = selection.LocalSchemaDeclarations;
        }
        else if (!providersByModule.TryGetValue(
            selector.ProviderIdentity, out catalog))
        {
            return false;
        }

        if (catalog is null
            || !StringComparer.Ordinal.Equals(
                catalog.Origin.ModuleIdentity,
                selector.ProviderIdentity)
            || !catalog.TryGetDeclaration(selector.ExportKey,
                out var resolved))
        {
            return false;
        }

        definition = new(catalog, resolved);
        return true;
    }

    internal static bool IsRuntimePolicyCompatible(
        CoreEvaluationFormIdentitySelection? selection,
        ImmutableArray<CoreEvaluationFormSchemaDeclarations> providers,
        uint runtimePolicy)
    {
        if (selection is null)
        {
            return true;
        }

        providers = providers.IsDefault ? [] : providers;
        return IsCompatible(selection.PureSchema)
            && IsCompatible(selection.ConstEvalSchema);

        bool IsCompatible(CoreEvaluationFormSchemaSelector? selector)
        {
            if (selector is not CoreEvaluationFormSchemaSelector selected)
            {
                return true;
            }

            if (StringComparer.Ordinal.Equals(selected.ProviderIdentity,
                selection.UsingModuleIdentity))
            {
                return selection.LocalSchemaDeclarations is
                        CoreEvaluationFormSchemaDeclarations local
                    && local.Origin.RuntimePolicy == runtimePolicy;
            }

            foreach (var provider in providers)
            {
                if (StringComparer.Ordinal.Equals(
                        provider.Origin.ModuleIdentity,
                        selected.ProviderIdentity))
                {
                    return provider.Origin.RuntimePolicy == runtimePolicy;
                }
            }

            return false;
        }
    }

    internal static bool AreRuntimePoliciesCompatible(
        ImmutableArray<CoreEvaluationFormIdentitySelection> selections,
        ImmutableArray<CoreEvaluationFormSchemaDeclarations> providers,
        uint runtimePolicy)
    {
        selections = selections.IsDefault ? [] : selections;
        foreach (var selection in selections)
        {
            if (!IsRuntimePolicyCompatible(selection, providers,
                runtimePolicy))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidSelector(
        CoreEvaluationFormSchemaSelector selector)
        => CoreEvaluationFormSchemaDeclarations.IsValidIdentity(
                selector.ProviderIdentity)
            && CoreEvaluationFormSchemaDeclarations.IsValidIdentity(
                selector.ExportKey);
}
