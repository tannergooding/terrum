// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Explicitly assigns an ExportKey to one checked source class declaration.</summary>
/// <param name="Program">The exact checked program containing the declaration.</param>
/// <param name="SourceInputOrdinal">The declaration's zero-based source input ordinal.</param>
/// <param name="DeclarationSpan">The exact full primary ClassDeclaration span.</param>
/// <param name="ExportKey">The producer-selected TRRM export key.</param>
/// <param name="MemberExports">The explicit keys for public direct class members.</param>
/// <remarks>
/// The C1M1 type-only producer emits keys for class TypeDefs and mapped
/// public fields; callable member keys require a construction successor.
/// </remarks>
public sealed record TrRmSourceClassExport(
    BoundProgram Program,
    int SourceInputOrdinal,
    TextSpan DeclarationSpan,
    string ExportKey,
    ImmutableArray<TrRmSourceClassMemberExport> MemberExports);

/// <summary>Explicitly assigns an ExportKey to one direct source class member.</summary>
/// <param name="SourceInputOrdinal">The member's zero-based source input ordinal.</param>
/// <param name="DeclarationSpan">The exact full member declaration span.</param>
/// <param name="ExportKey">The producer-selected TRRM export key.</param>
public sealed record TrRmSourceClassMemberExport(
    int SourceInputOrdinal,
    TextSpan DeclarationSpan,
    string ExportKey)
{
    /// <summary>Gets the explicit ExportKey for a complete-construction projection.</summary>
    public string? ConstructionProjectionExportKey { get; init; }
}

public static partial class TrRmExecutableCompiler
{
    internal enum SourceClassMemberKind : byte
    {
        Field,
        Constructor,
        Method,
        Property,
    }

    internal sealed record SourceClassMemberExport(
        SyntaxNode Declaration, SourceClassMemberKind Kind,
        string ExportKey, string? ConstructionProjectionExportKey,
        int FieldIndex, int FunctionIndex)
    {
        internal int BackingFieldIndex { get; init; } = -1;
        internal int SetterFunctionIndex { get; init; } = -1;
        internal int InitializerFunctionIndex { get; init; } = -1;
    }

    internal sealed record SourceClassExport(
        BoundSourceClassDeclaration Declaration, string ExportKey,
        ImmutableArray<SourceClassMemberExport> MemberExports);

