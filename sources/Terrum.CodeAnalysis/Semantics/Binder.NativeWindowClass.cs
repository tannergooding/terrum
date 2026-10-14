// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Linq;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private BoundExpression BindNativeWindowClassExInitializer(SyntaxNode node)
    {
        var arguments = ((SyntaxNode)node.Children[1]).Children.OfType<SyntaxNode>().ToArray();
        if (_nativeImportCatalog?.SupportsWindowClassInitialization != true
            || arguments.Length != 3
            || arguments.Any(static argument => argument.Children is not [SyntaxNode])
        )
        {
            Report("TRM3030", "windowclassex requires a direct WNDPROC address, current module handle and utf16z class-name literal.",
                node.Span);
            return ErrorExpression(node.Span);
        }

        var callback = BindExpression((SyntaxNode)arguments[0].Children[0], PrimitiveType.NativeCallbackAddress);
        var instance = BindExpression((SyntaxNode)arguments[1].Children[0], PrimitiveType.NativeModuleHandle);
        var className = BindExpression((SyntaxNode)arguments[2].Children[0], PrimitiveType.NativeUtf16ZPointer);
        if (callback is not BoundNativeCallbackAddress callbackAddress)
        {
            Report("TRM3030", "windowclassex requires a direct static WNDPROC address.", arguments[0].Span);
            return ErrorExpression(node.Span);
        }

        return new BoundNativeWindowClassExInitialization(callbackAddress, instance, className, node.Span);
    }

    private BoundExpression BindNativeWindowClassExAddress(SyntaxNode node, TextSpan span)
    {
        while (node.Kind == SyntaxKind.ParenthesizedExpression)
        {
            node = (SyntaxNode)node.Children[1];
        }
        if (_nativeImportCatalog?.SupportsWindowClassInitialization != true
            || node.Kind != SyntaxKind.NameExpression || !TryFindVariable(NameText(node), out var variable)
            || _variables[variable].Type != PrimitiveType.NativeWindowClassEx
            || _variables[variable].NativeWindowClassExFacts is null)
        {
            Report("TRM3030", "A WNDCLASSEXW address requires the exact initialized immutable descriptor local.",
                span);
            return ErrorExpression(span);
        }

        return new BoundNativeWindowClassExAddress(variable, span);
    }
}
