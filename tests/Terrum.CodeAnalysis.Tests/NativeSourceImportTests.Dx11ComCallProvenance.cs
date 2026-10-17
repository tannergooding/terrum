// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    [TestCase("presentation")]
    [TestCase("pipeline")]
    [TestCase("hardware")]
    [TestCase("main")]
    [TestCase("main-hardware")]
    public void Dx11ComCallsDeriveOwnerAndInterfaceProvenanceFromCatalog(string profile)
    {
        var program = ComProvenanceProgram(profile);
        SemanticVerifier.VerifyReferences(program);
        var callSites = ComProvenanceCallSites(program);
        var comCallSites = callSites.Where(site =>
            program.Functions[site.Call.Function].NativeComMethod is not null).ToArray();
        Assert.That(comCallSites, Is.Not.Empty);
        Assert.That(program.Functions[0].NativeImportKey, Is.Null);
        Assert.That(program.Functions[0].NativeComAcquisitionImportKey, Is.EqualTo(NativeDx11WarpAcquisitionContract.ImportKey));
        Assert.Multiple(() => {
            foreach (var function in program.Functions.Where(function => function.Intrinsic == BoundIntrinsic.NativeComOwnerClose && NativeDx11DirectShaderContract.IsOwnerName(function.DeclaringStruct ?? "")))
                Assert.That(function.NativeDx11DirectShaderMethod, Is.Null);
        });

        foreach (var site in comCallSites)
        {
            var target = program.Functions[site.Call.Function];
            var __nunitAssertValue0 = target.NativeComMethod;
            Assert.That(__nunitAssertValue0, Is.TypeOf<NativeComMethodDefinition>());
            var method = ((NativeComMethodDefinition)__nunitAssertValue0);
            Assert.That(method, Is.SameAs(NativeDx11ComCatalog.Method(method.Identity)));
            Assert.That(site.Call.Arguments[0], Is.TypeOf<BoundReceiverLoan>());
            var facts = site.Call.NativeComCall;
            if (target.Intrinsic == BoundIntrinsic.NativeComVTableCall)
            {
                Assert.That(facts, Is.Not.Null);
                Assert.That(facts.Method, Is.SameAs(method));
            }
            else
            {
                Assert.That(facts, Is.Null);
            }
            var acquisitions = NativeComCallContract.Acquisitions(
                program.Functions[site.FunctionIndex], program);

            for (var argumentIndex = 0; argumentIndex < site.Call.Arguments.Length; argumentIndex++)
            {
                if (site.Call.Arguments[argumentIndex] is not BoundReceiverLoan loan
                    || NativeComCallContract.Owner(loan.Type, program) is not NativeComOwnerDefinition owner)
                {
                    continue;
                }
                var parameter = site.Call.ParameterIndex(argumentIndex);
                var nativeParameter = NativeComCallContract.NativeParameterIndex(target, parameter, method);
                _ = NativeComCallContract.LoanInterface(method, nativeParameter, owner, program);

                if (acquisitions is null || !acquisitions.TryGetValue(loan.Variable, out var acquisitionFunction))
                {
                    throw new InvalidOperationException("A borrowed COM owner has no verified acquisition.");
                }
                if (facts is not null)
                {
                    var __nunitAssertValue1 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(facts.Loans, item => item.ParameterIndex == parameter));
                    Assert.That(__nunitAssertValue1, Has.Exactly(1).Items);
                    var retained = __nunitAssertValue1[0];
                    Assert.That(retained.Variable, Is.EqualTo(loan.Variable));
                    Assert.That(retained.Owner, Is.SameAs(owner));
                    Assert.That(retained.Interface, Is.SameAs(NativeComCallContract.LoanInterface(method, nativeParameter, owner, program)));
                    Assert.That(retained.AcquisitionFunction, Is.EqualTo(acquisitionFunction));
                }
                Assert.That(callSites, Has.Some.Matches(acquisitionSite =>
                    acquisitionSite.FunctionIndex == site.FunctionIndex
                    && acquisitionSite.Call.Function == acquisitionFunction
                    && acquisitionSite.Call.Arguments.OfType<BoundOutArgument>().Any(output =>
                        output.Variable == loan.Variable && output.Type == loan.Type)));
            }
        }
        var __nunitAssertValue2 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(comCallSites, site =>
                    program.Functions[site.Call.Function].NativeComMethod?.Identity
                        == NativeDx11ComCatalog.GetBufferIdentity));
        Assert.That(__nunitAssertValue2, Has.Exactly(1).Items);
        var getBuffer = __nunitAssertValue2[0];
        var __nunitAssertValue3 = getBuffer.Call.Arguments[0];
        Assert.That(__nunitAssertValue3, Is.TypeOf<BoundReceiverLoan>());
        var getReceiver = ((BoundReceiverLoan)__nunitAssertValue3);
        Assert.That(NativeComCallContract.Owner(getReceiver.Type, program)?.InterfaceIdentity, Is.EqualTo(NativeDx11ComCatalog.SwapChain));
        var __nunitAssertValue4 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(comCallSites, site =>
                    program.Functions[site.Call.Function].NativeComMethod?.Identity
                        == NativeDx11ComCatalog.CreateRenderTargetViewIdentity));
        Assert.That(__nunitAssertValue4, Has.Exactly(1).Items);
        var createTarget = __nunitAssertValue4[0];
        var __nunitAssertValue5 = program.Functions[createTarget.Call.Function].NativeComMethod;
        Assert.That(__nunitAssertValue5, Is.TypeOf<NativeComMethodDefinition>());
        var createMethod = ((NativeComMethodDefinition)__nunitAssertValue5);
        var outputIndex = Enumerable.Range(0, createTarget.Call.Arguments.Length)
            .Single(index => createTarget.Call.Arguments[index] is BoundOutArgument);
        Assert.That(NativeComCallContract.NativeParameterIndex(
            program.Functions[createTarget.Call.Function],
            createTarget.Call.ParameterIndex(outputIndex), createMethod), Is.EqualTo(createMethod.OutputParameterIndex));
        var textureArgumentIndex = Enumerable.Range(0, createTarget.Call.Arguments.Length)
            .Single(index => createTarget.Call.Arguments[index] is BoundReceiverLoan loan
                && NativeComCallContract.Owner(loan.Type, program)?.InterfaceIdentity
                    == NativeDx11ComCatalog.Texture2D);
        var __nunitAssertValue6 = createTarget.Call.Arguments[textureArgumentIndex];
        Assert.That(__nunitAssertValue6, Is.TypeOf<BoundReceiverLoan>());
        var textureLoan = ((BoundReceiverLoan)__nunitAssertValue6);
        var __nunitAssertValue7 = NativeComCallContract.Owner(textureLoan.Type, program);
        Assert.That(__nunitAssertValue7, Is.TypeOf<NativeComOwnerDefinition>());
        var textureOwner = ((NativeComOwnerDefinition)__nunitAssertValue7);
        Assert.That(NativeComCallContract.LoanInterface(createMethod,
                NativeComCallContract.NativeParameterIndex(program.Functions[createTarget.Call.Function],
                    createTarget.Call.ParameterIndex(textureArgumentIndex), createMethod),
                textureOwner, program).NominalIdentity, Is.EqualTo(NativeDx11ComCatalog.Resource));

        if (profile != "presentation")
        {
            var __nunitAssertValue8 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(comCallSites, site =>
                            program.Functions[site.Call.Function].NativeComMethod?.Identity
                                == NativeDx11ComCatalog.BlobAddRefIdentity));
            Assert.That(__nunitAssertValue8, Has.Exactly(1).Items);
            var clone = __nunitAssertValue8[0];
            var __nunitAssertValue9 = clone.Call.Arguments[0];
            Assert.That(__nunitAssertValue9, Is.TypeOf<BoundReceiverLoan>());
            var cloneReceiver = ((BoundReceiverLoan)__nunitAssertValue9);
            var cloneAcquisitions = NativeComCallContract.Acquisitions(
                program.Functions[clone.FunctionIndex], program);
            if (cloneAcquisitions is null
                || !cloneAcquisitions.TryGetValue(cloneReceiver.Variable, out var acquisitionFunction))
            {
                throw new InvalidOperationException("A cloned COM owner has no verified acquisition.");
            }
            Assert.That(program.Functions[acquisitionFunction].Intrinsic, Is.EqualTo(BoundIntrinsic.Dx11ShaderCompileFromFile));
        }

        var code = DirectWindowDeviceShaderCode(program);
        foreach (var functionIndex in comCallSites.Select(static site => site.Call.Function).Distinct())
        {
            var __nunitAssertValue10 = program.Functions[functionIndex].NativeComMethod;
            Assert.That(__nunitAssertValue10, Is.TypeOf<NativeComMethodDefinition>());
            var method = ((NativeComMethodDefinition)__nunitAssertValue10);
            var __nunitAssertValue11 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.Functions, item => item.Start == code.SourceFunctionOffsets[functionIndex]));
            Assert.That(__nunitAssertValue11, Has.Exactly(1).Items);
            var range = __nunitAssertValue11[0];
            var bytes = code.Code.AsSpan(range.Start, range.End - range.Start).ToArray();
            var offset = method.Slot * 8;
            byte[] dispatch = offset <= 127 ? [0x48, 0x8B, 0x01, 0xFF, 0x50, (byte)offset]
                : [0x48, 0x8B, 0x01, 0xFF, 0x90, (byte)(offset & 255), (byte)(offset >> 8), 0, 0];
            Assert.That(ContainsSequence(bytes, dispatch), Is.True, method.Identity);
            if (method.Identity == NativeDx11ComCatalog.PresentIdentity)
            {
                Assert.That(ContainsSequence(bytes,
                    [0xB8, 1, 0, 0, 0, 0x89, 0xC2, 0xB8, 0, 0, 0, 0, 0x41, 0x89, 0xC0]), Is.True, Convert.ToHexString(bytes));
                Assert.That(ContainsSequence(bytes, [0x85, 0xC0, 0x0F, 0x88]), Is.True);
            }
            else if (method.Identity == NativeDx11ComCatalog.ClearStateIdentity)
            {
                Assert.That(ContainsSequence(bytes, [0x85, 0xC0]), Is.False);
            }
        }
        Assert.That(code.NativeImports, Has.None.Matches(static import =>
            import.ExportName.StartsWith("NativeFfi", StringComparison.Ordinal)));
    }

    [TestCase("presentation")]
    [TestCase("pipeline")]
    [TestCase("hardware")]
    public void GenericNativeComCallsRejectForgedProvenanceFacts(string profile)
    {
        var program = ComProvenanceProgram(profile);
        var call = ComProvenanceCalls(program).First(call =>
            program.Functions[call.Function].Intrinsic == BoundIntrinsic.NativeComVTableCall
            && call.NativeComCall is not null);
        var __nunitAssertValue12 = call.NativeComCall;
        Assert.That(__nunitAssertValue12, Is.TypeOf<NativeComCallFacts>());
        var facts = ((NativeComCallFacts)__nunitAssertValue12);
        var receiver = facts.Loans[0];
        var forgeries = new NativeComCallFacts?[] {
            null,
            facts with { Method = facts.Method with { Slot = facts.Method.Slot + 1 } },
            facts with { Method = facts.Method with { Signature = "HRESULT(void*)" } },
            facts with { Loans = [] },
            facts with { Loans = facts.Loans.Add(receiver) },
            facts with { Loans = facts.Loans.SetItem(0, receiver with { ParameterIndex = 1 }) },
            facts with { Loans = facts.Loans.SetItem(0, receiver with { Variable = receiver.Variable + 1 }) },
            facts with { Loans = facts.Loans.SetItem(0, receiver with { AcquisitionFunction = call.Function }) },
            facts with { Loans = facts.Loans.SetItem(0, receiver with {
                Interface = NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.Unknown),
            }) },
        };

        foreach (var forgery in forgeries)
        {
            var forged = RewriteComProvenanceCall(program, call,
                candidate => candidate with { NativeComCall = forgery });
            Assert.That(profile == "presentation"
                ? NativeDx11DirectPresentationContract.MatchesProgram(forged)
                : NativeDx11DirectPipelineContract.MatchesCompatibleProgram(forged), Is.True);
            Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forged));
            Assert.Throws<ArgumentException>(() =>
                NativeEmitter.EmitWithNativeImports(forged, NativeRuntimePolicy.CheckedExceptions));
        }
    }

    [TestCase("presentation")]
    [TestCase("pipeline")]
    [TestCase("hardware")]
    public void Dx11ComCallsRejectOwnerLoansWithoutLiveProvenance(string profile)
    {
        var program = ComProvenanceProgram(profile);
        var call = ComProvenanceCalls(program).First(call =>
            program.Functions[call.Function].NativeComMethod is not null
            && call.Arguments[0] is BoundReceiverLoan);
        var __nunitAssertValue13 = call.Arguments[0];
        Assert.That(__nunitAssertValue13, Is.TypeOf<BoundReceiverLoan>());
        var receiver = ((BoundReceiverLoan)__nunitAssertValue13);
        var forged = RewriteComProvenanceCall(program, call, candidate => candidate with {
            Arguments = candidate.Arguments.SetItem(0, receiver with { Variable = -1 }),
        });

        Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forged));
        Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImports(forged, NativeRuntimePolicy.CheckedExceptions));
    }

    [Test]
    public void GenericNativeComCallsRejectMovingBorrowedOwnerArguments()
    {
        var program = ComProvenanceProgram("main");
        var call = ComProvenanceCalls(program).First(call =>
            program.Functions[call.Function].Intrinsic == BoundIntrinsic.NativeComVTableCall
            && call.Arguments.Count(argument =>
                argument is BoundReceiverLoan loan
                && NativeComCallContract.Owner(loan.Type, program) is not null) > 1);
        var argumentIndex = Enumerable.Range(0, call.Arguments.Length)
            .Where(index => call.Arguments[index] is BoundReceiverLoan loan
                && NativeComCallContract.Owner(loan.Type, program) is not null)
            .Skip(1).First();
        var __nunitAssertValue14 = call.Arguments[argumentIndex];
        Assert.That(__nunitAssertValue14, Is.TypeOf<BoundReceiverLoan>());
        var loan = ((BoundReceiverLoan)__nunitAssertValue14);
        var movedOwner = RewriteComProvenanceCall(program, call, candidate => candidate with {
            Arguments = candidate.Arguments.SetItem(argumentIndex,
                new BoundMove(loan.Variable, loan.Type, loan.Span)),
        });

        Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(movedOwner));
        Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImports(movedOwner, NativeRuntimePolicy.CheckedExceptions));
    }

    [TestCase("swapchain.Present()", "device.Present()", "TRM3011")]
    [TestCase("context.ClearState()", "swapchain.ClearState()", "TRM3011")]
    [TestCase("swapchain.TryGetBackBuffer", "context.TryGetBackBuffer", "TRM3011")]
    [TestCase("backBuffer, out var renderTarget", "buffer, out var renderTarget", "TRM3007")]
    public void Dx11ComProvenanceRejectsSourceReceiverAndOwnerTransplants(
        string original, string replacement, string expectedDiagnosticCode)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "samples", "design", "win32-dx11-triangle", "main.terrum"));
        var baseline = Compilation.CreateWithNativeImports(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(baseline.HasErrors, Is.False, string.Join(Environment.NewLine, baseline.Diagnostics));
        var __nunitAssertValue15 = baseline.Program;
        Assert.That(__nunitAssertValue15, Is.TypeOf<BoundProgram>());
        Assert.That(NativeDx11DirectPipelineContract.MatchesProgram(((BoundProgram)__nunitAssertValue15)), Is.True);
        Assert.That(source.Contains(original, StringComparison.Ordinal), Is.True);
        var mutated = source.Replace(original, replacement, StringComparison.Ordinal);
        Assert.That(mutated, Is.Not.EqualTo(source));
        var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(mutated),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Has.Some.Matches(diagnostic => diagnostic.Code == expectedDiagnosticCode));
    }

    private static BoundProgram ComProvenanceProgram(string profile) => profile switch {
        "presentation" => CompileDirectWindowDevicePresent(),
        "pipeline" => CompileDirectWindowDevicePipeline(),
        "hardware" => CompileDirectHardwarePipeline(),
        "main" => AssertDx11SampleEntryPointUsesExactResizeRenderSeals("main.terrum", hardware: false),
        "main-hardware" => AssertDx11SampleEntryPointUsesExactResizeRenderSeals("main-hardware.terrum", hardware: true),
        _ => throw new ArgumentException("Unknown closed sample profile.", nameof(profile)),
    };

    private static List<(int FunctionIndex, BoundCall Call)> ComProvenanceCallSites(BoundProgram program)
    {
        var calls = new List<(int FunctionIndex, BoundCall Call)>();
        for (var functionIndex = 0; functionIndex < program.Functions.Length; functionIndex++)
        {
            var index = functionIndex;
            _ = BoundTreeTraversal.RewriteCalls(program.Functions[index], call => {
                calls.Add((index, call));
                return call;
            });
        }
        return calls;
    }

    private static List<BoundCall> ComProvenanceCalls(BoundProgram program) =>
        ComProvenanceCallSites(program).Select(static site => site.Call).ToList();

    private static BoundProgram RewriteComProvenanceCall(BoundProgram program, BoundCall selected,
        Func<BoundCall, BoundCall> rewrite)
    {
        var matches = 0;
        var result = program with {
            Functions = [.. program.Functions.Select(function => BoundTreeTraversal.RewriteCalls(function,
                call => {
                    if (call.Function != selected.Function || call.Span != selected.Span)
                    {
                        return call;
                    }
                    matches++;
                    return rewrite(call);
                }))],
        };
        Assert.That(matches, Is.EqualTo(1));
        return result;
    }
}