    internal sealed class SourceClassExports(
        BoundProgram program,
        ImmutableArray<SourceClassExport> exports)
    {
        internal BoundProgram Program { get; } = program;
        internal ImmutableArray<SourceClassExport> Exports { get; } = exports;

        internal static SourceClassExports Capture(BoundProgram program,
            string moduleIdentity,
            IReadOnlyList<TrRmSourceTypeExport> typeExports,
            IReadOnlyList<TrRmSourceGenericMethodExport> genericMethodExports,
            IReadOnlyList<TrRmSourceClassExport> classExports,
            IReadOnlyList<TrRmSourceEnumDeclarationExport>? enumExports = null,
            bool requirePublicCallableKeys = false)
        {
            ArgumentNullException.ThrowIfNull(program);
            ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
            ArgumentNullException.ThrowIfNull(typeExports);
            ArgumentNullException.ThrowIfNull(genericMethodExports);
            ArgumentNullException.ThrowIfNull(classExports);

            var association = program.SourceClassProgramAssociation;
            if (association is null
                || !ReferenceEquals(association.Program, program)
                || association.Declarations.IsDefault
                || association.Declarations.Any(declaration =>
                    declaration.ClassIndex < 0
                    || declaration.ClassIndex >= program.Classes.Length
                    || declaration.SourceInputOrdinal < 0
                    || declaration.SourceInputOrdinal
                        >= program.SourceInputCount
                    || declaration.Declaration.Kind
                        != SyntaxKind.ClassDeclaration
                    || declaration.Declaration.Span != declaration.Span
                    || declaration.Parts.IsDefaultOrEmpty
                    || declaration.Parts.Count(static part =>
                        part.IsPrimary) != 1
                    || !declaration.Parts.Any(part =>
                        part.IsPrimary
                        && part.SourceInputOrdinal
                            == declaration.SourceInputOrdinal
                        && part.Span == declaration.Span
                        && ReferenceEquals(part.Declaration,
                            declaration.Declaration))
                    || declaration.Parts.Any(part =>
                        part.SourceInputOrdinal < 0
                        || part.SourceInputOrdinal
                            >= program.SourceInputCount
                        || part.Declaration.Kind
                            != SyntaxKind.ClassDeclaration
                        || part.Declaration.Span != part.Span)
                    || !ReferenceEquals(program.Classes[
                        declaration.ClassIndex],
                        declaration.DeclarationSnapshot)))
            {
                throw new ArgumentException(
                    "The checked program has no exact compiler-owned source class association.",
                    nameof(classExports));
            }
            foreach (var declaration in association.Declarations)
            {
                _ = GetPublicClassMembers(program, declaration).Count();
            }

            var existingKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var function in program.Functions)
            {
                if (!function.IsPrivate && !function.IsInternal
                    && function.DeclaringStruct is null
                    && function.ModuleImport is null
                    && function.GenericMethodInstantiation is null)
                {
                    AddKey(existingKeys, function.Name,
                        nameof(classExports));
                }
            }

            foreach (var typeExport in typeExports)
            {
                ArgumentNullException.ThrowIfNull(typeExport);
                AddKey(existingKeys, typeExport.ExportKey,
                    nameof(typeExports));
                AddKeys(existingKeys,
                    typeExport.EnumMemberDeclarationKeys.IsDefault
                        ? [] : typeExport.EnumMemberDeclarationKeys,
                    nameof(typeExports));
                AddKeys(existingKeys, typeExport.FieldExports.IsDefault
                    ? [] : typeExport.FieldExports.Select(
                        static field => field.ExportKey),
                    nameof(typeExports));
                AddKeys(existingKeys,
                    typeExport.InterfaceMethodExports.IsDefault
                        ? [] : typeExport.InterfaceMethodExports.Select(
                            static method => method.ExportKey),
                    nameof(typeExports));
                AddKeys(existingKeys,
                    typeExport.InterfaceAssociatedTypeExports.IsDefault
                        ? [] : typeExport.InterfaceAssociatedTypeExports
                            .Select(static associatedType =>
                                associatedType.ExportKey),
                    nameof(typeExports));
            }

            foreach (var methodExport in genericMethodExports)
            {
                ArgumentNullException.ThrowIfNull(methodExport);
                AddKey(existingKeys, methodExport.ExportKey,
                    nameof(genericMethodExports));
            }

            if (enumExports is not null)
            {
                foreach (var enumExport in enumExports)
                {
                    ArgumentNullException.ThrowIfNull(enumExport);
                    AddKeys(existingKeys, enumExport.ExportKeys,
                        nameof(enumExports));
                }
            }

            foreach (var enumDefinition in program.EnumDefinitions)
            {
                if (enumDefinition.Identity
                        is BoundEnumDeclarationIdentity.Core core
                    && StringComparer.Ordinal.Equals(
                        core.Value.ProviderIdentity, moduleIdentity))
                {
                    AddKey(existingKeys, core.Value.ExportKey,
                        nameof(classExports));
                }
            }

            var snapshot = new TrRmSourceClassExport[classExports.Count];
            for (var index = 0; index < snapshot.Length; index++)
            {
                var export = classExports[index]
                    ?? throw new ArgumentException(
                        "Source class mappings cannot contain null entries.",
                        nameof(classExports));
                if (export.MemberExports.IsDefault)
                {
                    throw new ArgumentException(
                        "Source class member mappings must be an explicit immutable array.",
                        nameof(classExports));
                }

                snapshot[index] = export with {
                    MemberExports = [.. export.MemberExports.Select(
                        member => member ?? throw new ArgumentException(
                            "Source class member mappings cannot contain null entries.",
                            nameof(classExports)))],
                };
            }

            var selected = new HashSet<(int ClassIndex, TextSpan Span)>();
            var selectedMembers = new HashSet<(int ClassIndex,
                int SourceInputOrdinal, TextSpan Span)>();
            var results = ImmutableArray.CreateBuilder<SourceClassExport>(
                snapshot.Length);
            foreach (var export in snapshot)
            {
                if (!ReferenceEquals(export.Program, program)
                    || export.SourceInputOrdinal < 0
                    || export.SourceInputOrdinal >= program.SourceInputCount
                    || export.DeclarationSpan.Start < 0
                    || export.DeclarationSpan.Length <= 0)
                {
                    throw new ArgumentException(
                        "A source class mapping does not identify this checked program.",
                        nameof(classExports));
                }

                ValidateSpan(export.DeclarationSpan, nameof(classExports));
                var declarationMatches = association.Declarations
                    .Where(declaration =>
                        declaration.SourceInputOrdinal
                            == export.SourceInputOrdinal
                        && declaration.Span == export.DeclarationSpan)
                    .ToArray();
                if (declarationMatches.Length != 1
                    || declarationMatches[0].DeclarationSnapshot.IsInternal)
                {
                    throw new ArgumentException(
                        "A source class mapping must select one public primary declaration in the checked program.",
                        nameof(classExports));
                }

                var declaration = declarationMatches[0];
                AddKey(existingKeys, export.ExportKey,
                    nameof(classExports));
                _ = selected.Add((declaration.ClassIndex,
                    declaration.Span));
                var members = ImmutableArray.CreateBuilder<
                    SourceClassMemberExport>(export.MemberExports.Length);
                foreach (var memberExport in export.MemberExports)
                {
                    if (!declaration.Parts.Any(part =>
                            part.SourceInputOrdinal
                                == memberExport.SourceInputOrdinal
                            && part.Declaration.Children
                                .OfType<SyntaxNode>()
                                .Any(node => node.Span
                                    == memberExport.DeclarationSpan
                                    && IsClassMemberKind(node.Kind)))
                        || memberExport.DeclarationSpan.Start < 0
                        || memberExport.DeclarationSpan.Length <= 0)
                    {
                        throw new ArgumentException(
                            "A source class member mapping must select one direct member of the selected partial class.",
                            nameof(classExports));
                    }

                    ValidateSpan(memberExport.DeclarationSpan,
                        nameof(classExports));
                    var matchingMembers = declaration.Parts
                        .Where(part => part.SourceInputOrdinal
                            == memberExport.SourceInputOrdinal)
                        .SelectMany(static part =>
                            part.Declaration.Children.OfType<SyntaxNode>())
                        .Where(node => node.Span
                            == memberExport.DeclarationSpan
                            && IsClassMemberKind(node.Kind))
                        .ToArray();
                    if (matchingMembers.Length != 1)
                    {
                        throw new ArgumentException(
                            "A source class member mapping must select one exact direct member declaration.",
                            nameof(classExports));
                    }

                    var member = BindClassMember(program, declaration,
                        memberExport.SourceInputOrdinal, matchingMembers[0],
                        memberExport,
                        existingKeys);
                    members.Add(member);
                    _ = selectedMembers.Add((declaration.ClassIndex,
                        memberExport.SourceInputOrdinal,
                        memberExport.DeclarationSpan));
                }

                results.Add(new SourceClassExport(declaration,
                    export.ExportKey, members.MoveToImmutable()));
            }

            foreach (var classIndex in selected.Select(
                static selection => selection.ClassIndex).Distinct())
            {
                var declaration = association.Declarations.Single(
                    candidate => candidate.ClassIndex == classIndex);
                foreach (var member in GetPublicClassMembers(program,
                    declaration))
                {
                    if (!requirePublicCallableKeys
                        || member.Declaration.Kind
                            == SyntaxKind.FieldDeclaration)
                    {
                        continue;
                    }

                    if (!selectedMembers.Contains((classIndex,
                        member.SourceInputOrdinal, member.Declaration.Span)))
                    {
                        throw new NotSupportedException(
                            "Every retained public constructor, method, and property requires an explicit source ExportKey.");
                    }
                }
            }

            return new SourceClassExports(program, results.MoveToImmutable());
        }

