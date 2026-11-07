// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmGeneralPredicateGraphValidator
{
    internal static byte[] Validate(
        TrRmArtifactSchema schema,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmContractTableReader contracts, TrRmTypeSpecTableReader typeSpecs,
        TrRmTypeDefTableReader typeDefs,
        TrRmEnumDefTableReader enumDefs,
        TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs,
        TrRmImmutableDataTableReader immutableData,
        TrRmPredicateGraphTableReader graphs,
        TrRmPredicateReadFootprintTableReader footprints,
        TrRmPredicateReadRootTableReader roots,
        TrRmPredicateReadDependencyTableReader dependencies,
        TrRmPredicateNodeTableReader nodes,
        TrRmPredicateNodeOperandTableReader operands,
        TrRmPredicateChildEdgeTableReader edges,
        TrRmPredicateCaptureTableReader captures,
        TrRmPredicateCallTableReader calls,
        TrRmPredicateOperationTableReader operations,
        TrRmPredicateTypeUseTableReader typeUses,
        TrRmPredicateControlTableReader controls,
        TrRmPredicateTypePatternTableReader typePatterns,
        TrRmEvaluationReadBoundTableReader readBounds,
        TrRmConstantSourceTableReader constantSources,
        List<TrRmConstantExecutionUse> constantExecutionUses)
    {
        if (!schema.UsesGeneralPredicateGraphs()
            || graphs.Count != contracts.PredicateGraphAttachmentCount)
        {
            throw InvalidGraph();
        }

        var anchorMethods = new byte[methods.Count + 1];
        for (var typeSpecRow = 1; typeSpecRow <= typeSpecs.Count; typeSpecRow++)
        {
            if (typeSpecs.TryGetNativeFunctionPointerAnchor(
                    typeSpecRow, out _, out var method))
            {
                anchorMethods[method] = 1;
            }
        }

        var graphMethods = new int[graphs.Count + 1];
        var graphPhases = new byte[graphs.Count + 1];
        var graphOrdinals = new uint[graphs.Count + 1];
        for (var method = 1; method <= methods.Count; method++)
        {
            foreach (var clause in contracts.GetPredicateGraphContracts(method))
            {
                var graph = clause.PredicateGraphRow;
                if (graph <= 0 || graph > graphs.Count || graphMethods[graph] != 0
                    || clause.PhaseTag is < 1 or > 3)
                {
                    throw InvalidGraph();
                }

                graphMethods[graph] = method;
                graphPhases[graph] = clause.PhaseTag;
                graphOrdinals[graph] = clause.PhaseLocalOrdinal;
                var requiredFeature = clause.PhaseTag == 3
                    ? TrRmFeatureTableReader.PublishedPredicatesFeatureId
                    : TrRmFeatureTableReader.GeneralPredicateGraphsFeatureId;
                RequireFeature(features, modules, methods.GetOwnerModule(method),
                    requiredFeature, schema);
            }
        }

        for (var graph = 1; graph <= graphs.Count; graph++)
        {
            if (graphMethods[graph] == 0)
            {
                throw InvalidGraph();
            }
        }

        for (var capture = 1; capture <= captures.Count; capture++)
        {
            if (captures.GetRole(capture) != 3)
            {
                continue;
            }

            var capturedType = typeSpecs.GetTypeSpecRef(
                captures.GetCapturedTypeSpecRef(capture));
            if (!capturedType.IsRow || capturedType.Form != 0x10
                || typeSpecs.GetPredicateCaptureReference(capturedType.Row) != capture)
            {
                throw InvalidGraph();
            }
        }
        for (var typeSpec = 1; typeSpec <= typeSpecs.Count; typeSpec++)
        {
            if (typeSpecs.GetRow(typeSpec)[0] != 0x10)
            {
                continue;
            }

            var capture = typeSpecs.GetPredicateCaptureReference(typeSpec);
            if (captures.GetRole(capture) != 3
                || captures.GetCapturedTypeSpecRef(capture) != (uint)typeSpec)
            {
                throw InvalidGraph();
            }
        }

        var captureDefinitions = new byte[captures.Count + 1];
        var graphForNode = new int[nodes.Count + 1];
        var parentNodes = new int[nodes.Count + 1];
        var parentOrdinals = new uint[nodes.Count + 1];
        var pending = new int[nodes.Count];
        var nodeCountsByGraph = new int[graphs.Count + 1];
        for (var graph = 1; graph <= graphs.Count; graph++)
        {
            var root = graphs.GetRootNodeRow(graph);
            if (graphForNode[root] != 0)
            {
                throw InvalidGraph();
            }

            var pendingCount = 0;
            pending[pendingCount++] = root;
            graphForNode[root] = graph;
            while (pendingCount != 0)
            {
                var parent = pending[--pendingCount];
                nodeCountsByGraph[graph]++;
                for (var ordinal = 0; ordinal < edges.GetChildCount(parent); ordinal++)
                {
                    var child = edges.GetChildNodeRowByOrdinal(parent, (uint)ordinal);
                    if (graphForNode[child] != 0)
                    {
                        throw InvalidGraph();
                    }

                    graphForNode[child] = graph;
                    parentNodes[child] = parent;
                    parentOrdinals[child] = (uint)ordinal;
                    pending[pendingCount++] = child;
                }
            }
        }

        for (var node = 1; node <= nodes.Count; node++)
        {
            if (graphForNode[node] == 0)
            {
                throw InvalidGraph();
            }
        }

        var usedFootprints = new byte[footprints.Count + 1];
        var usedRoots = new byte[roots.Count + 1];
        var usedDependencies = new byte[dependencies.Count + 1];
        var selectorKinds = new byte[graphs.Count + 1];
        var callOwners = new int[calls.Count + 1];
        var visitedValueTypeRows = new HashSet<int>[modules.Count + 1];
        for (var graph = 1; graph <= graphs.Count; graph++)
        {
            var method = graphMethods[graph];
            var phase = graphPhases[graph];
            var footprint = graphs.GetReadFootprintRow(graph);
            if (usedFootprints[footprint] != 0)
            {
                throw InvalidGraph();
            }
            usedFootprints[footprint] = 1;

            var rootNode = graphs.GetRootNodeRow(graph);
            if (!typeSpecs.AreEquivalentTypeSpecRefs(
                    nodes.GetResultTypeSpecRef(rootNode), 0xF200_0001)
                || !ValidateGraphShape(graph, phase, rootNode, nodeCountsByGraph[graph],
                    method, methods, footprint, graphForNode, graphMethods,
                    graphPhases, graphOrdinals, parentNodes, parentOrdinals,
                    footprints, roots, dependencies, anchorMethods, immutableData,
                    usedRoots, usedDependencies,
                    nodes, operands, edges,
                    captures, captureDefinitions, calls, callOwners, operations,
                    typeUses, controls, typePatterns, constantSources,
                    constantExecutionUses,
                    features, modules, typeSpecs, signatures, typeDefs,
                    enumDefs, genericParams, imports, frozenValueNodes, blobs,
                    visitedValueTypeRows))
            {
                throw InvalidGraph();
            }
            if (phase == 3)
            {
                selectorKinds[graph] = checked((byte)nodes.GetNodeKind(rootNode));
            }
        }

        for (var capture = 1; capture <= captures.Count; capture++)
        {
            if (captureDefinitions[capture] != 1)
            {
                throw InvalidGraph();
            }
        }

        for (var call = 1; call <= calls.Count; call++)
        {
            if (callOwners[call] == 0)
            {
                throw InvalidGraph();
            }
        }

        var boundMethods = new byte[methods.Count + 1];
        for (var row = 1; row <= readBounds.Count; row++)
        {
            var method = readBounds.GetOwnerMethodDefRow(row);
            var footprint = readBounds.GetReadFootprintRow(row);
            if (boundMethods[method] != 0 || usedFootprints[footprint] != 0
                || anchorMethods[method] != 0)
            {
                throw InvalidGraph();
            }

            boundMethods[method] = 1;
            usedFootprints[footprint] = 1;
            var rootFirst = footprints.GetRootFirstRow(footprint);
            var rootCount = footprints.GetRootRowCount(footprint);
            var dependencyFirst = footprints.GetDependencyFirstRow(footprint);
            var dependencyCount = footprints.GetDependencyRowCount(footprint);
            var rootKeys = new HashSet<(byte Kind, uint Identity)>();
            var dependencyKeys = new HashSet<(byte Kind, uint Reference)>();
            for (var ordinal = 0; ordinal < rootCount; ordinal++)
            {
                var root = rootFirst + ordinal;
                var kind = roots.GetRootKind(root);
                var identity = roots.GetRootIdentity(root);
                if (usedRoots[root] != 0 || kind == 8
                    || !rootKeys.Add((kind, identity)))
                {
                    throw InvalidGraph();
                }
                if (kind == 6)
                {
                    if (!TrRmPredicateTableValidation.IsRowReference(
                            identity, immutableData.Count))
                    {
                        throw InvalidGraph();
                    }

                    RequireFeature(features, modules,
                        methods.GetOwnerModule(method),
                        TrRmFeatureTableReader.ImmutableDataIdentitiesFeatureId,
                        TrRmArtifactSchema.GeneralPredicateGraphsDraft);
                }
                usedRoots[root] = 1;
            }
            for (var ordinal = 0; ordinal < dependencyCount; ordinal++)
            {
                var dependency = dependencyFirst + ordinal;
                var key = (dependencies.GetDependencyKind(dependency),
                    dependencies.GetDependencyRef(dependency));
                if (usedDependencies[dependency] != 0 || !dependencyKeys.Add(key))
                {
                    throw InvalidGraph();
                }
                usedDependencies[dependency] = 1;
            }
        }

        for (var method = 1; method <= methods.Count; method++)
        {
            var forms = contracts.GetEvaluationFormContracts(method);
            var requiresBound = !forms.IsEmpty;
            if (requiresBound != (boundMethods[method] != 0)
                || (requiresBound && anchorMethods[method] != 0))
            {
                throw InvalidGraph();
            }

            foreach (var form in forms)
            {
                if (form.FormTag == 2)
                {
                    RequireFeature(features, modules, methods.GetOwnerModule(method),
                        TrRmFeatureTableReader.ConstantExecutionFeatureId, schema);
                }
            }
        }

        for (var footprint = 1; footprint <= footprints.Count; footprint++)
        {
            if (usedFootprints[footprint] == 0)
            {
                throw InvalidGraph();
            }
        }
        for (var row = 1; row <= roots.Count; row++)
        {
            if (usedRoots[row] == 0)
            {
                throw InvalidGraph();
            }
        }
        for (var row = 1; row <= dependencies.Count; row++)
        {
            if (usedDependencies[row] == 0)
            {
                throw InvalidGraph();
            }
        }

        ValidateOutputSelectors(selectorKinds, methods, contracts);
        return selectorKinds;
    }

    private static bool ValidateGraphShape(int graph, byte phase, int rootNode,
        int nodeCount, int method, TrRmMethodDefTableReader methods,
        int footprint, int[] graphForNode, int[] graphMethods,
        byte[] graphPhases, uint[] graphOrdinals,
        int[] parentNodes, uint[] parentOrdinals,
        TrRmPredicateReadFootprintTableReader footprints,
        TrRmPredicateReadRootTableReader roots,
        TrRmPredicateReadDependencyTableReader dependencies,
        byte[] anchorMethods,
        TrRmImmutableDataTableReader immutableData,
        byte[] usedRoots, byte[] usedDependencies,
        TrRmPredicateNodeTableReader nodes,
        TrRmPredicateNodeOperandTableReader operands,
        TrRmPredicateChildEdgeTableReader edges,
        TrRmPredicateCaptureTableReader captures,
        byte[] captureDefinitions,
        TrRmPredicateCallTableReader calls,
        int[] callOwners,
        TrRmPredicateOperationTableReader operations,
        TrRmPredicateTypeUseTableReader typeUses,
        TrRmPredicateControlTableReader controls,
        TrRmPredicateTypePatternTableReader typePatterns,
        TrRmConstantSourceTableReader constantSources,
        List<TrRmConstantExecutionUse> constantExecutionUses,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmTypeSpecTableReader typeSpecs, TrRmMethodSigTableReader signatures,
        TrRmTypeDefTableReader typeDefs,
        TrRmEnumDefTableReader enumDefs,
        TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs,
        HashSet<int>[] visitedValueTypeRows)
    {
        var rootFirst = footprints.GetRootFirstRow(footprint);
        var rootCount = footprints.GetRootRowCount(footprint);
        var dependencyFirst = footprints.GetDependencyFirstRow(footprint);
        var dependencyCount = footprints.GetDependencyRowCount(footprint);
        var rootKeys = new HashSet<(byte Kind, uint Identity)>();
        var dependencyKeys = new HashSet<(byte Kind, uint Reference)>();
        var methodSignature = methods.GetSignature(method);
        var parameterCount = signatures.GetParamRange(methodSignature).Count;
        var ownerModule = methods.GetOwnerModule(method);
        for (var ordinal = 0; ordinal < rootCount; ordinal++)
        {
            var row = rootFirst + ordinal;
            var kind = roots.GetRootKind(row);
            var identity = roots.GetRootIdentity(row);
            if (usedRoots[row] != 0 || !rootKeys.Add((kind, identity)))
            {
                return false;
            }
            usedRoots[row] = 1;
            if (kind == 6)
            {
                if (!TrRmPredicateTableValidation.IsRowReference(
                        identity, immutableData.Count))
                {
                    return false;
                }

                RequireFeature(features, modules, ownerModule,
                    TrRmFeatureTableReader.ImmutableDataIdentitiesFeatureId,
                    TrRmArtifactSchema.GeneralPredicateGraphsDraft);
            }
            if (kind == 8)
            {
                if (phase == 3 || !IsEarlierSamePhaseCapture(
                        identity, method, phase, graph, graphOrdinals,
                        graphMethods, graphPhases, captures))
                {
                    return false;
                }
            }
            var invalidInputRoot = (kind is 2 or 3 or 4)
                && identity >= (uint)parameterCount;
            var invalidReceiverRoot = kind == 1
                && signatures.GetReceiverTypeSpec(methodSignature) == 0;
            var invalidResultRoot = kind == 5 && phase != 2;
            if (invalidInputRoot || invalidReceiverRoot || invalidResultRoot)
            {
                return false;
            }
        }

        for (var ordinal = 0; ordinal < dependencyCount; ordinal++)
        {
            var row = dependencyFirst + ordinal;
            if (usedDependencies[row] != 0
                || !dependencyKeys.Add((dependencies.GetDependencyKind(row),
                    dependencies.GetDependencyRef(row))))
            {
                return false;
            }
            usedDependencies[row] = 1;
        }

        var selectorKind = nodes.GetNodeKind(rootNode);
        if (phase == 3)
        {
            if (nodeCount != 1 || selectorKind is < 1 or > 6
                || rootCount != 1 || dependencyCount != 0
                || edges.GetChildCount(rootNode) != 0
                || nodes.GetOperandCount(rootNode) != 1
                || roots.GetRootKind(rootFirst) != 5
                || roots.GetRootIdentity(rootFirst) != 0)
            {
                return false;
            }

            var operand = nodes.GetOperandFirstRow(rootNode);
            return operands.GetOperandKind(checked((int)operand)) == 1
                && operands.GetOperandRef(checked((int)operand)) == (uint)rootFirst
                && HasSupportedOwnerResult(selectorKind,
                    signatures.GetResultTypeSpec(methodSignature), typeSpecs);
        }

        var isExpressionKind = selectorKind is >= 0x10 and <= 0x36;
        if (!isExpressionKind)
        {
            return false;
        }

        for (var node = 1; node <= nodes.Count; node++)
        {
            if (graphForNode[node] != graph)
            {
                continue;
            }

            var nodeKind = nodes.GetNodeKind(node);
            var childCount = edges.GetChildCount(node);
            if (!ValidateNodeShape(node, nodeKind, childCount, nodes, operands,
                    calls, graph, operations, typeUses,
                    controls, typePatterns, captures))
            {
                return false;
            }
            if (!ValidateNodeRelation(node, nodeKind, parentNodes[node],
                    parentOrdinals[node], nodes, edges))
            {
                return false;
            }
            var (captureOperand, definitionRole) = nodeKind switch
            {
                0x45 => (1, (byte)1),
                0x46 or 0x47 => (0, (byte)1),
                0x48 when nodes.GetOperandCount(node) == 2 => (1, (byte)2),
                0x49 when nodes.GetOperandCount(node) == 2 => (1, (byte)1),
                _ => (-1, (byte)0),
            };
            if (captureOperand >= 0
                && !RecordCaptureDefinition(
                    GetOperandRef(node, captureOperand, nodes, operands),
                    graph, definitionRole, captureDefinitions, captures))
            {
                return false;
            }
            if (nodeKind is 0x35 or 0x36)
            {
                RequireFeature(features, modules, ownerModule,
                    TrRmFeatureTableReader.LayoutQueriesFeatureId,
                    TrRmArtifactSchema.GeneralPredicateGraphsDraft);
            }

            var firstOperand = nodes.GetOperandFirstRow(node);
            var operandCount = nodes.GetOperandCount(node);
            for (uint operandOrdinal = 0; operandOrdinal < operandCount; operandOrdinal++)
            {
                var operandRow = checked((int)(firstOperand + operandOrdinal));
                var operandKind = operands.GetOperandKind(operandRow);
                var operandRef = operands.GetOperandRef(operandRow);
                switch (operandKind)
                {
                    case 1:
                        if (!IsInRange(operandRef, rootFirst, rootCount))
                        {
                            return false;
                        }
                        var rootKind = roots.GetRootKind(checked((int)operandRef));
                        if ((nodeKind is 0x10 or 0x11)
                            && !IsSubjectRootAllowed(nodeKind, rootKind))
                        {
                            return false;
                        }
                        break;
                    case 2:
                        RequireFeature(features, modules, ownerModule,
                            TrRmFeatureTableReader.TypedFrozenValuesFeatureId,
                            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
                        break;
                    case 3:
                        if (captures.GetGraphRow(checked((int)operandRef)) != graph)
                        {
                            return false;
                        }
                        var captureRole = captures.GetRole(checked((int)operandRef));
                        if ((nodeKind == 0x12 && captureRole is not (1 or 2))
                            || ((nodeKind is 0x45 or 0x46 or 0x47)
                                && captureRole != 1)
                            || (nodeKind == 0x48 && operandOrdinal == 1
                                && captureRole != 2)
                            || (nodeKind == 0x49 && operandOrdinal == 1
                                && captureRole != 1))
                        {
                            return false;
                        }
                        break;
                    case 5:
                            var callRow = checked((int)operandRef);
                            if (!IsPredicateCallTargetCallable(
                                    callRow, calls, dependencies, anchorMethods)
                                || !CallTypesAreVisible(callRow,
                                    graph, method, phase, graphMethods, graphPhases,
                                    graphOrdinals, calls, typeSpecs, captures))
                            {
                                return false;
                            }
                            var ownerTypeSpecRef
                                = calls.GetOwnerTypeSpecRef(callRow);
                            if (ownerTypeSpecRef != 0)
                            {
                                ValidateTypeSpecValueDependencies(
                                    ownerTypeSpecRef, ownerModule, typeSpecs,
                                    typeDefs, enumDefs, genericParams, imports,
                                    constantSources, frozenValueNodes, blobs,
                                    features, modules, constantExecutionUses,
                                    visitedValueTypeRows);
                            }
                            var callDependency = calls.GetDependencyRef(callRow);
                            if (!IsInRange(callDependency, dependencyFirst, dependencyCount)
                                || (callOwners[callRow] != 0
                                    && callOwners[callRow] != graph))
                            {
                                return false;
                            }
                            callOwners[callRow] = graph;
                            for (var argument = 0u;
                                argument < calls.GetGenericArgumentCount(callRow); argument++)
                            {
                                var argumentOrdinal = checked((int)argument);
                                var argumentKind = calls.GetGenericArgumentKind(
                                    callRow, argumentOrdinal);
                                var argumentRef = calls.GetGenericArgumentRef(
                                    callRow, argumentOrdinal);
                                if (argumentKind == 1)
                                {
                                    ValidateTypeSpecValueDependencies(argumentRef,
                                        ownerModule, typeSpecs, typeDefs, enumDefs, genericParams,
                                        imports, constantSources, frozenValueNodes,
                                        blobs, features, modules,
                                        constantExecutionUses,
                                        visitedValueTypeRows);
                                    continue;
                                }
                                if (argumentKind != 2)
                                {
                                    continue;
                                }

                                var sourceRow = checked((int)argumentRef);
                            if (constantSources.GetKind(sourceRow) == 2)
                            {
                                constantExecutionUses.Add(
                                    new TrRmConstantExecutionUse(
                                        checked((int)constantSources
                                            .GetPayloadReference(sourceRow)),
                                        ownerModule, 0));
                            }
                        }
                        break;
                    case 7:
                        RequireFeature(features, modules, ownerModule,
                            TrRmFeatureTableReader.LayoutQueriesFeatureId,
                            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
                        break;
                    case 9:
                        RequireFeature(features, modules, ownerModule,
                            TrRmFeatureTableReader.NominalUnionMetadataFeatureId,
                            TrRmArtifactSchema.GeneralPredicateGraphsDraft);
                        break;
                    case 6:
                        var typeSpecRef = typeUses.GetTypeSpecRef(
                            checked((int)operandRef));
                        if (!CapturedTypeIsVisible(typeSpecRef,
                            graph, method, phase, graphMethods, graphPhases,
                            graphOrdinals, typeSpecs, captures))
                        {
                            return false;
                        }
                        ValidateTypeSpecValueDependencies(typeSpecRef,
                            ownerModule, typeSpecs, typeDefs, enumDefs, genericParams,
                            imports, constantSources, frozenValueNodes, blobs,
                            features, modules, constantExecutionUses,
                            visitedValueTypeRows);
                        break;
                    case 10:
                        if ((nodeKind is 0x17 or 0x18
                                && controls.GetControlKind(checked((int)operandRef)) != 1)
                            || (nodeKind == 0x1F
                                && controls.GetControlKind(checked((int)operandRef)) != 3)
                            || (nodeKind == 0x36
                                && controls.GetControlKind(checked((int)operandRef)) != 2))
                        {
                            return false;
                        }
                        break;
                    case 11:
                        if (!ValidateTypePatternScope(
                            checked((int)operandRef), graph, method, phase,
                            graphMethods, graphPhases, graphOrdinals,
                            typePatterns, typeUses, typeSpecs,
                            captureDefinitions, captures, constantSources,
                            constantExecutionUses, ownerModule, typeDefs,
                            enumDefs, genericParams, imports, frozenValueNodes, blobs,
                            features, modules, visitedValueTypeRows))
                        {
                            return false;
                        }
                        break;
                }
            }
        }

        return true;
    }

    private static bool ValidateNodeRelation(int node, uint kind,
        int parent, uint childOrdinal, TrRmPredicateNodeTableReader nodes,
        TrRmPredicateChildEdgeTableReader edges)
    {
        var pattern = IsPatternKind(kind);
        if (pattern)
        {
            if (parent == 0)
            {
                return false;
            }

            var parentKind = nodes.GetNodeKind(parent);
            return (parentKind == 0x34 && childOrdinal == 1)
                || IsPatternParent(parentKind);
        }

        if (kind == 0x34)
        {
            if (parent != 0 && IsPatternKind(nodes.GetNodeKind(parent)))
            {
                return false;
            }

            var subject = edges.GetChildNodeRowByOrdinal(node, 0);
            var patternNode = edges.GetChildNodeRowByOrdinal(node, 1);
            return !IsPatternKind(nodes.GetNodeKind(subject))
                && IsPatternKind(nodes.GetNodeKind(patternNode));
        }

        if (kind == 0x2E)
        {
            return parent != 0 && nodes.GetNodeKind(parent) == 0x34
                && childOrdinal == 0;
        }

        if (parent == 0)
        {
            return true;
        }

        var ownerKind = nodes.GetNodeKind(parent);
        if (IsPatternKind(ownerKind))
        {
            return false;
        }

        return ownerKind != 0x34 || childOrdinal == 0;
    }

    private static bool IsPatternKind(uint kind)
        => kind is >= 0x40 and <= 0x4F;

    private static bool IsPatternParent(uint kind)
        => kind is 0x4A or 0x4B or 0x4C or 0x4D or 0x4E or 0x4F;

    private static bool ValidateNodeShape(int node, uint kind, int childCount,
        TrRmPredicateNodeTableReader nodes,
        TrRmPredicateNodeOperandTableReader operands,
        TrRmPredicateCallTableReader calls, int graph,
        TrRmPredicateOperationTableReader operations,
        TrRmPredicateTypeUseTableReader typeUses,
        TrRmPredicateControlTableReader controls,
        TrRmPredicateTypePatternTableReader typePatterns,
        TrRmPredicateCaptureTableReader captures)
    {
        var resultType = nodes.GetResultTypeSpecRef(node);
        if (kind is >= 0x40 and <= 0x4F
            && resultType != 0xF200_0001)
        {
            return false;
        }

        return kind switch
        {
            0x10 or 0x11 => childCount == 0
                && HasOperandKinds(node, nodes, operands, 1),
            0x12 => childCount == 0
                && HasOperandKinds(node, nodes, operands, 3),
            0x13 or 0x19 or 0x1A or 0x1B or 0x1D or 0x1E
                => childCount == 1 && HasNoOperands(node, nodes),
            0x14 => childCount == 0
                && HasOperandKinds(node, nodes, operands, 2),
            0x15 or 0x16 => childCount == 1
                && HasOperandKinds(node, nodes, operands, 7),
            0x17 or 0x18 => childCount == 1
                && HasOperandKinds(node, nodes, operands, 10)
                && ControlHasKind(node, nodes, operands, controls, 0, 1),
            0x1C => childCount == 2 && HasNoOperands(node, nodes),
            0x1F => childCount == 0
                && HasOperandKinds(node, nodes, operands, 10)
                && ControlHasKind(node, nodes, operands, controls, 0, 3),
            0x20 => childCount == 1
                && HasOperandKinds(node, nodes, operands, 4)
                && OperationHasCode(node, nodes, operands, operations,
                    0, 0x0001, 0x0002, 0x0003),
            0x21 => childCount == 2
                && HasOperandKinds(node, nodes, operands, 4)
                && OperationHasRange(node, nodes, operands, operations,
                    0, 0x0004, 0x0014),
            0x22 => childCount == 1
                && HasOperandKinds(node, nodes, operands, 4)
                && OperationHasRange(node, nodes, operands, operations,
                    0, 0x0015, 0x0016),
            0x23 => childCount == 1 && HasNoOperands(node, nodes),
            0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2A or 0x2B
                => childCount == 2 && HasNoOperands(node, nodes),
            0x29 => childCount == 3 && HasNoOperands(node, nodes),
            0x2C => HasOperandKinds(node, nodes, operands, 5)
                && CallChildrenMatch(node, 0, 1, childCount,
                    nodes, operands, calls, requireHiddenOutput: false),
            0x2D => HasOperandKinds(node, nodes, operands, 8, 5)
                && CallChildrenMatch(node, 1, 1, childCount,
                    nodes, operands, calls, requireHiddenOutput: false),
            0x2E => HasOperandKinds(node, nodes, operands, 5)
                && CallChildrenMatch(node, 0, 1, childCount,
                    nodes, operands, calls, requireHiddenOutput: true),
            0x2F => HasOperandKinds(node, nodes, operands, 5)
                && CallChildrenMatch(node, 0, 2, childCount,
                    nodes, operands, calls, requireHiddenOutput: false),
            0x30 => childCount >= 2 && HasNoOperands(node, nodes),
            0x31 => childCount == 0 && HasNoOperands(node, nodes),
            0x32 => childCount == 1 && HasNoOperands(node, nodes),
            0x33 => HasOperandKinds(node, nodes, operands, 9, 6),
            0x34 => childCount == 2 && HasNoOperands(node, nodes),
            0x35 => childCount == 0
                && HasOperandKinds(node, nodes, operands, 6, 6)
                && TypeUseOperandsValid(node, nodes, operands, typeUses),
            0x36 => childCount == 0
                && HasOperandKinds(node, nodes, operands, 6, 10)
                && TypeUseOperandsValid(node, nodes, operands, typeUses)
                && ControlHasKind(node, nodes, operands, controls, 1, 2),
            0x40 or 0x41 or 0x42 => childCount == 0 && HasNoOperands(node, nodes),
            0x43 => childCount == 0
                && HasOperandKinds(node, nodes, operands, 2),
            0x44 => childCount == 0
                && HasOperandKinds(node, nodes, operands, 6),
            0x45 => childCount == 0
                && HasOperandKinds(node, nodes, operands, 6, 3)
                && TypeUseOperandsValid(node, nodes, operands, typeUses)
                && CaptureHasRole(node, 1, 1, nodes, operands, captures, graph),
            0x46 or 0x47 => childCount == 0
                && HasOperandKinds(node, nodes, operands, 3)
                && CaptureHasRole(node, 0, 1, nodes, operands, captures, graph),
            0x48 => childCount == 0
                && HasReferencePatternOperands(node, nodes, operands, typePatterns)
                && (nodes.GetOperandCount(node) == 1
                    || CaptureHasRole(node, 1, 2, nodes, operands, captures, graph)),
            0x49 => childCount == 0
                && HasGenericPatternOperands(node, nodes, operands, typePatterns)
                && (nodes.GetOperandCount(node) == 1
                    || CaptureHasRole(node, 1, 1, nodes, operands, captures, graph)),
            0x4A => childCount >= 2 && HasNoOperands(node, nodes),
            0x4B => HasOperandKinds(node, nodes, operands, 9, 6),
            0x4C => childCount == 1 && HasNoOperands(node, nodes),
            0x4D or 0x4E => childCount == 2 && HasNoOperands(node, nodes),
            0x4F => childCount >= 2 && HasNoOperands(node, nodes),
            _ => false,
        };
    }

    private static bool HasNoOperands(int node, TrRmPredicateNodeTableReader nodes)
        => nodes.GetOperandCount(node) == 0;

    private static bool HasOperandKinds(int node,
        TrRmPredicateNodeTableReader nodes,
        TrRmPredicateNodeOperandTableReader operands,
        byte firstKind, byte secondKind = 0)
    {
        var expectedCount = secondKind == 0 ? 1u : 2u;
        if (nodes.GetOperandCount(node) != expectedCount)
        {
            return false;
        }

        var firstRow = nodes.GetOperandFirstRow(node);
        return operands.GetOperandKind(checked((int)firstRow)) == firstKind
            && (expectedCount == 1
                || operands.GetOperandKind(checked((int)(firstRow + 1))) == secondKind);
    }

    private static bool CaptureHasRole(int node, int operandOrdinal, byte role,
        TrRmPredicateNodeTableReader nodes,
        TrRmPredicateNodeOperandTableReader operands,
        TrRmPredicateCaptureTableReader captures, int graph)
    {
        var capture = GetOperandRef(node, operandOrdinal, nodes, operands);
        return captures.GetGraphRow(checked((int)capture)) == graph
            && captures.GetRole(checked((int)capture)) == role;
    }

    private static bool ControlHasKind(int node,
        TrRmPredicateNodeTableReader nodes,
        TrRmPredicateNodeOperandTableReader operands,
        TrRmPredicateControlTableReader controls,
        int operandOrdinal, ushort controlKind)
    {
        var controlRow = GetOperandRef(node, operandOrdinal, nodes, operands);
        return controls.GetControlKind(checked((int)controlRow)) == controlKind;
    }

    private static bool OperationHasCode(int node,
        TrRmPredicateNodeTableReader nodes,
        TrRmPredicateNodeOperandTableReader operands,
        TrRmPredicateOperationTableReader operations,
        int operandOrdinal, ushort first, ushort second, ushort third)
    {
        var operation = GetOperandRef(node, operandOrdinal, nodes, operands);
        var code = operations.GetOperationCode(checked((int)operation));
        return code == first || code == second || code == third;
    }

    private static bool OperationHasRange(int node,
        TrRmPredicateNodeTableReader nodes,
        TrRmPredicateNodeOperandTableReader operands,
        TrRmPredicateOperationTableReader operations,
        int operandOrdinal, ushort first, ushort last)
    {
        var operation = GetOperandRef(node, operandOrdinal, nodes, operands);
        var code = operations.GetOperationCode(checked((int)operation));
        return code >= first && code <= last;
    }

    private static bool TypeUseOperandsValid(int node,
        TrRmPredicateNodeTableReader nodes,
        TrRmPredicateNodeOperandTableReader operands,
        TrRmPredicateTypeUseTableReader typeUses)
    {
        var firstRow = nodes.GetOperandFirstRow(node);
        var count = nodes.GetOperandCount(node);
        for (var ordinal = 0u; ordinal < count; ordinal++)
        {
            var operandRow = checked((int)(firstRow + ordinal));
            if (operands.GetOperandKind(operandRow) == 6)
            {
                _ = typeUses.GetTypeSpecRef(
                    checked((int)operands.GetOperandRef(operandRow)));
            }
        }

        return true;
    }

    private static bool HasReferencePatternOperands(int node,
        TrRmPredicateNodeTableReader nodes,
        TrRmPredicateNodeOperandTableReader operands,
        TrRmPredicateTypePatternTableReader patterns)
    {
        var count = nodes.GetOperandCount(node);
        if (count is not (1 or 2)
            || operands.GetOperandKind(checked((int)nodes.GetOperandFirstRow(node))) != 11)
        {
            return false;
        }

        var first = GetOperandRef(node, 0, nodes, operands);
        return patterns.GetKind(checked((int)first)) is 9 or 10
            && (count == 1
                || operands.GetOperandKind(checked((int)(
                    nodes.GetOperandFirstRow(node) + 1))) == 3);
    }

    private static bool HasGenericPatternOperands(int node,
        TrRmPredicateNodeTableReader nodes,
        TrRmPredicateNodeOperandTableReader operands,
        TrRmPredicateTypePatternTableReader patterns)
    {
        var count = nodes.GetOperandCount(node);
        if (count is not (1 or 2)
            || operands.GetOperandKind(checked((int)nodes.GetOperandFirstRow(node))) != 11)
        {
            return false;
        }

        var first = GetOperandRef(node, 0, nodes, operands);
        return patterns.GetKind(checked((int)first)) == 7
            && (count == 1
                || operands.GetOperandKind(checked((int)(
                    nodes.GetOperandFirstRow(node) + 1))) == 3);
    }

    private static bool CallChildrenMatch(int node, int callOperandOrdinal,
        byte expectedCallKind, int childCount,
        TrRmPredicateNodeTableReader nodes,
        TrRmPredicateNodeOperandTableReader operands,
        TrRmPredicateCallTableReader calls, bool requireHiddenOutput)
    {
        var callRow = GetOperandRef(node, callOperandOrdinal, nodes, operands);
        if (calls.GetCallKind(checked((int)callRow)) != expectedCallKind)
        {
            return false;
        }

        var childOrdinals = new byte[childCount];
        var bindingCount = checked((int)calls.GetBindingCount(checked((int)callRow)));
        var childBindingCount = 0;
        var hasHiddenOutput = false;
        for (var index = 0; index < bindingCount; index++)
        {
            var binding = calls.GetBinding(checked((int)callRow), index);
            if (binding.BindingKind is 1 or 2)
            {
                if (binding.ChildOrdinal >= (uint)childCount
                    || childOrdinals[binding.ChildOrdinal] != 0)
                {
                    return false;
                }

                childOrdinals[binding.ChildOrdinal] = 1;
                childBindingCount++;
            }
            else if (binding.BindingKind == 3)
            {
                hasHiddenOutput = true;
            }
        }

        return childBindingCount == childCount
            && (!requireHiddenOutput || hasHiddenOutput);
    }

    private static uint GetOperandRef(int node, int ordinal,
        TrRmPredicateNodeTableReader nodes,
        TrRmPredicateNodeOperandTableReader operands)
        => operands.GetOperandRef(checked((int)(
            nodes.GetOperandFirstRow(node) + (uint)ordinal)));

    private static bool IsInRange(uint row, int first, int count)
        => count != 0 && row >= (uint)first
            && (ulong)row < (ulong)(uint)first + (uint)count;

    private static bool IsSubjectRootAllowed(uint nodeKind, byte rootKind)
        => nodeKind switch
        {
            0x10 => rootKind is 1 or 2 or 4 or 5 or 8,
            0x11 => rootKind is 1 or 2 or 5 or 6 or 8,
            _ => false,
        };

    private static bool ValidateTypePatternScope(int pattern, int graph,
        int method, byte phase, int[] graphMethods, byte[] graphPhases,
        uint[] graphOrdinals,
        TrRmPredicateTypePatternTableReader patterns,
        TrRmPredicateTypeUseTableReader typeUses,
        TrRmTypeSpecTableReader typeSpecs,
        byte[] captureDefinitions,
        TrRmPredicateCaptureTableReader captures,
        TrRmConstantSourceTableReader constantSources,
        List<TrRmConstantExecutionUse> constantExecutionUses,
        int ownerModule, TrRmTypeDefTableReader typeDefs,
        TrRmEnumDefTableReader enumDefs,
        TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs, TrRmFeatureTableReader features,
        TrRmModuleTableReader modules,
        HashSet<int>[] visitedValueTypeRows)
    {
        var visited = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(pattern);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current))
            {
                continue;
            }
            var kind = patterns.GetKind(current);
            if (kind == 8)
            {
                var sourceRow = checked((int)patterns.GetTargetRef(current));
                if (constantSources.GetKind(sourceRow) == 2)
                {
                    constantExecutionUses.Add(new TrRmConstantExecutionUse(
                        checked((int)constantSources.GetPayloadReference(sourceRow)),
                        ownerModule, 0));
                }
            }
            if (kind is 5 or 6)
            {
                var capture = checked((int)patterns.GetTargetRef(current));
                if (!RecordCaptureDefinition(
                        (uint)capture, graph, 3, captureDefinitions, captures))
                {
                    return false;
                }
            }
            if (kind is 1 or 7)
            {
                var typeUse = checked((int)patterns.GetTargetRef(current));
                var typeSpecRef = typeUses.GetTypeSpecRef(typeUse);
                if (!CapturedTypeIsVisible(typeSpecRef,
                        graph, method, phase, graphMethods, graphPhases,
                        graphOrdinals, typeSpecs, captures))
                {
                    return false;
                }

                ValidateTypeSpecValueDependencies(typeSpecRef, ownerModule,
                    typeSpecs, typeDefs, enumDefs, genericParams, imports, constantSources,
                    frozenValueNodes, blobs, features, modules,
                    constantExecutionUses, visitedValueTypeRows);
            }
            for (var ordinal = 0u; ordinal < patterns.GetArgumentCount(current); ordinal++)
            {
                pending.Push(checked((int)patterns.GetArgumentRef(
                    current, checked((int)ordinal))));
            }
        }

        return true;
    }

    private static void ValidateTypeSpecValueDependencies(uint typeSpecRef,
        int ownerModule, TrRmTypeSpecTableReader typeSpecs,
        TrRmTypeDefTableReader typeDefs,
        TrRmEnumDefTableReader enumDefs,
        TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs, TrRmFeatureTableReader features,
        TrRmModuleTableReader modules,
        List<TrRmConstantExecutionUse> constantExecutionUses,
        HashSet<int>[] visitedTypeSpecRows)
    {
        var visitedRows = visitedTypeSpecRows[ownerModule] ??= new HashSet<int>();
        TrRmConstantSourceValueValidator.ValidateConstructedTypeSpecValues(
            typeSpecRef, ownerModule, typeSpecs, typeDefs, enumDefs,
            genericParams, imports, constantSources, frozenValueNodes, blobs,
            features, modules, visitedRows, constantExecutionUses);
    }

    private static bool RecordCaptureDefinition(uint captureRef, int graph,
        byte role, byte[] captureDefinitions,
        TrRmPredicateCaptureTableReader captures)
    {
        var capture = checked((int)captureRef);
        if (capture <= 0 || capture >= captureDefinitions.Length
            || captures.GetGraphRow(capture) != graph
            || captures.GetRole(capture) != role
            || captureDefinitions[capture] != 0)
        {
            return false;
        }

        captureDefinitions[capture] = 1;
        return true;
    }

    private static bool CallTypesAreVisible(int call, int graph, int method,
        byte phase, int[] graphMethods, byte[] graphPhases, uint[] graphOrdinals,
        TrRmPredicateCallTableReader calls, TrRmTypeSpecTableReader typeSpecs,
        TrRmPredicateCaptureTableReader captures)
    {
        var ownerType = calls.GetOwnerTypeSpecRef(call);
        if (ownerType != 0 && !CapturedTypeIsVisible(ownerType, graph, method,
                phase, graphMethods, graphPhases, graphOrdinals, typeSpecs, captures))
        {
            return false;
        }

        for (var ordinal = 0u; ordinal < calls.GetGenericArgumentCount(call); ordinal++)
        {
            var argumentOrdinal = checked((int)ordinal);
            if (calls.GetGenericArgumentKind(call, argumentOrdinal) == 1
                && !CapturedTypeIsVisible(calls.GetGenericArgumentRef(call, argumentOrdinal),
                    graph, method, phase, graphMethods, graphPhases,
                    graphOrdinals, typeSpecs, captures))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsPredicateCallTargetCallable(int call,
        TrRmPredicateCallTableReader calls,
        TrRmPredicateReadDependencyTableReader dependencies,
        TrRmTypeSpecTableReader typeSpecs)
    {
        var requirementMethod = calls.GetRequirementMethodDefRef(call);
        if (IsNativeFunctionPointerAnchorMethodDef(requirementMethod,
                typeSpecs))
        {
            return false;
        }

        var dependency = checked((int)calls.GetDependencyRef(call));
        return dependencies.GetDependencyKind(dependency) != 1
            || !IsNativeFunctionPointerAnchorMethodDef(
                dependencies.GetDependencyRef(dependency), typeSpecs);
    }

    private static bool IsPredicateCallTargetCallable(int call,
        TrRmPredicateCallTableReader calls,
        TrRmPredicateReadDependencyTableReader dependencies,
        byte[] anchorMethods)
    {
        var requirementMethod = calls.GetRequirementMethodDefRef(call);
        if (IsNativeFunctionPointerAnchorMethodDef(
                requirementMethod, anchorMethods))
        {
            return false;
        }

        var dependency = checked((int)calls.GetDependencyRef(call));
        return dependencies.GetDependencyKind(dependency) != 1
            || !IsNativeFunctionPointerAnchorMethodDef(
                dependencies.GetDependencyRef(dependency), anchorMethods);
    }

    internal static bool IsNativeFunctionPointerAnchorMethodDef(
        uint methodDefRow, TrRmTypeSpecTableReader typeSpecs)
    {
        if (methodDefRow == 0)
        {
            return false;
        }

        for (var typeSpecRow = 1; typeSpecRow <= typeSpecs.Count; typeSpecRow++)
        {
            if (typeSpecs.TryGetNativeFunctionPointerAnchor(typeSpecRow,
                    out _, out var anchorMethodDefRow)
                && (uint)anchorMethodDefRow == methodDefRow)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNativeFunctionPointerAnchorMethodDef(
        uint methodDefRow, byte[] anchorMethods)
        => methodDefRow != 0 && methodDefRow < (uint)anchorMethods.Length
            && anchorMethods[checked((int)methodDefRow)] != 0;

    private static bool CapturedTypeIsVisible(uint typeSpecRef, int graph,
        int method, byte phase, int[] graphMethods, byte[] graphPhases,
        uint[] graphOrdinals, TrRmTypeSpecTableReader typeSpecs,
        TrRmPredicateCaptureTableReader captures)
    {
        var type = typeSpecs.GetTypeSpecRef(typeSpecRef, allowInitQualifier: true);
        if (type.Form != 0x10)
        {
            return true;
        }

        var capture = typeSpecs.GetPredicateCaptureReference(type.Row);
        if (captures.GetRole(capture) != 3)
        {
            return false;
        }
        var captureGraph = captures.GetGraphRow(capture);
        return captureGraph == graph
            || (graphMethods[captureGraph] == method
                && graphPhases[captureGraph] == phase
                && graphOrdinals[captureGraph] <= graphOrdinals[graph]);
    }

    private static bool IsEarlierSamePhaseCapture(uint captureRow, int method,
        byte phase, int currentGraph, uint[] graphOrdinals,
        int[] graphMethods, byte[] graphPhases,
        TrRmPredicateCaptureTableReader captures)
    {
        if (!TrRmPredicateTableValidation.IsRowReference(captureRow, captures.Count))
        {
            return false;
        }

        var earlierGraph = captures.GetGraphRow((int)captureRow);
        return earlierGraph != currentGraph
            && graphMethods[earlierGraph] == method
            && graphPhases[earlierGraph] == phase
            && graphOrdinals[earlierGraph] < graphOrdinals[currentGraph];
    }

    private static bool HasSupportedOwnerResult(uint nodeKind, uint resultTypeSpec,
        TrRmTypeSpecTableReader typeSpecs)
        => nodeKind switch
        {
            1 or 2 => typeSpecs.AreEquivalentTypeSpecRefs(resultTypeSpec, 0xF200_0001),
            3 or 4 => typeSpecs.AreEquivalentTypeSpecRefs(resultTypeSpec, 0xF200_0008)
                || typeSpecs.AreEquivalentTypeSpecRefs(resultTypeSpec, 0xF200_000D),
            5 or 6 => typeSpecs.AreEquivalentTypeSpecRefs(resultTypeSpec, 0xF200_0008),
            _ => false,
        };

    private static void ValidateOutputSelectors(byte[] selectorKinds,
        TrRmMethodDefTableReader methods, TrRmContractTableReader contracts)
    {
        for (var method = 1; method <= methods.Count; method++)
        {
            var outputs = contracts.GetOutputStateContracts(method);
            for (var first = 0; first < outputs.Length; first++)
            {
                var firstOutput = outputs[first];
                var firstKind = GetOutputSelectorKind(firstOutput);
                for (var second = first + 1; second < outputs.Length; second++)
                {
                    var secondOutput = outputs[second];
                    if (firstOutput.ParameterOrdinal != secondOutput.ParameterOrdinal)
                    {
                        continue;
                    }

                    var secondKind = GetOutputSelectorKind(secondOutput);
                    if (firstKind == secondKind
                        || (firstOutput.State != secondOutput.State
                            && SelectorsOverlap(firstKind, secondKind)))
                    {
                        throw InvalidGraph();
                    }
                }
            }
        }

        byte GetOutputSelectorKind(TrRmOutputStateContract output)
        {
            var graph = output.PredicateGraphRow;
            if (graph <= 0 || graph >= selectorKinds.Length
                || selectorKinds[graph] == 0)
            {
                throw InvalidGraph();
            }

            return selectorKinds[graph];
        }
    }

    private static bool SelectorsOverlap(byte left, byte right)
        => (left, right) switch
        {
            (3, 5) or (5, 3) or (3, 6) or (6, 3) or (4, 5) or (5, 4) => true,
            _ => false,
        };

    private static void RequireFeature(TrRmFeatureTableReader features,
        TrRmModuleTableReader modules, int module, uint featureId,
        TrRmArtifactSchema schema)
    {
        if (!TrRmFeatureManifestValidator.Requires(
            features, modules, module, featureId, schema))
        {
            throw InvalidGraph();
        }
    }

    private static InvalidDataException InvalidGraph()
        => new("Invalid TRRM general PredicateGraph table.");
}
