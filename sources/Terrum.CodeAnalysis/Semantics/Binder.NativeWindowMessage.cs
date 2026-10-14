// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private void DeclareNativeWindowMessageAccessors(NativeImportCatalog catalog)
    {
        if (!catalog.SupportsWindowMessageLoop)
        {
            return;
        }

        var invokedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in _trees)
        {
            foreach (var node in tree.Root.DescendantNodes())
            {
                if (node.Kind == SyntaxKind.InvocationExpression && node.Children.Length > 0
                    && node.Children[0] is SyntaxNode { Kind: SyntaxKind.NameExpression } name)
                {
                    _ = invokedNames.Add(NameText(name));
                }
            }
        }

        foreach (var function in NativeWindowMessageContract.AccessorFunctions)
        {
            if (!invokedNames.Contains(function.Name) || !_functions.TryAdd(function.Name, _signatures.Count))
            {
                continue;
            }

            var parameters = function.Variables.Select(static variable =>
                new Parameter(variable.Name, variable.Type, default, variable.IsOut));
            _signatures.Add(new Signature(function.Name, function.ReturnType, [.. parameters],
                null, null, _trees[0], null, false, false, true, false, false, true, false, function));
        }
    }

    private BoundExpression BindNativeWindowMessageInitializer(SyntaxNode node)
    {
        if (_nativeImportCatalog?.SupportsWindowMessageLoop != true
            || ((SyntaxNode)node.Children[1]).Children.OfType<SyntaxNode>().Any())
        {
            Report("TRM3030", "msgzero initializes only the opaque MSG carrier for the reviewed Windows message loop.",
                node.Span);
            return ErrorExpression(node.Span);
        }

        return new BoundNativeWindowMessageInitialization(node.Span);
    }

    private BoundExpression BindNativeWindowMessageAddress(SyntaxNode node, TextSpan span)
    {
        while (node.Kind == SyntaxKind.ParenthesizedExpression)
        {
            node = (SyntaxNode)node.Children[1];
        }
        if (_nativeImportCatalog?.SupportsWindowMessageLoop != true
            || node.Kind != SyntaxKind.NameExpression || !TryFindVariable(NameText(node), out var variable)
            || _variables[variable].Type != PrimitiveType.NativeWindowMessage)
        {
            Report("TRM3030", "An MSG address requires the exact local opaque message carrier.",
                span);
            return ErrorExpression(span);
        }

        return new BoundNativeWindowMessageAddress(variable, span);
    }
}
