// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeWindowClientRectContract
{
    internal const string ImportKey = "windows-x64.system32.user32.GetClientRect";

    internal static ImmutableArray<NativeAggregateStorageLayoutDefinition> AggregateStorageLayouts { get; } = [
        new("ClientRectangle", 16, sizeof(int)) {
            Fields = [
                new("Left", nameof(PrimitiveType.Int32), 0, sizeof(int)),
                new("Top", nameof(PrimitiveType.Int32), sizeof(int), sizeof(int)),
                new("Right", nameof(PrimitiveType.Int32), 2 * sizeof(int), sizeof(int)),
                new("Bottom", nameof(PrimitiveType.Int32), 3 * sizeof(int), sizeof(int)),
            ],
        },
    ];

    internal static NativeImportDefinition Import { get; } = new(ImportKey,
        "user32.dll", "GetClientRect", [
            PrimitiveType.NativeWindowHandle, PrimitiveType.NativeStorageAddress,
        ], PrimitiveType.Int32, true, false, false, NativeImportDeploymentPolicy.System32) {
        ParameterContracts = [
            NativeImportParameterContract.CreatedWindowHandleNoEscape,
            NativeImportParameterContract.WritableNativeStorageNoEscape,
        ],
        AggregateStorageParameters = [
            new(1, PrimitiveType.NativeStorageAddress, 16, 4, IsReadonly: false,
                SourceType: "ClientRectangle"),
        ],
    };

    internal static bool IsReservedImport(NativeImportDefinition import) =>
        import.Key == ImportKey || import.ExportName == Import.ExportName;

    internal static bool MatchesImport(NativeImportDefinition import) =>
        !import.ParameterTypes.IsDefault && !import.ParameterContracts.IsDefault
        && import.ParameterTypes.SequenceEqual(Import.ParameterTypes)
        && import.ParameterContracts.SequenceEqual(Import.ParameterContracts)
        && import with {
            ParameterTypes = Import.ParameterTypes,
            ParameterContracts = Import.ParameterContracts,
        } == Import;

    internal static NativeImportParameterContract? ExpectedHandleParameterContract(
        NativeImportDefinition import, int parameter) =>
        import.Key == ImportKey && parameter == 0
            ? NativeImportParameterContract.CreatedWindowHandleNoEscape
            : null;

    internal static bool MatchesCatalog(NativeImportCatalog? catalog) =>
        catalog is { IsForTesting: false, Identity: NativeWindowClassContract.CatalogIdentity }
        && catalog.TryGetImport(ImportKey, out var import)
        && import is not null && MatchesImport(import);
}
