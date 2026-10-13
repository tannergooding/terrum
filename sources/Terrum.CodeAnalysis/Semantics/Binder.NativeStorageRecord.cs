// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private BoundExpression BindNativeStorageRecordInitializer(SyntaxNode node, PrimitiveType? expected)
    {
        var arguments = ((SyntaxNode)node.Children[1]).Children.OfType<SyntaxNode>().ToArray();
        var initializerName = NameText((SyntaxNode)node.Children[0]);
        var layout = _nativeImportCatalog?.StorageRecordLayouts
            .SingleOrDefault(candidate => candidate.InitializerName == initializerName);
        if (expected != PrimitiveType.NativeStorageRecord || layout is null
            || arguments.Length != layout.Fields.Length
                - (layout.BackingPointerFieldIndex is null ? 0 : 1) + 1
            || arguments.Any(static argument => argument.Children is not [SyntaxNode]))
        {
            Report("TRM3030", "The selected native storage record requires its catalogued initializer and exact fields.",
                node.Span);
            return ErrorExpression(node.Span);
        }

        var source = BindExpression((SyntaxNode)arguments[0].Children[0],
            PrimitiveType.NativeStorageAddress);
        if (source is not BoundNativeStorageAddress {
            Type: PrimitiveType.NativeStorageAddress,
            AddressType: PrimitiveType.NativeStorageAddress,
            CarrierType: PrimitiveType.NativeFixedArray,
            Index: BoundConstant { Type: PrimitiveType.Int32, Value: 0 },
            IsReadonly: true,
            ByteExtent: > 0,
            ElementCount: > 0,
            ElementStride: > 0,
        } address
            || address.ElementCount > NativeFixedArrayContract.MaximumByteExtent / address.ElementStride
            || address.ByteExtent != address.ElementCount * address.ElementStride
            || _variables[address.Variable].NativeFixedArrayLayout is not BoundNativeFixedArrayLayout arrayLayout
            || !NativeFixedArrayContract.MatchesLayout(arrayLayout, _structs, _nativeImportCatalog)
            || arrayLayout.ElementType != address.ElementType
            || !NativeStorageParameterContract.SupportsFixedArrayParameter(_nativeImportCatalog, arrayLayout,
                _structs, PrimitiveType.NativeStorageAddress, isReadonly: true))
        {
            Report("TRM3030", "A native storage record requires a readonly loan from element zero of an initialized fixed array.",
                node.Span);
            return ErrorExpression(node.Span);
        }

        var fields = ImmutableArray.CreateBuilder<BoundExpression>(
            layout.Fields.Length - (layout.BackingPointerFieldIndex is null ? 0 : 1));
        for (var fieldIndex = 0; fieldIndex < layout.Fields.Length; fieldIndex++)
        {
            if (layout.BackingPointerFieldIndex == fieldIndex)
            {
                continue;
            }
            fields.Add(BindExpression((SyntaxNode)arguments[fields.Count + 1].Children[0],
                layout.Fields[fieldIndex].Type));
        }

        var initialization = new BoundNativeStorageRecordInitialization(layout, source, fields.MoveToImmutable(),
            node.Span) {
            Loan = new NativeStorageLoan(address.Variable, address.CarrierType,
                address.ElementType, true, address.ByteExtent),
        };
        if (NativeStorageRecordContract.Facts(initialization, _nativeImportCatalog) is null)
        {
            Report("TRM3030", "Native storage record fields do not satisfy the selected layout and backing constraints.",
                node.Span);
            return ErrorExpression(node.Span);
        }

        return initialization;
    }
}
