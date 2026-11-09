// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.Tools;

internal sealed record CoreManifestRegistrationComposition(
    LayoutCoreRegistrationSet? Layout,
    NativeCoreRegistrationSet? Native,
    NativeOutputCoreRegistrationSet? NativeOutput);

internal static class CoreManifestRegistrationComposer
{
    internal static CoreManifestRegistrationComposition Create(
        LayoutCoreManifestDefinition? layout,
        NativeCoreManifestDefinition? native,
        NativeOutputCoreManifestDefinition? nativeOutput,
        string? layoutPath, string? nativePath, string? nativeOutputPath)
    {
        if (layout is null && native is null && nativeOutput is null)
        {
            return new CoreManifestRegistrationComposition(null, null, null);
        }

        var usingModuleIdentity = layout?.UsingModuleIdentity
            ?? native?.UsingModuleIdentity
            ?? nativeOutput!.UsingModuleIdentity;
        var coreAuthorityIdentity = layout?.CoreAuthorityIdentity
            ?? native?.CoreAuthorityIdentity
            ?? nativeOutput!.CoreAuthorityIdentity;
        if ((layout is not null
                && (layout.CoreAuthorityIdentity != coreAuthorityIdentity
                    || layout.UsingModuleIdentity != usingModuleIdentity))
            || (native is not null
                && (native.CoreAuthorityIdentity != coreAuthorityIdentity
                    || native.UsingModuleIdentity != usingModuleIdentity))
            || (nativeOutput is not null
                && (nativeOutput.CoreAuthorityIdentity
                    != coreAuthorityIdentity
                    || nativeOutput.UsingModuleIdentity
                    != usingModuleIdentity)))
        {
            throw Invalid(layoutPath, nativePath, nativeOutputPath,
                "authority and usingModuleIdentity must match exactly");
        }

        var origins = MergeOrigins(layout, native, nativeOutput,
            layoutPath, nativePath, nativeOutputPath);
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
        if (nativeOutput is not null)
        {
            declarations.AddRange(nativeOutput.Declarations);
        }

        try
        {
            var bindings = CoreProviderModuleBindings.Create(usingModuleIdentity,
                origins, declarations.ToImmutable());
            return new CoreManifestRegistrationComposition(
                layout?.CreateRegistration(bindings),
                native?.CreateRegistration(bindings),
                nativeOutput?.CreateRegistration(bindings));
        }
        catch (ArgumentException exception)
        {
            throw Invalid(layoutPath, nativePath, nativeOutputPath,
                exception.Message);
        }
    }

    private static ImmutableArray<CoreProviderModuleOrigin> MergeOrigins(
        LayoutCoreManifestDefinition? layout,
        NativeCoreManifestDefinition? native,
        NativeOutputCoreManifestDefinition? nativeOutput,
        string? layoutPath, string? nativePath, string? nativeOutputPath)
    {
        var origins = ImmutableArray.CreateBuilder<CoreProviderModuleOrigin>();
        var originsByModule = new Dictionary<string, CoreProviderModuleOrigin>(
            StringComparer.Ordinal);
        Add(layout?.Origins ?? []);
        Add(native?.Origins ?? []);
        Add(nativeOutput?.Origins ?? []);
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
                            nativeOutputPath,
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
        string? nativePath, string? nativeOutputPath, string message)
    {
        var paths = new[] { layoutPath, nativePath, nativeOutputPath }
            .Where(static path => path is not null)
            .Select(static path => $"'{path}'");
        var path = string.Join(" and ", paths);
        return new FormatException(
            $"Invalid combined Core manifest selection {path}: {message}.");
    }
}