        private static SourceClassMemberExport BindClassMember(
            BoundProgram program, BoundSourceClassDeclaration owner,
            int sourceInputOrdinal, SyntaxNode declaration,
            TrRmSourceClassMemberExport mapping,
            HashSet<string> existingKeys)
        {
            var binding = GetClassMemberBinding(program, owner,
                sourceInputOrdinal, declaration);
            if (!binding.IsPublic)
            {
                throw new ArgumentException(
                    "Source class member ExportKeys can select public members only.",
                    nameof(mapping));
            }
            if (!binding.IsPrimaryDeclaration)
            {
                throw new ArgumentException(
                    "A source class member ExportKey cannot select a partial implementation secondary.",
                    nameof(mapping));
            }
            if (binding.Kind != SourceClassMemberKind.Constructor
                && mapping.ConstructionProjectionExportKey is not null)
            {
                throw new ArgumentException(
                    "A construction-projection ExportKey is valid only on a class constructor.",
                    nameof(mapping));
            }

            AddKey(existingKeys, mapping.ExportKey, nameof(mapping));
            if (mapping.ConstructionProjectionExportKey is string projectionKey)
            {
                AddKey(existingKeys, projectionKey, nameof(mapping));
            }

            return new SourceClassMemberExport(declaration, binding.Kind,
                mapping.ExportKey, mapping.ConstructionProjectionExportKey,
                binding.FieldIndex, binding.FunctionIndex) {
                BackingFieldIndex = binding.BackingFieldIndex,
                SetterFunctionIndex = binding.SetterFunctionIndex,
                InitializerFunctionIndex =
                    binding.InitializerFunctionIndex,
            };
        }

