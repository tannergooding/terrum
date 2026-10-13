// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private BoundExpression BindNativeAggregateStorageAddress(SyntaxNode node, TextSpan span)
    {
        while (node.Kind == SyntaxKind.ParenthesizedExpression)
        {
            node = (SyntaxNode)node.Children[1];
        }

        if (node.Kind == SyntaxKind.IndexExpression
            && TryFindNativeFixedArrayLocal((SyntaxNode)node.Children[0], out var arrayVariable)
            && _variables[arrayVariable].NativeFixedArrayLayout is BoundNativeFixedArrayLayout layout
            && NativeFixedArrayContract.MatchesLayout(layout, _structs, _nativeImportCatalog))
        {
            var isReadonly = !_variables[arrayVariable].IsMutable
                || !NativeFixedArrayContract.AllowsElementWrites(layout, _structs, _nativeImportCatalog);
            if (!NativeStorageParameterContract.SupportsFixedArrayParameter(_nativeImportCatalog, layout,
                _structs, PrimitiveType.NativeStorageAddress, isReadonly))
            {
                Report("TRM3030", "The selected fixed-array layout does not admit this native storage loan.",
                    span);
                return ErrorExpression(span);
            }
            var index = BindExpression((SyntaxNode)node.Children[2], PrimitiveType.Int32);
            return new BoundNativeStorageAddress(arrayVariable, index, layout.CarrierType, layout.ElementType,
                PrimitiveType.NativeStorageAddress, layout.ElementCount, layout.ElementStride,
                layout.ByteExtent, isReadonly, span);
        }

        if (node.Kind != SyntaxKind.NameExpression
            || !TryFindVariable(NameText(node), out var variable))
        {
            Report("TRM3030", "A native storage address requires a matching initialized local.",
                span);
            return ErrorExpression(span);
        }

        var type = _variables[variable].Type;
        if (type == PrimitiveType.NativeStorageRecord
            && _variables[variable].NativeStorageRecordLayout is NativeStorageRecordLayoutDefinition recordLayout
            && _variables[variable].NativeStorageRecordFacts is not null
            && NativeStorageParameterContract.SupportsStorageRecordType(
                recordLayout, _nativeImportCatalog, isReadonly: !_variables[variable].IsMutable)
            && !_variables[variable].IsMutable)
        {
            return new BoundNativeStorageAddress(variable,
                new BoundConstant(0, PrimitiveType.Int32, span), PrimitiveType.NativeStorageRecord,
                PrimitiveType.NativeStorageRecord, PrimitiveType.NativeStorageAddress, 1,
                recordLayout.ByteExtent, recordLayout.ByteExtent, IsReadonly: true, Span: span);
        }
        if (!NativeStorageParameterContract.SupportsAggregateType(_nativeImportCatalog,
                type, _structs, !_variables[variable].IsMutable)
            && !NativeStorageParameterContract.SupportsValueOutputAddressType(_nativeImportCatalog,
                type, _structs, !_variables[variable].IsMutable))
        {
            Report("TRM3030", "A native storage address requires a matching initialized local.",
                span);
            return ErrorExpression(span);
        }
        var extent = ValueLayout.Size(type, _structs);
        return new BoundNativeStorageAddress(variable,
            new BoundConstant(0, PrimitiveType.Int32, span), type, type,
            PrimitiveType.NativeStorageAddress, 1, extent, extent,
            IsReadonly: !_variables[variable].IsMutable, Span: span);
    }
}
