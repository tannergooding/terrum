// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Semantics;

[Flags]
internal enum NativeSourceFeature : uint
{
    None = 0,
    NativeFixedArrayStorage = 1,
}

internal readonly record struct NativeSourceProfile(NativeSourceFeature Features)
{
    internal bool Has(NativeSourceFeature feature) => (Features & feature) != 0;
}

internal sealed class NativeSourceProfileBuilder
{
    internal NativeSourceProfile Select(ImmutableArray<SyntaxTree> trees, NativeImportCatalog? catalog,
        BoundRuntimePolicy runtimePolicy) =>
        new(NativeFixedArrayContract.SupportsRuntimePolicy(catalog, runtimePolicy)
            ? NativeSourceFeature.NativeFixedArrayStorage : NativeSourceFeature.None);
}