        private static IEnumerable<(int SourceInputOrdinal,
            SyntaxNode Declaration)> GetPublicClassMembers(
            BoundProgram program, BoundSourceClassDeclaration owner)
        {
            foreach (var part in owner.Parts)
            {
                foreach (var member in part.Declaration.Children
                    .OfType<SyntaxNode>())
                {
                    if (!IsClassMemberKind(member.Kind))
                    {
                        throw new NotSupportedException(
                            "A retained class declaration has an unrepresented member or declaration contract.");
                    }

                    var binding = GetClassMemberBinding(program, owner,
                        part.SourceInputOrdinal, member);
                    if (binding.IsPublic && binding.IsPrimaryDeclaration)
                    {
                        yield return (part.SourceInputOrdinal, member);
                    }
                }
            }
        }

        private static (SourceClassMemberKind Kind, int FieldIndex,
            int FunctionIndex, int BackingFieldIndex,
            int SetterFunctionIndex, int InitializerFunctionIndex,
            bool IsPublic, bool IsPrimaryDeclaration)
            GetClassMemberBinding(
            BoundProgram program, BoundSourceClassDeclaration owner,
            int sourceInputOrdinal, SyntaxNode declaration)
        {
            var name = GetMemberName(declaration);
            if (string.IsNullOrEmpty(name))
            {
                throw new NotSupportedException(
                    "A retained class member has no exact source name.");
            }

            var kind = declaration.Kind switch
            {
                SyntaxKind.FieldDeclaration => SourceClassMemberKind.Field,
                SyntaxKind.ConstructorDeclaration
                    => SourceClassMemberKind.Constructor,
                SyntaxKind.FunctionDeclaration => SourceClassMemberKind.Method,
                SyntaxKind.PropertyDeclaration => SourceClassMemberKind.Property,
                _ => throw new NotSupportedException(
                    "A retained class member kind is unsupported."),
            };
            if (kind == SourceClassMemberKind.Field)
            {
                var fields = owner.DeclarationSnapshot.Fields
                    .Select(static (field, index) => (field, index))
                    .Where(candidate => StringComparer.Ordinal.Equals(
                        candidate.field.Name, name))
                    .ToArray();
                if (fields.Length != 1)
                {
                    throw new NotSupportedException(
                        "A retained class field has no unique checked FieldDef correspondence.");
                }

                return (kind, fields[0].index, -1, -1, -1, -1,
                    !fields[0].field.IsPrivate
                        && !fields[0].field.IsInternal, true);
            }

            var functions = program.Functions
                .Select(static (function, index) => (function, index))
                .Where(candidate =>
                    StringComparer.Ordinal.Equals(
                        candidate.function.DeclaringStruct,
                        owner.DeclarationSnapshot.Name)
                    && (kind == SourceClassMemberKind.Property
                        ? candidate.function.SourceDeclarationInputOrdinal
                                == sourceInputOrdinal
                            && candidate.function.SourceDeclarationSpan
                                == declaration.Span
                            && StringComparer.Ordinal.Equals(
                                candidate.function.PropertyName, name)
                        : candidate.function.SourceDeclarationInputOrdinal
                                == sourceInputOrdinal
                            && candidate.function.SourceDeclarationSpan
                                == declaration.Span
                            || kind == SourceClassMemberKind.Constructor
                                && candidate.function.IsPartialConstructor
                                && candidate.function.SourceInputOrdinal
                                    == sourceInputOrdinal
                                && candidate.function.Span
                                    == declaration.Span))
                .ToArray();
            if (kind == SourceClassMemberKind.Property)
            {
                var getters = functions.Where(static candidate =>
                    candidate.function.PropertyAccessorKind
                        is BoundPropertyAccessorKind.None
                            or BoundPropertyAccessorKind.Getter).ToArray();
                var setters = functions.Where(static candidate =>
                    candidate.function.PropertyAccessorKind
                        == BoundPropertyAccessorKind.Setter).ToArray();
                var initializers = functions.Where(static candidate =>
                    candidate.function.PropertyAccessorKind
                        == BoundPropertyAccessorKind.Initializer).ToArray();
                var backingFields = owner.DeclarationSnapshot.Fields
                    .Select(static (field, fieldIndex) =>
                        (field, fieldIndex))
                    .Where(candidate => StringComparer.Ordinal.Equals(
                        candidate.field.BackingPropertyName, name))
                    .ToArray();
                if (getters.Length != 1 || setters.Length > 1
                    || initializers.Length > 1 || backingFields.Length > 1
                    || (setters.Length != 0 && initializers.Length != 0))
                {
                    throw new NotSupportedException(
                        "A retained class property has an incomplete or ambiguous accessor/backing association.");
                }

                var getter = getters[0];
                var isPrimaryDeclaration
                    = getter.function.SourceDeclarationInputOrdinal
                        == sourceInputOrdinal
                    && getter.function.SourceDeclarationSpan
                        == declaration.Span;
                return (kind, -1, getter.index,
                    backingFields.Length == 0 ? -1
                        : backingFields[0].fieldIndex,
                    setters.Length == 0 ? -1 : setters[0].index,
                    initializers.Length == 0 ? -1
                        : initializers[0].index,
                    !getter.function.IsPrivate
                        && !getter.function.IsInternal,
                    isPrimaryDeclaration);
            }

            if (functions.Length != 1
                || (kind == SourceClassMemberKind.Constructor)
                    != functions[0].function.IsClassConstructor)
            {
                throw new NotSupportedException(
                    "A retained class method has no unique checked MethodDef correspondence.");
            }

            var isPrimaryDeclaration
                = functions[0].function.SourceDeclarationInputOrdinal
                    == sourceInputOrdinal
                && functions[0].function.SourceDeclarationSpan
                    == declaration.Span;
            return (kind, -1, functions[0].index, -1, -1, -1,
                !functions[0].function.IsPrivate
                    && !functions[0].function.IsInternal,
                isPrimaryDeclaration);
        }

