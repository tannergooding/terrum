// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;
using Terrum.CodeAnalysis.Semantics;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class BoundTreeTraversalTests
{
    [Test]
    public void RewriteCallsVisitsUnsafeAndViewScopeChildren()
    {
        var unsafeCall = new BoundCall(3, [], PrimitiveType.Void, default);
        var viewCall = new BoundCall(4, [], PrimitiveType.Void, default);
        var body = new BoundBlock([
            new BoundUnsafe(new BoundBlock([
                new BoundExpressionStatement(unsafeCall, default),
            ], default), default),
            new BoundViewScope(0,
                new BoundConstant(0, PrimitiveType.Int32, default),
                new BoundConstant(0, PrimitiveType.Int32, default),
                new BoundConstant(1, PrimitiveType.Int32, default),
                new BoundBlock([
                    new BoundExpressionStatement(viewCall, default),
                ], default), default),
        ], default);
        var function = new BoundFunction("Fixture", PrimitiveType.Void,
            ImmutableArray<BoundVariable>.Empty, 0, body, default);

        var rewritten = BoundTreeTraversal.RewriteCalls(function,
            static call => call with { Function = call.Function + 1 });
        var __nunitAssertValue0 = rewritten.Body.Statements[0];
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundUnsafe>());
        var unsafeStatement = ((BoundUnsafe)__nunitAssertValue0);
        var __nunitAssertValue1 = unsafeStatement.Body.Statements[0];
        Assert.That(__nunitAssertValue1, Is.TypeOf<BoundExpressionStatement>());
        var __nunitAssertValue2 = (((BoundExpressionStatement)__nunitAssertValue1)).Expression;
        Assert.That(__nunitAssertValue2, Is.TypeOf<BoundCall>());
        var rewrittenUnsafeCall = ((BoundCall)__nunitAssertValue2);
        var __nunitAssertValue3 = rewritten.Body.Statements[1];
        Assert.That(__nunitAssertValue3, Is.TypeOf<BoundViewScope>());
        var view = ((BoundViewScope)__nunitAssertValue3);
        var __nunitAssertValue4 = view.Body.Statements[0];
        Assert.That(__nunitAssertValue4, Is.TypeOf<BoundExpressionStatement>());
        var __nunitAssertValue5 = (((BoundExpressionStatement)__nunitAssertValue4)).Expression;
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundCall>());
        var rewrittenViewCall = ((BoundCall)__nunitAssertValue5);
        Assert.That(rewrittenUnsafeCall.Function, Is.EqualTo(4));
        Assert.That(rewrittenViewCall.Function, Is.EqualTo(5));
    }

    [Test]
    public void RewriteCallsVisitsNativeStorageAndAggregateChildren()
    {
        var storageIndex = new BoundCall(3, [], PrimitiveType.Int32, default);
        var element = new BoundCall(4, [], PrimitiveType.Float32, default);
        var storage = new BoundNativeStorageAddress(0, storageIndex,
            PrimitiveType.NativeFixedArray, PrimitiveType.Float32,
            PrimitiveType.NativeWindowHandle, 4, sizeof(float), 16, true, default);
        var layout = new BoundNativeFixedArrayLayout(PrimitiveType.NativeFixedArray,
            PrimitiveType.Float32, 4, sizeof(float), 16, 4);
        var array = new BoundNativeFixedArrayInitialization(layout, [element], default);
        var function = new BoundFunction("Fixture", PrimitiveType.Void,
            ImmutableArray<BoundVariable>.Empty, 0, new BoundBlock([
                new BoundExpressionStatement(storage, default),
                new BoundExpressionStatement(array, default),
            ], default), default);

        var rewritten = BoundTreeTraversal.RewriteCalls(function,
            static call => call with { Function = call.Function + 1 });
        var __nunitAssertValue6 = rewritten.Body.Statements[0];
        Assert.That(__nunitAssertValue6, Is.TypeOf<BoundExpressionStatement>());
        var __nunitAssertValue7 = (((BoundExpressionStatement)__nunitAssertValue6)).Expression;
        Assert.That(__nunitAssertValue7, Is.TypeOf<BoundNativeStorageAddress>());
        var rewrittenStorage = ((BoundNativeStorageAddress)__nunitAssertValue7);
        var __nunitAssertValue8 = rewritten.Body.Statements[1];
        Assert.That(__nunitAssertValue8, Is.TypeOf<BoundExpressionStatement>());
        var __nunitAssertValue9 = (((BoundExpressionStatement)__nunitAssertValue8)).Expression;
        Assert.That(__nunitAssertValue9, Is.TypeOf<BoundNativeFixedArrayInitialization>());
        var rewrittenArray = ((BoundNativeFixedArrayInitialization)__nunitAssertValue9);
        var __nunitAssertValue10 = rewrittenStorage.Index;
        Assert.That(__nunitAssertValue10, Is.TypeOf<BoundCall>());
        Assert.That((((BoundCall)__nunitAssertValue10)).Function, Is.EqualTo(4));
        var __nunitAssertValue11 = global::System.Linq.Enumerable.ToArray(rewrittenArray.Elements);
        Assert.That(__nunitAssertValue11, Has.Exactly(1).Items);
        var __nunitAssertValue12 = __nunitAssertValue11[0];
        Assert.That(__nunitAssertValue12, Is.TypeOf<BoundCall>());
        Assert.That((((BoundCall)__nunitAssertValue12)).Function, Is.EqualTo(5));
        Assert.That(BoundTreeTraversal.IsWithinLimits(rewritten.Body), Is.True);

        var invalidArray = new BoundNativeFixedArrayInitialization(layout, default, default);
        Assert.That(BoundTreeTraversal.IsWithinLimits(new BoundBlock([
            new BoundExpressionStatement(invalidArray, default),
        ], default)), Is.False);
        var invalidCall = new BoundCall(3, default, PrimitiveType.Void, default);
        Assert.That(BoundTreeTraversal.IsWithinLimits(new BoundBlock([
            new BoundExpressionStatement(invalidCall, default),
        ], default)), Is.False);
    }

    [Test]
    public void RewriteCallsVisitsNativeBlobGetterReceiver()
    {
        var receiver = new BoundCall(3, [], PrimitiveType.NativeBlobFixtureObject, default);
        var getters = new BoundNativeBlobGetters(receiver, NativeBlobFixtureContract.ValidationKey,
            NativeBlobFixtureContract.PointerGetter, NativeBlobFixtureContract.SizeGetter, default);
        var function = new BoundFunction("Fixture", PrimitiveType.Void,
            ImmutableArray<BoundVariable>.Empty, 0, new BoundBlock([
                new BoundExpressionStatement(getters, default),
            ], default), default);

        var rewritten = BoundTreeTraversal.RewriteCalls(function,
            static call => call with { Function = call.Function + 1 });
        var __nunitAssertValue13 = global::System.Linq.Enumerable.ToArray(rewritten.Body.Statements);
        Assert.That(__nunitAssertValue13, Has.Exactly(1).Items);
        var __nunitAssertValue14 = __nunitAssertValue13[0];
        Assert.That(__nunitAssertValue14, Is.TypeOf<BoundExpressionStatement>());
        var __nunitAssertValue15 = (((BoundExpressionStatement)__nunitAssertValue14)).Expression;
        Assert.That(__nunitAssertValue15, Is.TypeOf<BoundNativeBlobGetters>());
        var rewrittenGetters = ((BoundNativeBlobGetters)__nunitAssertValue15);
        var __nunitAssertValue16 = rewrittenGetters.Receiver;
        Assert.That(__nunitAssertValue16, Is.TypeOf<BoundCall>());
        Assert.That((((BoundCall)__nunitAssertValue16)).Function, Is.EqualTo(4));
        Assert.That(BoundTreeTraversal.IsWithinLimits(rewritten.Body), Is.True);
    }
}
