// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.Tools;

internal sealed record CoreManifestRegistrationComposition(
    LayoutCoreRegistrationSet? Layout,
    NativeCoreRegistrationSet? Native);

internal static class CoreManifestRegistrationComposer
{
    internal static CoreManifestRegistrationComposition Create(
        LayoutCoreManifestDefinition? layout,
        NativeCoreManifestDefinition? native,
        string? layoutPath, string? nativePath)
    {
        if (layout is null && native is null)
        {
            return new CoreManifestRegistrationComposition(null, null);
        }

        var usingModuleIdentity = layout?.UsingModuleIdentity
            ?? native!.UsingModuleIdentity;
        if (layout is not null && native is not null
            && (layout.CoreAuthorityIdentity != native.CoreAuthorityIdentity
                || layout.UsingModuleIdentity != native.UsingModuleIdentity))
        {
            throw Invalid(layoutPath, nativePath,
                "authority and usingModuleIdentity must match exactly");
        }

        var origins = MergeOrigins(layout, native, layoutPath, nativePath);
        var declarations = ImmutableArray.CreateBuilder<
            CoreProviderDeclarationSnapshot>();
        if (layout is not null)
        {
            declarations.AddRange(layout.Declarations);
        }
        if (native is not null)
        {
            declarations.AddRange(native.Declarations);
        }

        try
        {
            var bindings = CoreProviderModuleBindings.Create(usingModuleIdentity,
                origins, declarations.ToImmutable());
            return new CoreManifestRegistrationComposition(
                layout?.CreateRegistration(bindings),
                native?.CreateRegistration(bindings));
        }
        catch (ArgumentException exception)
        {
            throw Invalid(layoutPath, nativePath, exception.Message);
        }
    }

    private static ImmutableArray<CoreProviderModuleOrigin> MergeOrigins(
        LayoutCoreManifestDefinition? layout,
        NativeCoreManifestDefinition? native,
        string? layoutPath, string? nativePath)
    {
        var origins = ImmutableArray.CreateBuilder<CoreProviderModuleOrigin>();
        var originsByModule = new Dictionary<string, CoreProviderModuleOrigin>(
            StringComparer.Ordinal);
        Add(layout?.Origins ?? []);
        Add(native?.Origins ?? []);
        return origins.ToImmutable();

        void Add(ImmutableArray<CoreProviderModuleOrigin> selected)
        {
            foreach (var origin in selected)
            {
                if (originsByModule.TryGetValue(origin.ModuleIdentity,
                    out var previous))
                {
                    if (previous != origin)
                    {
                        throw Invalid(layoutPath, nativePath,
                            $"provider origin '{origin.ModuleIdentity}' differs between the selected manifests");
                    }
                    continue;
                }

                originsByModule.Add(origin.ModuleIdentity, origin);
                origins.Add(origin);
            }
        }
    }

    private static FormatException Invalid(string? layoutPath,
        string? nativePath, string message)
    {
        var path = layoutPath is not null && nativePath is not null
            ? $"'{layoutPath}' and '{nativePath}'"
            : $"'{layoutPath ?? nativePath ?? "<manifest>"}'";
        return new FormatException(
            $"Invalid combined Core manifest selection {path}: {message}.");
    }
}