        private static string? GetMemberName(SyntaxNode declaration)
        {
            if (declaration.Kind == SyntaxKind.FieldDeclaration
                && declaration.Children.Length >= 2
                && declaration.Children[^2] is SyntaxTokenElement fieldToken
                && fieldToken.Token.Kind == TokenKind.Identifier)
            {
                return fieldToken.Token.Name;
            }

            return declaration.Children
                .OfType<SyntaxTokenElement>()
                .Where(static token =>
                    token.Token.Kind == TokenKind.Identifier)
                .Select(static token => token.Token.Name)
                .LastOrDefault();
        }

        private static bool IsClassMemberKind(SyntaxKind kind)
            => kind is SyntaxKind.FieldDeclaration
                or SyntaxKind.ConstructorDeclaration
                or SyntaxKind.FunctionDeclaration
                or SyntaxKind.PropertyDeclaration;

        private static void ValidateSpan(TextSpan span, string parameterName)
        {
            try
            {
                _ = checked(span.Start + span.Length);
            }
            catch (OverflowException)
            {
                throw new ArgumentException(
                    "A source class declaration span overflows.",
                    parameterName);
            }
        }

        private static void AddKeys(HashSet<string> keys,
            IEnumerable<string> values, string parameterName)
        {
            foreach (var value in values)
            {
                AddKey(keys, value, parameterName);
            }
        }

        private static void AddKey(HashSet<string> keys, string? key,
            string parameterName)
        {
            if (string.IsNullOrEmpty(key)
                || key.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Source class ExportKeys must be nonempty and contain no NUL.",
                    parameterName);
            }

            try
            {
                var byteCount = s_sourceTypeExportKeyEncoding.GetByteCount(key);
                if (byteCount is < 1 or > 0xFF_FFFF)
                {
                    throw new ArgumentException(
                        "A source class ExportKey exceeds String capacity.",
                        parameterName);
                }
            }
            catch (EncoderFallbackException)
            {
                throw new ArgumentException(
                    "A source class ExportKey must contain Unicode scalar text.",
                    parameterName);
            }

            if (!keys.Add(key))
            {
                throw new ArgumentException(
                    "Source class ExportKeys must be unique within the Module.",
                    parameterName);
            }
        }
    }
}
