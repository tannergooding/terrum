// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Tests.Semantics;

internal static class CoreEvaluationFormTestHelpers
{
    private const string ModuleIdentity = "tests.consteval";
    private const string ExportKey = "const-eval";

    internal static CompilationOptions Options(
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast)
        => WithConstEval(new() { RuntimePolicy = runtimePolicy });

    internal static CompilationOptions WithConstEval(
        CompilationOptions options)
        => options with {
            CoreEvaluationFormIdentitySelection = ConstEvalSelection(
                options.RuntimePolicy),
        };

    internal static CompilationOptions WithContractFailureSelection(
        CompilationOptions options)
        => options with {
            CoreContractFailureIdentitySelection =
                new CoreContractFailureIdentitySelection(
                    "sample.consumer",
                    new CoreContractFailureDeclarationIdentity(
                        "core.contracts", "requirement-exception"),
                    new CoreContractFailureDeclarationIdentity(
                        "core.contracts", "postcondition-exception"),
                    new CoreContractFailureDeclarationIdentity(
                        "core.contracts", "require-exception-schema")),
        };

    internal static CompilationOptions
        OptionsWithConstEvalAndContractFailures(
            BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast)
        => WithConstEval(WithContractFailureSelection(new() {
            RuntimePolicy = runtimePolicy,
        }));

    internal static CoreEvaluationFormIdentitySelection ConstEvalSelection(
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast,
        string moduleIdentity = ModuleIdentity)
    {
        var declaration = new CoreEvaluationFormSchemaDeclaration(
            "ConstEval", 0, 0, [
                CoreEvaluationFormTargetKind.OrdinaryNamedCallable,
                CoreEvaluationFormTargetKind.IndividualAccessor,
                CoreEvaluationFormTargetKind
                    .EligibleInitializationConstructor,
            ], isPublic: true, isPrimary: true,
            isPrimaryTargetOnly: true, isRepeatable: false);
        var catalog = CoreEvaluationFormSchemaDeclarations.Create(
            new CoreProviderModuleOrigin(moduleIdentity, "tests/revision-1",
                new CoreProviderSchemaSelection("tests/evaluation-form", 1),
                1, 0, (uint)runtimePolicy),
            [declaration],
            [new(ExportKey, declaration)]);
        return new CoreEvaluationFormIdentitySelection(moduleIdentity,
            ConstEvalSchema: new CoreEvaluationFormSchemaSelector(
                moduleIdentity, ExportKey),
            LocalSchemaDeclarations: catalog);
    }

    internal static string ConstEvalManifestJson(
        BoundRuntimePolicy runtimePolicy,
        string moduleIdentity = ModuleIdentity,
        string coreAuthorityIdentity = "tests.core-authority")
        => $$"""
            {
              "schema": "terrumc.evaluation-core-manifest/1",
              "coreAuthorityIdentity": "{{coreAuthorityIdentity}}",
              "usingModuleIdentity": "{{moduleIdentity}}",
              "pureSchema": null,
              "constEvalSchema": {
                "providerIdentity": "{{moduleIdentity}}",
                "exportKey": "{{ExportKey}}"
              },
              "localSchemaDeclarations": {
                "origin": {
                  "moduleIdentity": "{{moduleIdentity}}",
                  "revisionIdentity": "tests/revision-1",
                  "schema": {
                    "identity": "tests/evaluation-form",
                    "revision": 1
                  },
                  "governingSemanticEdition": 1,
                  "targetRequirement": 0,
                  "runtimePolicy": {{(uint)runtimePolicy}}
                },
                "declarations": [
                  {
                    "qualifiedName": "ConstEval",
                    "genericArity": 0,
                    "argumentCount": 0,
                    "targets": [
                      "ordinary-named-callable",
                      "individual-accessor",
                      "eligible-initialization-constructor"
                    ],
                    "isPublic": true,
                    "isPrimary": true,
                    "isPrimaryTargetOnly": true,
                    "isRepeatable": false
                  }
                ],
                "exports": [
                  {
                    "exportKey": "{{ExportKey}}",
                    "declarationIndex": 0
                  }
                ]
              }
            }
            """;
}
