#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include "RootPinLoadedTypesV2.h"

#include <algorithm>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <cstdlib>
#include <limits>
#include <memory>
#include <malloc.h>
#include <new>
#include <numeric>
#include <stdexcept>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

static_assert(sizeof(void*) == 8);
static_assert(sizeof(RootPinTypeNodeV2) == 96);
static_assert(sizeof(RootPinTypeFieldV1) == 32);
static_assert(sizeof(RootPinTypeOriginV1) == 40);
static_assert(sizeof(RootPinTypeBatchV2) == 72);
static_assert(sizeof(RootPinMethodNodeV1) == 56);
static_assert(sizeof(RootPinMethodInputV1) == 24);
static_assert(sizeof(RootPinMethodBatchV1) == 72);
static_assert(sizeof(RootPinPlaceRequestV1) == 40);
static_assert(sizeof(RootPinStableValue16V1) == 16);
static_assert(sizeof(RootPinSupportSlotV1) == 24);
static_assert(sizeof(RootPinSupportFrameV1) == 32);
static_assert(sizeof(RootPinInputTransferV1) == 16);
static_assert(sizeof(RootPinCallTransferV1) == 40);
static_assert(sizeof(RootPinRuntimeFunctionTableV1) == 104);
static_assert(sizeof(RootPinRuntimeInfoV1) == 112);
static_assert(offsetof(RootPinRuntimeInfoV1, functionTable) == 8);
static_assert(offsetof(RootPinRuntimeFunctionTableV1, rootExact) == 8);
static_assert(offsetof(RootPinRuntimeFunctionTableV1, pinExact) == 16);
static_assert(offsetof(RootPinRuntimeFunctionTableV1, pinObject) == 24);
static_assert(offsetof(RootPinRuntimeFunctionTableV1, resolveExact) == 32);
static_assert(offsetof(RootPinRuntimeFunctionTableV1, retainSupport) == 40);
static_assert(offsetof(RootPinRuntimeFunctionTableV1, releaseSupport) == 48);
static_assert(offsetof(RootPinRuntimeFunctionTableV1, enterFrame) == 56);
static_assert(offsetof(RootPinRuntimeFunctionTableV1, exitFrame) == 64);
static_assert(offsetof(RootPinRuntimeFunctionTableV1, poll) == 72);
static_assert(offsetof(RootPinRuntimeFunctionTableV1, beginCall) == 80);
static_assert(offsetof(RootPinRuntimeFunctionTableV1, returnSupport) == 88);
static_assert(offsetof(RootPinRuntimeFunctionTableV1, finishCall) == 96);
static_assert(offsetof(RootPinSupportSlotV1, residence) == 0);
static_assert(offsetof(RootPinSupportSlotV1, completeType) == 8);
static_assert(offsetof(RootPinSupportSlotV1, live) == 16);
static_assert(offsetof(RootPinSupportSlotV1, ownsHold) == 20);
static_assert(offsetof(RootPinSupportFrameV1, previous) == 0);
static_assert(offsetof(RootPinSupportFrameV1, slotCount) == 8);
static_assert(offsetof(RootPinSupportFrameV1, flags) == 12);
static_assert(offsetof(RootPinSupportFrameV1, slots) == 16);
static_assert(offsetof(RootPinSupportFrameV1, entryMethod) == 24);
static_assert(offsetof(RootPinInputTransferV1, stagedSource) == 0);
static_assert(offsetof(RootPinInputTransferV1, formalOrdinal) == 8);
static_assert(offsetof(RootPinInputTransferV1, sourceRole) == 12);
static_assert(offsetof(RootPinInputTransferV1, flags) == 13);
static_assert(offsetof(RootPinInputTransferV1, reserved) == 14);
static_assert(offsetof(RootPinCallTransferV1, previous) == 0);
static_assert(offsetof(RootPinCallTransferV1, target) == 8);
static_assert(offsetof(RootPinCallTransferV1, prospectiveResult) == 16);
static_assert(offsetof(RootPinCallTransferV1, inputCount) == 24);
static_assert(offsetof(RootPinCallTransferV1, state) == 28);
static_assert(offsetof(RootPinCallTransferV1, inputs) == 32);
static_assert(offsetof(RootPinTypeNodeV2, role) == 8);
static_assert(offsetof(RootPinTypeNodeV2, shape) == 12);
static_assert(offsetof(RootPinTypeNodeV2, size) == 16);
static_assert(offsetof(RootPinTypeNodeV2, alignment) == 24);
static_assert(offsetof(RootPinTypeNodeV2, retirementKey) == 32);
static_assert(offsetof(RootPinTypeNodeV2, child) == 40);
static_assert(offsetof(RootPinTypeNodeV2, fieldFirst) == 44);
static_assert(offsetof(RootPinTypeNodeV2, fieldCount) == 48);
static_assert(offsetof(RootPinTypeNodeV2, originFirst) == 52);
static_assert(offsetof(RootPinTypeNodeV2, originCount) == 56);
static_assert(offsetof(RootPinTypeNodeV2, retirementOrigin) == 60);
static_assert(offsetof(RootPinTypeNodeV2, retireGcPayloadThunk) == 64);
static_assert(offsetof(RootPinTypeNodeV2, layoutKind) == 72);
static_assert(offsetof(RootPinTypeNodeV2, flags) == 76);
static_assert(offsetof(RootPinTypeNodeV2, elementCount) == 80);
static_assert(offsetof(RootPinTypeNodeV2, elementStride) == 88);
static_assert(offsetof(RootPinTypeFieldV1, fieldKey) == 0);
static_assert(offsetof(RootPinTypeFieldV1, offset) == 8);
static_assert(offsetof(RootPinTypeFieldV1, knownAlignment) == 16);
static_assert(offsetof(RootPinTypeFieldV1, valueNode) == 24);
static_assert(offsetof(RootPinTypeOriginV1, originKey) == 0);
static_assert(offsetof(RootPinTypeOriginV1, cookie) == 8);
static_assert(offsetof(RootPinTypeOriginV1, retainOrigin) == 16);
static_assert(offsetof(RootPinTypeOriginV1, releaseOrigin) == 24);
static_assert(offsetof(RootPinTypeOriginV1, kind) == 32);
static_assert(offsetof(RootPinTypeBatchV2, abiRevision) == 0);
static_assert(offsetof(RootPinTypeBatchV2, targetProfileId) == 8);
static_assert(offsetof(RootPinTypeBatchV2, nodeCount) == 16);
static_assert(offsetof(RootPinTypeBatchV2, nodes) == 32);
static_assert(offsetof(RootPinTypeBatchV2, fields) == 40);
static_assert(offsetof(RootPinTypeBatchV2, origins) == 48);
static_assert(offsetof(RootPinTypeBatchV2, originUses) == 56);
static_assert(offsetof(RootPinTypeBatchV2, flags) == 64);
static_assert(offsetof(RootPinMethodNodeV1, resultType) == 8);
static_assert(offsetof(RootPinMethodNodeV1, formalCount) == 16);
static_assert(offsetof(RootPinMethodNodeV1, inputFirst) == 24);
static_assert(offsetof(RootPinMethodNodeV1, originFirst) == 32);
static_assert(offsetof(RootPinMethodNodeV1, declarationOrigin) == 40);
static_assert(offsetof(RootPinMethodNodeV1, flags) == 48);
static_assert(offsetof(RootPinMethodNodeV1, reserved) == 52);
static_assert(offsetof(RootPinMethodInputV1, formalOrdinal) == 0);
static_assert(offsetof(RootPinMethodInputV1, entrySlot) == 4);
static_assert(offsetof(RootPinMethodInputV1, completeType) == 8);
static_assert(offsetof(RootPinMethodInputV1, sourceRole) == 16);
static_assert(offsetof(RootPinMethodInputV1, handoffKind) == 17);
static_assert(offsetof(RootPinMethodInputV1, reserved16) == 18);
static_assert(offsetof(RootPinMethodInputV1, reserved32) == 20);
static_assert(offsetof(RootPinMethodBatchV1, targetProfileId) == 8);
static_assert(offsetof(RootPinMethodBatchV1, methodCount) == 16);
static_assert(offsetof(RootPinMethodBatchV1, methods) == 32);
static_assert(offsetof(RootPinMethodBatchV1, inputs) == 40);
static_assert(offsetof(RootPinMethodBatchV1, origins) == 48);
static_assert(offsetof(RootPinMethodBatchV1, originUses) == 56);
static_assert(offsetof(RootPinPlaceRequestV1, exactT) == 24);
static_assert(offsetof(RootPinPlaceRequestV1, accessBits) == 32);
static_assert(offsetof(RootPinStableValue16V1, support) == 8);

namespace
{
    struct TypeKey
    {
        uint64_t value;
        uint32_t role;

        bool operator==(const TypeKey&) const = default;
    };

    struct TypeKeyHash
    {
        size_t operator()(const TypeKey& key) const noexcept
        {
            const auto mixed = key.value ^ (static_cast<uint64_t>(key.role) << 32);
            return std::hash<uint64_t>{}(mixed);
        }
    };

    struct Origin
    {
        uint64_t key;
        uint64_t cookie;
        RootPinOriginCallbackV1 retain;
        RootPinOriginCallbackV1 release;
        uint32_t kind;
    };

    struct Field
    {
        uint64_t key;
        uint64_t offset;
        uint64_t knownAlignment;
        RootPinLoadedTypeV1* valueType;
    };

    struct FieldExtent
    {
        uint64_t offset;
        uint64_t size;
        uint32_t index;
    };

    bool IsPowerOfTwo(uint64_t value)
    {
        return value != 0 && (value & (value - 1)) == 0;
    }

    bool HasValidAlignment(uint64_t size, uint64_t alignment)
    {
        return alignment == 0 ? size == 0 : IsPowerOfTwo(alignment);
    }

    bool ValidRange(uint32_t first, uint32_t count, uint32_t total)
    {
        if (count == 0)
        {
            return first == 0;
        }

        return first <= total && count <= total - first;
    }

    struct MemoryRange
    {
        uintptr_t first;
        uintptr_t last;
    };

    bool GetMemoryRange(const void* pointer, size_t byteCount, MemoryRange& range)
    {
        if (byteCount == 0)
        {
            range = { reinterpret_cast<uintptr_t>(pointer), reinterpret_cast<uintptr_t>(pointer) };
            return true;
        }

        if (pointer == nullptr)
        {
            return false;
        }

        const auto first = reinterpret_cast<uintptr_t>(pointer);
        if (byteCount > std::numeric_limits<uintptr_t>::max() - first)
        {
            return false;
        }

        range = { first, first + byteCount };
        return true;
    }

    bool Overlaps(const MemoryRange& left, const MemoryRange& right)
    {
        return left.first < right.last && right.first < left.last;
    }

    bool ValidArray(
        const void* pointer,
        uint32_t count,
        size_t elementSize,
        size_t alignment,
        MemoryRange& range)
    {
        if (count == 0)
        {
            return pointer == nullptr && GetMemoryRange(nullptr, 0, range);
        }

        if (pointer == nullptr || count > std::numeric_limits<size_t>::max() / elementSize)
        {
            return false;
        }

        return reinterpret_cast<uintptr_t>(pointer) % alignment == 0
            && GetMemoryRange(pointer, static_cast<size_t>(count) * elementSize, range);
    }

    bool RangesEqual(const std::vector<uint64_t>& left, const std::vector<uint64_t>& right)
    {
        return left == right;
    }

    uint64_t ChildKey(const RootPinTypeBatchV2& batch, uint32_t index)
    {
        return index == ROOTPIN_TYPE_NO_INDEX ? 0 : batch.nodes[index].typeKey;
    }

    uint32_t ChildRole(const RootPinTypeBatchV2& batch, uint32_t index)
    {
        return index == ROOTPIN_TYPE_NO_INDEX ? 0 : batch.nodes[index].role;
    }

    bool SameOrigin(const RootPinTypeOriginV1& left, const RootPinTypeOriginV1& right)
    {
        return left.originKey == right.originKey
            && left.cookie == right.cookie
            && left.retainOrigin == right.retainOrigin
            && left.releaseOrigin == right.releaseOrigin
            && left.kind == right.kind;
    }

    bool SameOrigin(const Origin& left, const Origin& right)
    {
        return left.key == right.key
            && left.cookie == right.cookie
            && left.retain == right.retain
            && left.release == right.release
            && left.kind == right.kind;
    }

    bool SameNodeInput(
        const RootPinTypeBatchV2& batch,
        uint32_t leftIndex,
        uint32_t rightIndex,
        const std::vector<std::vector<uint64_t>>& originKeys)
    {
        const auto& left = batch.nodes[leftIndex];
        const auto& right = batch.nodes[rightIndex];
        if (left.typeKey != right.typeKey || left.role != right.role
            || left.shape != right.shape || left.size != right.size
            || left.alignment != right.alignment
            || left.retirementKey != right.retirementKey
            || left.layoutKind != right.layoutKind || left.flags != right.flags
            || left.elementCount != right.elementCount
            || left.elementStride != right.elementStride
            || ChildKey(batch, left.child) != ChildKey(batch, right.child)
            || ChildRole(batch, left.child) != ChildRole(batch, right.child)
            || left.fieldCount != right.fieldCount
            || !RangesEqual(originKeys[leftIndex], originKeys[rightIndex]))
        {
            return false;
        }

        for (uint32_t ordinal = 0; ordinal < left.fieldCount; ordinal++)
        {
            const auto& leftField = batch.fields[left.fieldFirst + ordinal];
            const auto& rightField = batch.fields[right.fieldFirst + ordinal];
            if (leftField.fieldKey != rightField.fieldKey
                || leftField.offset != rightField.offset
                || leftField.knownAlignment != rightField.knownAlignment
                || ChildKey(batch, leftField.valueNode) != ChildKey(batch, rightField.valueNode)
                || ChildRole(batch, leftField.valueNode) != ChildRole(batch, rightField.valueNode))
            {
                return false;
            }
        }

        if (left.role == ROOTPIN_TYPE_ROLE_GC_PAYLOAD)
        {
            const auto& leftRetirement = batch.origins[left.retirementOrigin];
            const auto& rightRetirement = batch.origins[right.retirementOrigin];
            if (leftRetirement.originKey != rightRetirement.originKey
                || left.retireGcPayloadThunk != right.retireGcPayloadThunk)
            {
                return false;
            }
        }

        return true;
    }

    bool SameNodeLoaded(
        const RootPinTypeBatchV2& batch,
        uint32_t inputIndex,
        const std::vector<std::vector<uint64_t>>& originKeys,
        const RootPinLoadedTypeV1& loaded);

    bool ValidateNodeShapes(const RootPinTypeBatchV2& batch)
    {
        for (uint32_t index = 0; index < batch.nodeCount; index++)
        {
            const auto& node = batch.nodes[index];
            if (node.typeKey == 0
                || (node.role != ROOTPIN_TYPE_ROLE_VALUE
                    && node.role != ROOTPIN_TYPE_ROLE_GC_PAYLOAD)
                || node.shape < ROOTPIN_TYPE_SHAPE_LEAF
                || node.shape > ROOTPIN_TYPE_SHAPE_FIXED_ARRAY
                || !HasValidAlignment(node.size, node.alignment))
            {
                return false;
            }

            if (node.shape != ROOTPIN_TYPE_SHAPE_FIXED_ARRAY
                && (node.elementCount != 0 || node.elementStride != 0))
            {
                return false;
            }

            if ((node.role == ROOTPIN_TYPE_ROLE_VALUE
                    && (node.flags & ~ROOTPIN_TYPE_FLAG_COPY) != 0)
                || (node.role == ROOTPIN_TYPE_ROLE_GC_PAYLOAD && node.flags != 0))
            {
                return false;
            }

            const bool fieldsShape = node.shape == ROOTPIN_TYPE_SHAPE_FIELDS;
            if (fieldsShape)
            {
                if (node.layoutKind > ROOTPIN_TYPE_LAYOUT_TRANSPARENT)
                {
                    return false;
                }
            }
            else if (node.layoutKind != ROOTPIN_TYPE_LAYOUT_AUTO
                || node.fieldFirst != 0 || node.fieldCount != 0)
            {
                return false;
            }

            if ((node.shape == ROOTPIN_TYPE_SHAPE_LEAF
                    || node.shape == ROOTPIN_TYPE_SHAPE_FIELDS)
                && node.child != ROOTPIN_TYPE_NO_INDEX)
            {
                return false;
            }

            if (node.shape != ROOTPIN_TYPE_SHAPE_LEAF
                && node.shape != ROOTPIN_TYPE_SHAPE_FIELDS
                && node.child >= batch.nodeCount)
            {
                return false;
            }

            if (node.role == ROOTPIN_TYPE_ROLE_VALUE)
            {
                if (node.retirementKey != 0
                    || node.retireGcPayloadThunk != 0
                    || node.retirementOrigin != ROOTPIN_TYPE_NO_INDEX)
                {
                    return false;
                }
            }
            else if (node.retirementKey == 0
                || node.retireGcPayloadThunk == 0
                || node.retirementOrigin >= batch.originCount)
            {
                return false;
            }

            const RootPinTypeNodeV2* child = node.child == ROOTPIN_TYPE_NO_INDEX
                ? nullptr
                : &batch.nodes[node.child];
            switch (node.shape)
            {
                case ROOTPIN_TYPE_SHAPE_LEAF:
                case ROOTPIN_TYPE_SHAPE_FIELDS:
                    if (node.role == ROOTPIN_TYPE_ROLE_GC_PAYLOAD
                        && node.shape != ROOTPIN_TYPE_SHAPE_FIELDS)
                    {
                        return false;
                    }
                    break;
                case ROOTPIN_TYPE_SHAPE_OPTIONAL:
                    if (node.role != ROOTPIN_TYPE_ROLE_VALUE
                        || child->role != ROOTPIN_TYPE_ROLE_VALUE
                        || child->shape < ROOTPIN_TYPE_SHAPE_OBJECT_CELL
                        || child->shape > ROOTPIN_TYPE_SHAPE_STABLE_CLASS
                        || node.size != child->size
                        || node.alignment != child->alignment
                        || (node.flags & ROOTPIN_TYPE_FLAG_COPY)
                            != (child->flags & ROOTPIN_TYPE_FLAG_COPY))
                    {
                        return false;
                    }
                    break;
                case ROOTPIN_TYPE_SHAPE_OBJECT_CELL:
                    if (node.role != ROOTPIN_TYPE_ROLE_VALUE
                        || node.size != 8 || node.alignment != 8
                        || child->role != ROOTPIN_TYPE_ROLE_GC_PAYLOAD
                        || (child->shape != ROOTPIN_TYPE_SHAPE_FIELDS
                            && child->shape != ROOTPIN_TYPE_SHAPE_BOX_PAYLOAD))
                    {
                        return false;
                    }
                    break;
                case ROOTPIN_TYPE_SHAPE_ROOT_CELL:
                    if (node.role != ROOTPIN_TYPE_ROLE_VALUE
                        || node.size != 8 || node.alignment != 8
                        || child->role != ROOTPIN_TYPE_ROLE_VALUE)
                    {
                        return false;
                    }
                    break;
                case ROOTPIN_TYPE_SHAPE_STABLE_REF:
                    if (node.role != ROOTPIN_TYPE_ROLE_VALUE
                        || node.size != 16 || node.alignment != 8
                        || child->role != ROOTPIN_TYPE_ROLE_VALUE)
                    {
                        return false;
                    }
                    break;
                case ROOTPIN_TYPE_SHAPE_STABLE_CLASS:
                    if (node.role != ROOTPIN_TYPE_ROLE_VALUE
                        || node.size != 16 || node.alignment != 8
                        || child->role != ROOTPIN_TYPE_ROLE_GC_PAYLOAD
                        || child->shape != ROOTPIN_TYPE_SHAPE_FIELDS)
                    {
                        return false;
                    }
                    break;
                case ROOTPIN_TYPE_SHAPE_BOX_PAYLOAD:
                    if (node.role != ROOTPIN_TYPE_ROLE_GC_PAYLOAD
                        || node.size != child->size
                        || node.alignment != child->alignment
                        || child->role != ROOTPIN_TYPE_ROLE_VALUE)
                    {
                        return false;
                    }
                    break;
                case ROOTPIN_TYPE_SHAPE_FIXED_ARRAY:
                {
                    if (node.role != ROOTPIN_TYPE_ROLE_VALUE
                        || child->role != ROOTPIN_TYPE_ROLE_VALUE)
                    {
                        return false;
                    }

                    const auto arrayAlignment = child->alignment == 0 ? 1 : child->alignment;
                    const auto maximum = std::numeric_limits<uint64_t>::max();
                    const auto remainder = child->size % arrayAlignment;
                    const auto padding = remainder == 0 ? 0 : arrayAlignment - remainder;
                    if (child->size > maximum - padding)
                    {
                        return false;
                    }
                    const auto stride = child->size + padding;
                    if (node.elementStride != stride
                        || node.alignment != arrayAlignment
                        || (stride != 0 && node.elementCount > maximum / stride)
                        || node.size != node.elementCount * stride)
                    {
                        return false;
                    }
                    break;
                }
                default:
                    return false;
            }
        }

        return true;
    }

    bool ValidateStorageAcyclic(const RootPinTypeBatchV2& batch)
    {
        std::vector<size_t> indegree(batch.nodeCount);

        for (uint32_t parentIndex = 0; parentIndex < batch.nodeCount; parentIndex++)
        {
            const auto& node = batch.nodes[parentIndex];
            if (node.shape == ROOTPIN_TYPE_SHAPE_FIELDS)
            {
                for (uint32_t ordinal = 0; ordinal < node.fieldCount; ordinal++)
                {
                    const auto child = batch.fields[node.fieldFirst + ordinal].valueNode;
                    if (indegree[child] == std::numeric_limits<size_t>::max())
                    {
                        return false;
                    }
                    indegree[child]++;
                }
            }
            else if (node.shape == ROOTPIN_TYPE_SHAPE_OPTIONAL
                || node.shape == ROOTPIN_TYPE_SHAPE_BOX_PAYLOAD
                || node.shape == ROOTPIN_TYPE_SHAPE_FIXED_ARRAY)
            {
                indegree[node.child]++;
            }
        }

        std::vector<uint32_t> ready;
        ready.reserve(batch.nodeCount);
        for (uint32_t index = 0; index < batch.nodeCount; index++)
        {
            if (indegree[index] == 0)
            {
                ready.push_back(index);
            }
        }

        size_t visited = 0;
        for (size_t cursor = 0; cursor < ready.size(); cursor++)
        {
            const auto readyNodeIndex = ready[cursor];
            const auto& node = batch.nodes[readyNodeIndex];
            visited++;
            if (node.shape == ROOTPIN_TYPE_SHAPE_FIELDS)
            {
                for (uint32_t ordinal = 0; ordinal < node.fieldCount; ordinal++)
                {
                    const auto child = batch.fields[node.fieldFirst + ordinal].valueNode;
                    if (--indegree[child] == 0)
                    {
                        ready.push_back(child);
                    }
                }
            }
            else if ((node.shape == ROOTPIN_TYPE_SHAPE_OPTIONAL
                || node.shape == ROOTPIN_TYPE_SHAPE_BOX_PAYLOAD
                || node.shape == ROOTPIN_TYPE_SHAPE_FIXED_ARRAY)
                && --indegree[node.child] == 0)
            {
                ready.push_back(node.child);
            }
        }

        return visited == batch.nodeCount;
    }

    bool ValidateFields(
        const RootPinTypeBatchV2& batch,
        std::vector<uint8_t>& fieldOwnership,
        const std::vector<uint8_t>& containsSupport)
    {
        for (uint32_t index = 0; index < batch.nodeCount; index++)
        {
            const auto& node = batch.nodes[index];
            if (node.shape != ROOTPIN_TYPE_SHAPE_FIELDS)
            {
                continue;
            }

            std::vector<FieldExtent> extents;
            extents.reserve(node.fieldCount);
            for (uint32_t ordinal = 0; ordinal < node.fieldCount; ordinal++)
            {
                const auto fieldIndex = node.fieldFirst + ordinal;
                if (fieldOwnership[fieldIndex] != 0)
                {
                    return false;
                }
                fieldOwnership[fieldIndex] = 1;

                const auto& field = batch.fields[fieldIndex];
                if (field.valueNode >= batch.nodeCount)
                {
                    return false;
                }
                const auto& valueType = batch.nodes[field.valueNode];
                if (field.fieldKey == 0 || field.reserved != 0
                    || field.offset > node.size
                    || valueType.size > node.size - field.offset
                    || !HasValidAlignment(valueType.size, field.knownAlignment)
                    || (valueType.size != 0
                        && (field.knownAlignment > node.alignment
                            || field.offset % field.knownAlignment != 0)))
                {
                    return false;
                }

                for (uint32_t prior = 0; prior < ordinal; prior++)
                {
                    if (batch.fields[node.fieldFirst + prior].fieldKey == field.fieldKey)
                    {
                        return false;
                    }
                }

                extents.push_back({ field.offset, valueType.size, fieldIndex });
            }

            std::sort(extents.begin(), extents.end(), [](const auto& left, const auto& right) {
                if (left.offset != right.offset)
                {
                    return left.offset < right.offset;
                }
                return left.size < right.size;
            });

            for (size_t prior = 0; prior < extents.size(); prior++)
            {
                const auto& extent = extents[prior];
                if (extent.size == 0)
                {
                    continue;
                }

                for (size_t current = prior + 1; current < extents.size(); current++)
                {
                    const auto& next = extents[current];
                    if (next.offset >= extent.offset + extent.size)
                    {
                        break;
                    }
                    if (next.size != 0)
                    {
                        const bool rootlessCopyUnion =
                            node.role == ROOTPIN_TYPE_ROLE_VALUE
                            && node.layoutKind == ROOTPIN_TYPE_LAYOUT_UNION
                            && (node.flags & ROOTPIN_TYPE_FLAG_COPY) != 0
                            && containsSupport[batch.fields[next.index].valueNode] == 0
                            && containsSupport[batch.fields[extent.index].valueNode] == 0;
                        if (!rootlessCopyUnion)
                        {
                            return false;
                        }
                    }
                }
            }

            if (node.layoutKind == ROOTPIN_TYPE_LAYOUT_UNION)
            {
                if (node.role != ROOTPIN_TYPE_ROLE_VALUE
                    || (node.flags & ROOTPIN_TYPE_FLAG_COPY) == 0)
                {
                    return false;
                }
                for (uint32_t ordinal = 0; ordinal < node.fieldCount; ordinal++)
                {
                    const auto child = batch.fields[node.fieldFirst + ordinal].valueNode;
                    if (containsSupport[child] != 0
                        || (batch.nodes[child].flags & ROOTPIN_TYPE_FLAG_COPY) == 0)
                    {
                        return false;
                    }
                }
            }
        }

        return std::all_of(fieldOwnership.begin(), fieldOwnership.end(), [](uint8_t owned) {
            return owned == 1;
        });
    }

    bool ValidateGraph(
        const RootPinTypeBatchV2& batch,
        std::vector<std::vector<uint64_t>>& originKeys,
        std::unordered_map<uint64_t, Origin>& origins,
        std::vector<uint8_t>& containsSupport)
    {
        if (!ValidateNodeShapes(batch))
        {
            return false;
        }

        std::vector<uint8_t> originUseOwnership(batch.originUseCount);
        std::vector<uint8_t> originOwnership(batch.originCount);
        originKeys.resize(batch.nodeCount);

        for (uint32_t index = 0; index < batch.originCount; index++)
        {
            const auto& source = batch.origins[index];
            if (source.originKey == 0 || source.retainOrigin == nullptr
                || source.releaseOrigin == nullptr || source.kind < ROOTPIN_TYPE_ORIGIN_SEMANTIC_LAYOUT
                || source.kind > ROOTPIN_TYPE_ORIGIN_ALLOCATOR_RUNTIME
                || source.reserved != 0)
            {
                return false;
            }

            const Origin value = {
                source.originKey,
                source.cookie,
                source.retainOrigin,
                source.releaseOrigin,
                source.kind,
            };
            const auto [iterator, inserted] = origins.emplace(source.originKey, value);
            if (!inserted && (iterator->second.cookie != value.cookie
                || iterator->second.retain != value.retain
                || iterator->second.release != value.release
                || iterator->second.kind != value.kind))
            {
                return false;
            }
        }

        for (uint32_t index = 0; index < batch.nodeCount; index++)
        {
            const auto& node = batch.nodes[index];
            if (node.originCount == 0
                || !ValidRange(node.originFirst, node.originCount, batch.originUseCount)
                || (node.shape == ROOTPIN_TYPE_SHAPE_FIELDS
                    && !ValidRange(node.fieldFirst, node.fieldCount, batch.fieldCount)))
            {
                return false;
            }
            if (node.shape == ROOTPIN_TYPE_SHAPE_FIELDS)
            {
                for (uint32_t ordinal = 0; ordinal < node.fieldCount; ordinal++)
                {
                    if (batch.fields[node.fieldFirst + ordinal].valueNode >= batch.nodeCount)
                    {
                        return false;
                    }
                }
            }

            auto& keys = originKeys[index];
            keys.reserve(node.originCount);
            bool hasSemanticLayoutOrigin = false;
            bool hasAllocatorOrigin = false;
            for (uint32_t offset = 0; offset < node.originCount; offset++)
            {
                const auto useIndex = node.originFirst + offset;
                if (originUseOwnership[useIndex] != 0)
                {
                    return false;
                }
                originUseOwnership[useIndex] = 1;

                const auto originIndex = batch.originUses[useIndex];
                if (originIndex >= batch.originCount)
                {
                    return false;
                }
                originOwnership[originIndex] = 1;
                const auto& origin = batch.origins[originIndex];
                keys.push_back(origin.originKey);
                hasSemanticLayoutOrigin |= origin.kind == ROOTPIN_TYPE_ORIGIN_SEMANTIC_LAYOUT;
                hasAllocatorOrigin |= origin.kind == ROOTPIN_TYPE_ORIGIN_ALLOCATOR_RUNTIME;
            }
            std::sort(keys.begin(), keys.end());
            if (std::adjacent_find(keys.begin(), keys.end()) != keys.end()
                || !hasSemanticLayoutOrigin
                || (node.role == ROOTPIN_TYPE_ROLE_GC_PAYLOAD && !hasAllocatorOrigin))
            {
                return false;
            }

            if (node.role == ROOTPIN_TYPE_ROLE_GC_PAYLOAD)
            {
                const auto retirementKey = batch.origins[node.retirementOrigin].originKey;
                if (!std::binary_search(keys.begin(), keys.end(), retirementKey)
                    || batch.origins[node.retirementOrigin].kind != ROOTPIN_TYPE_ORIGIN_CODE_UNWIND)
                {
                    return false;
                }
            }
        }

        if (!std::all_of(originUseOwnership.begin(), originUseOwnership.end(), [](uint8_t owned) {
                return owned == 1;
            })
            || !std::all_of(originOwnership.begin(), originOwnership.end(), [](uint8_t owned) {
                return owned == 1;
            }))
        {
            return false;
        }

        containsSupport.assign(batch.nodeCount, 0);
        std::vector<std::vector<uint32_t>> parents(batch.nodeCount);
        std::vector<uint32_t> work;
        for (uint32_t index = 0; index < batch.nodeCount; index++)
        {
            const auto& node = batch.nodes[index];
            if (node.shape >= ROOTPIN_TYPE_SHAPE_OBJECT_CELL
                && node.shape <= ROOTPIN_TYPE_SHAPE_STABLE_CLASS)
            {
                containsSupport[index] = 1;
                work.push_back(index);
            }

            if (node.child != ROOTPIN_TYPE_NO_INDEX)
            {
                parents[node.child].push_back(index);
            }
            if (node.shape == ROOTPIN_TYPE_SHAPE_FIELDS)
            {
                for (uint32_t ordinal = 0; ordinal < node.fieldCount; ordinal++)
                {
                    parents[batch.fields[node.fieldFirst + ordinal].valueNode].push_back(index);
                }
            }
        }

        for (size_t cursor = 0; cursor < work.size(); cursor++)
        {
            for (const auto parent : parents[work[cursor]])
            {
                if (containsSupport[parent] == 0)
                {
                    containsSupport[parent] = 1;
                    work.push_back(parent);
                }
            }
        }

        std::vector<uint8_t> fieldOwnership(batch.fieldCount);
        if (!ValidateFields(batch, fieldOwnership, containsSupport))
        {
            return false;
        }

        std::unordered_map<TypeKey, uint32_t, TypeKeyHash> firstNode;
        for (uint32_t index = 0; index < batch.nodeCount; index++)
        {
            const TypeKey key = { batch.nodes[index].typeKey, batch.nodes[index].role };
            const auto [iterator, inserted] = firstNode.emplace(key, index);
            if (!inserted && !SameNodeInput(batch, index, iterator->second, originKeys))
            {
                return false;
            }
        }

        if (!ValidateStorageAcyclic(batch))
        {
            return false;
        }

        return true;
    }

}

struct RootPinLoadedTypeV1
{
    RootPinDomainV1 domain;
    TypeKey key;
    uint32_t shape;
    uint64_t size;
    uint64_t alignment;
    uint64_t retirementKey;
    uint64_t retirementThunk;
    uint64_t retirementOriginKey;
    uint32_t layoutKind;
    uint32_t flags;
    uint64_t elementCount;
    uint64_t elementStride;
    bool containsSupport;
    RootPinLoadedTypeV1* child;
    std::vector<Field> fields;
    std::vector<uint64_t> origins;
};

struct ValueWalkFrame
{
    RootPinLoadedTypeV1* type;
    const uint8_t* value;
    uint64_t cursor;
};

struct MethodInputSlot
{
    uint32_t formalOrdinal;
    uint32_t entrySlot;
    RootPinLoadedTypeV1* completeType;
    uint8_t sourceRole;
    uint8_t handoffKind;

    bool operator==(const MethodInputSlot&) const = default;
};

struct RootPinLoadedMethodImpl
{
    RootPinDomainV1 domain;
    uint64_t identity;
    RootPinLoadedTypeV1* resultType;
    uint32_t formalCount;
    uint32_t entrySlotCount;
    uint32_t flags;
    uint64_t declarationOrigin;
    uint64_t entryOrigin;
    std::vector<MethodInputSlot> inputs;
    std::vector<uint64_t> origins;
};

namespace
{
    bool SameLoadedMethod(
        const RootPinLoadedMethodImpl& left,
        const RootPinLoadedMethodImpl& right)
    {
        return left.domain == right.domain
            && left.identity == right.identity
            && left.resultType == right.resultType
            && left.formalCount == right.formalCount
            && left.entrySlotCount == right.entrySlotCount
            && left.flags == right.flags
            && left.declarationOrigin == right.declarationOrigin
            && left.entryOrigin == right.entryOrigin
            && left.inputs == right.inputs
            && left.origins == right.origins;
    }
}

struct RootPinObjectCellImpl
{
    RootPinDomainV1 domain;
    uint8_t* currentBase;
    bool currentBaseOwned;
    uint64_t payloadSize;
    uint64_t alignment;
    uint64_t generation;
    uint64_t rootEdges;
    uint64_t activePins;
    uint64_t traceEpoch;
    RootPinObjectCellV1 traceNext;
    bool published;
    bool live;
    RootPinLoadedTypeV1* payloadType;
};

struct RootPinRootCellImpl
{
    RootPinDomainV1 domain;
    RootPinObjectCellV1 object;
    RootPinLoadedTypeV1* exactType;
    uint64_t identity;
    uint64_t generation;
    uint64_t offset;
    uint64_t extent;
    uint64_t knownAlignment;
    uint64_t descriptorHolds;
    uint64_t supportHolds;
    uint64_t traceEpoch;
    size_t arenaChunkIndex;
    uint32_t accessBits;
    bool active;
};

struct RootPinPinCellImpl
{
    RootPinDomainV1 domain;
    RootPinObjectCellV1 object;
    RootPinRootCellV1 root;
    RootPinLoadedTypeV1* exactType;
    uint64_t identity;
    uint64_t stableAddress;
    uint64_t extent;
    uint64_t alignment;
    uint64_t descriptorHolds;
    uint64_t traceEpoch;
    size_t arenaChunkIndex;
    bool wholeObject;
    bool active;
};

struct RootPinMutatorRegistration
{
    RootPinMutatorRegistration* previous;
    RootPinMutatorRegistration* next;
    RootPinDomainV1 domain;
    RootPinSupportFrameV1* frameTop;
    DWORD threadId;
    bool registered;
    bool parked;
};

struct CellChunk
{
    uint8_t* base;
    size_t nextSlot;
    size_t activeCells;
    bool committed;
};

inline constexpr size_t RootPinCellChunkSize = 1024 * 1024;
inline constexpr size_t RootPinCellSlotSize = 128;
inline constexpr size_t RootPinCellsPerChunk = RootPinCellChunkSize / RootPinCellSlotSize;

static_assert(sizeof(RootPinRootCellImpl) <= RootPinCellSlotSize);
static_assert(sizeof(RootPinPinCellImpl) <= RootPinCellSlotSize);
static_assert(alignof(RootPinRootCellImpl) <= 16);
static_assert(alignof(RootPinPinCellImpl) <= 16);

struct RootPinDomainImpl
{
    SRWLOCK coordination = SRWLOCK_INIT;
    CONDITION_VARIABLE coordinationCondition = CONDITION_VARIABLE_INIT;
    SRWLOCK gate = SRWLOCK_INIT;
    RootPinMutatorRegistration* mutators = nullptr;
    size_t mutatorCount = 0;
    size_t parkedMutatorCount = 0;
    RootPinMutatorRegistration* collectionOwner = nullptr;
    bool collectionRequested = false;
    std::unordered_map<TypeKey, RootPinLoadedTypeV1*, TypeKeyHash> types;
    std::unordered_map<uint64_t, Origin> origins;
    std::vector<std::unique_ptr<RootPinLoadedTypeV1>> ownedTypes;
    std::unordered_set<RootPinObjectCellV1> objects;
    std::unordered_set<RootPinRootCellV1> roots;
    std::unordered_set<RootPinPinCellV1> pins;
    std::vector<std::unique_ptr<RootPinObjectCellImpl>> ownedObjects;
    std::unordered_map<uint64_t, RootPinLoadedMethodV1> methods;
    std::vector<std::unique_ptr<RootPinLoadedMethodImpl>> ownedMethods;
    std::vector<CellChunk> cellChunks;
    std::vector<ValueWalkFrame> supportWalkStack;
    uint64_t nextObjectGeneration = 1;
    uint64_t nextCellIdentity = 1;
    uint64_t traceEpoch = 0;

    ~RootPinDomainImpl()
    {
        for (const auto& object : ownedObjects)
        {
            if (object->currentBaseOwned)
            {
                _aligned_free(object->currentBase);
            }
        }
    }
};

struct RootPinThreadState
{
    RootPinSupportFrameV1* frameTop;
    RootPinCallTransferV1* callTop;
    RootPinMutatorRegistration mutator;
};

static thread_local RootPinThreadState s_threadState = {};

namespace
{
    extern RootPinDomainImpl s_domain;

    class ExclusiveLock
    {
    public:
        explicit ExclusiveLock(SRWLOCK* gate, bool coordinateDomainGate = true) noexcept
            : _gate(gate),
              _coordination(coordinateDomainGate && gate == &s_domain.gate
                  ? &s_domain.coordination : nullptr)
        {
            if (_coordination != nullptr)
            {
                AcquireSRWLockExclusive(_coordination);
            }
            AcquireSRWLockExclusive(_gate);
        }

        ~ExclusiveLock()
        {
            ReleaseSRWLockExclusive(_gate);
            if (_coordination != nullptr)
            {
                ReleaseSRWLockExclusive(_coordination);
            }
        }

        ExclusiveLock(const ExclusiveLock&) = delete;
        ExclusiveLock& operator=(const ExclusiveLock&) = delete;

    private:
        SRWLOCK* _gate;
        SRWLOCK* _coordination;
    };

    [[noreturn]] void ContractViolation() noexcept;

    void WaitForCollectionLocked(RootPinDomainV1 domain)
    {
        auto* mutator = &s_threadState.mutator;
        if (mutator->registered && mutator->domain != domain)
        {
            ContractViolation();
        }

        while (domain->collectionRequested)
        {
            if (domain->collectionOwner == mutator)
            {
                ContractViolation();
            }

            if (mutator->registered && !mutator->parked)
            {
                if (domain->parkedMutatorCount == std::numeric_limits<size_t>::max())
                {
                    ContractViolation();
                }
                mutator->parked = true;
                domain->parkedMutatorCount++;
                WakeAllConditionVariable(&domain->coordinationCondition);
            }

            if (!SleepConditionVariableSRW(&domain->coordinationCondition,
                    &domain->coordination, INFINITE, 0))
            {
                ContractViolation();
            }
        }

        if (mutator->parked)
        {
            if (domain->parkedMutatorCount == 0)
            {
                ContractViolation();
            }
            mutator->parked = false;
            domain->parkedMutatorCount--;
            WakeAllConditionVariable(&domain->coordinationCondition);
        }
    }

    void RegisterMutatorLocked(RootPinDomainV1 domain)
    {
        auto* mutator = &s_threadState.mutator;
        if (mutator->registered || mutator->parked || domain->collectionRequested
            || mutator->frameTop != nullptr)
        {
            ContractViolation();
        }
        if (domain->mutatorCount == std::numeric_limits<size_t>::max())
        {
            ContractViolation();
        }

        mutator->domain = domain;
        mutator->threadId = GetCurrentThreadId();
        mutator->previous = nullptr;
        mutator->next = domain->mutators;
        mutator->registered = true;
        if (mutator->next != nullptr)
        {
            mutator->next->previous = mutator;
        }
        domain->mutators = mutator;
        domain->mutatorCount++;
    }

    void UnregisterMutatorLocked(RootPinDomainV1 domain)
    {
        auto* mutator = &s_threadState.mutator;
        if (!mutator->registered || mutator->domain != domain || mutator->parked
            || mutator->frameTop != nullptr || mutator->threadId != GetCurrentThreadId()
            || domain->mutatorCount == 0)
        {
            ContractViolation();
        }

        if (mutator->previous == nullptr)
        {
            if (domain->mutators != mutator)
            {
                ContractViolation();
            }
            domain->mutators = mutator->next;
        }
        else
        {
            mutator->previous->next = mutator->next;
        }
        if (mutator->next != nullptr)
        {
            mutator->next->previous = mutator->previous;
        }
        domain->mutatorCount--;
        *mutator = {};
        WakeAllConditionVariable(&domain->coordinationCondition);
    }

    class StopTheWorld
    {
    public:
        explicit StopTheWorld(RootPinDomainV1 domain)
            : _domain(domain), _coordination(&domain->coordination)
        {
            WaitForCollectionLocked(domain);
            auto* current = &s_threadState.mutator;
            _domain->collectionOwner = current->registered ? current : nullptr;
            _domain->collectionRequested = true;
            _active = true;
            WakeAllConditionVariable(&_domain->coordinationCondition);

            const size_t ownerCount = _domain->collectionOwner == nullptr ? 0 : 1;
            if (ownerCount > _domain->mutatorCount)
            {
                ContractViolation();
            }
            while (_domain->parkedMutatorCount
                < _domain->mutatorCount - ownerCount)
            {
                if (!SleepConditionVariableSRW(&_domain->coordinationCondition,
                        &_domain->coordination, INFINITE, 0))
                {
                    ContractViolation();
                }
            }
        }

        ~StopTheWorld()
        {
            if (_active)
            {
                _domain->collectionRequested = false;
                _domain->collectionOwner = nullptr;
                WakeAllConditionVariable(&_domain->coordinationCondition);
            }
        }

        StopTheWorld(const StopTheWorld&) = delete;
        StopTheWorld& operator=(const StopTheWorld&) = delete;

    private:
        RootPinDomainV1 _domain;
        ExclusiveLock _coordination;
        bool _active = false;
    };

    template<typename T>
    T* AllocateCellLocked(RootPinDomainV1 domain, size_t& chunkIndex)
    {
        static_assert(sizeof(T) <= RootPinCellSlotSize);
        static_assert(alignof(T) <= 16);

        if (domain->cellChunks.empty()
            || domain->cellChunks.back().nextSlot == RootPinCellsPerChunk)
        {
            if (domain->cellChunks.size() == std::numeric_limits<size_t>::max())
            {
                return nullptr;
            }
            domain->cellChunks.reserve(domain->cellChunks.size() + 1);
            auto* base = static_cast<uint8_t*>(VirtualAlloc(nullptr, RootPinCellChunkSize,
                MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
            if (base == nullptr)
            {
                return nullptr;
            }
            domain->cellChunks.push_back({ base, 0, 0, true });
        }

        chunkIndex = domain->cellChunks.size() - 1;
        auto& chunk = domain->cellChunks[chunkIndex];
        if (!chunk.committed)
        {
            if (VirtualAlloc(chunk.base, RootPinCellChunkSize, MEM_COMMIT, PAGE_READWRITE)
                != chunk.base)
            {
                return nullptr;
            }
            chunk.committed = true;
        }

        if (chunk.nextSlot >= RootPinCellsPerChunk)
        {
            ContractViolation();
        }

        const auto slotOffset = chunk.nextSlot * RootPinCellSlotSize;
        auto* slot = chunk.base + slotOffset;
        chunk.nextSlot++;
        chunk.activeCells++;
        return ::new (slot) T{};
    }

    template<typename T>
    void ReleaseCellLocked(RootPinDomainV1 domain, T* cell, size_t chunkIndex)
    {
        if (cell == nullptr || chunkIndex >= domain->cellChunks.size())
        {
            ContractViolation();
        }

        auto& chunk = domain->cellChunks[chunkIndex];
        const auto first = reinterpret_cast<uintptr_t>(chunk.base);
        const auto address = reinterpret_cast<uintptr_t>(cell);
        if (!chunk.committed || chunk.activeCells == 0
            || address < first || address - first >= RootPinCellChunkSize
            || (address - first) % RootPinCellSlotSize != 0)
        {
            ContractViolation();
        }

        cell->~T();
        chunk.activeCells--;
        if (chunk.activeCells == 0)
        {
            if (!VirtualFree(chunk.base, RootPinCellChunkSize, MEM_DECOMMIT))
            {
                ContractViolation();
            }
            chunk.committed = false;
        }
    }

    RootPinDomainImpl s_domain;
#if defined(TERRUM_ROOT_PIN_TESTS)
    std::atomic<uint32_t> s_failureStage;
    std::atomic<uint32_t> s_relocationFailureOrdinal;

    void MaybeFailAtStage(uint32_t stage)
    {
        auto expected = stage;
        if (s_failureStage.compare_exchange_strong(expected, 0))
        {
            throw std::bad_alloc();
        }
    }
#else
    void MaybeFailAtStage(uint32_t)
    {
    }
#endif

    bool SameNodeLoaded(
        const RootPinTypeBatchV2& batch,
        uint32_t inputIndex,
        const std::vector<std::vector<uint64_t>>& originKeys,
        const RootPinLoadedTypeV1& loaded)
    {
        const auto& input = batch.nodes[inputIndex];
        if (input.typeKey != loaded.key.value || input.role != loaded.key.role
            || input.shape != loaded.shape || input.size != loaded.size
            || input.alignment != loaded.alignment
            || input.retirementKey != loaded.retirementKey
            || input.layoutKind != loaded.layoutKind || input.flags != loaded.flags
            || input.elementCount != loaded.elementCount
            || input.elementStride != loaded.elementStride
            || ChildKey(batch, input.child) != (loaded.child == nullptr ? 0 : loaded.child->key.value)
            || ChildRole(batch, input.child) != (loaded.child == nullptr ? 0 : loaded.child->key.role)
            || input.fieldCount != loaded.fields.size()
            || !RangesEqual(originKeys[inputIndex], loaded.origins))
        {
            return false;
        }

        if (input.role == ROOTPIN_TYPE_ROLE_GC_PAYLOAD
            && (batch.origins[input.retirementOrigin].originKey != loaded.retirementOriginKey
                || input.retireGcPayloadThunk != loaded.retirementThunk))
        {
            return false;
        }

        for (uint32_t ordinal = 0; ordinal < input.fieldCount; ordinal++)
        {
            const auto& inputField = batch.fields[input.fieldFirst + ordinal];
            const auto& loadedField = loaded.fields[ordinal];
            if (inputField.fieldKey != loadedField.key
                || inputField.offset != loadedField.offset
                || inputField.knownAlignment != loadedField.knownAlignment
                || ChildKey(batch, inputField.valueNode) != loadedField.valueType->key.value
                || ChildRole(batch, inputField.valueNode) != loadedField.valueType->key.role)
            {
                return false;
            }
        }

        return true;
    }

    std::unique_ptr<RootPinLoadedTypeV1> CreateLoadedType(
        RootPinDomainV1 domain,
        const RootPinTypeBatchV2& batch,
        uint32_t index,
        const std::vector<std::vector<uint64_t>>& originKeys,
        const std::vector<uint8_t>& containsSupport)
    {
        const auto& node = batch.nodes[index];
        auto loaded = std::make_unique<RootPinLoadedTypeV1>();
        loaded->domain = domain;
        loaded->key = { node.typeKey, node.role };
        loaded->shape = node.shape;
        loaded->size = node.size;
        loaded->alignment = node.alignment;
        loaded->retirementKey = node.retirementKey;
        loaded->retirementThunk = node.retireGcPayloadThunk;
        loaded->retirementOriginKey = node.role == ROOTPIN_TYPE_ROLE_GC_PAYLOAD
            ? batch.origins[node.retirementOrigin].originKey
            : 0;
        loaded->layoutKind = node.layoutKind;
        loaded->flags = node.flags;
        loaded->elementCount = node.elementCount;
        loaded->elementStride = node.elementStride;
        loaded->containsSupport = containsSupport[index] != 0;
        loaded->child = nullptr;
        loaded->origins = originKeys[index];
        loaded->fields.reserve(node.fieldCount);
        return loaded;
    }

    [[noreturn]] void ContractViolation() noexcept
    {
        TerminateProcess(GetCurrentProcess(), 0xC0000409);
        std::abort();
    }

    bool CheckedPointerAddress(uint8_t* base, uint64_t offset, uint64_t& address)
    {
        const auto first = reinterpret_cast<uintptr_t>(base);
        if (offset > std::numeric_limits<uintptr_t>::max() - first)
        {
            return false;
        }

        address = static_cast<uint64_t>(first + static_cast<uintptr_t>(offset));
        return true;
    }

    uint64_t AlignmentAfterOffset(uint64_t alignment, uint64_t offset)
    {
        return alignment == 0 ? 0 : std::gcd(alignment, offset);
    }

    struct PlaceCandidate
    {
        RootPinLoadedTypeV1* type;
        uint64_t offset;
        uint64_t alignment;
    };

    bool FindExactPlace(
        RootPinLoadedTypeV1* payloadType,
        uint64_t payloadSize,
        uint64_t payloadAlignment,
        uint64_t targetOffset,
        uint64_t extent,
        uint64_t requestedAlignment,
        RootPinLoadedTypeV1* exactType,
        uint64_t& actualAlignment)
    {
        if (payloadType == nullptr || payloadType->size != payloadSize
            || targetOffset > payloadSize || extent > payloadSize - targetOffset
            || !IsPowerOfTwo(requestedAlignment))
        {
            return false;
        }

        std::vector<PlaceCandidate> candidates;
        candidates.push_back({ payloadType, 0, payloadAlignment });

        for (size_t cursor = 0; cursor < candidates.size(); cursor++)
        {
            const auto candidate = candidates[cursor];
            const auto* type = candidate.type;
            if (type == exactType && candidate.offset == targetOffset
                && type->size == extent && candidate.alignment >= requestedAlignment
                && candidate.alignment % requestedAlignment == 0)
            {
                actualAlignment = candidate.alignment;
                return true;
            }

            if (type->shape == ROOTPIN_TYPE_SHAPE_FIELDS)
            {
                for (const auto& field : type->fields)
                {
                    if (field.valueType->size == 0
                        || field.offset > std::numeric_limits<uint64_t>::max() - candidate.offset)
                    {
                        continue;
                    }
                    const auto fieldOffset = candidate.offset + field.offset;
                    if (targetOffset < fieldOffset
                        || targetOffset - fieldOffset >= field.valueType->size)
                    {
                        continue;
                    }

                    const auto placementAlignment =
                        AlignmentAfterOffset(candidate.alignment, field.offset);
                    const auto fieldAlignment =
                        std::min(placementAlignment, field.knownAlignment);
                    candidates.push_back({ field.valueType, fieldOffset, fieldAlignment });
                }
            }
            else if (type->shape == ROOTPIN_TYPE_SHAPE_FIXED_ARRAY)
            {
                if (type->elementCount == 0 || type->elementStride == 0
                    || targetOffset < candidate.offset
                    || targetOffset - candidate.offset >= type->size)
                {
                    continue;
                }

                const auto relativeOffset = targetOffset - candidate.offset;
                const auto elementIndex = relativeOffset / type->elementStride;
                const auto elementOffset = relativeOffset % type->elementStride;
                if (elementIndex >= type->elementCount
                    || elementOffset >= type->child->size
                    || elementIndex > std::numeric_limits<uint64_t>::max() / type->elementStride)
                {
                    continue;
                }

                const auto displacement = elementIndex * type->elementStride;
                if (displacement > std::numeric_limits<uint64_t>::max() - candidate.offset)
                {
                    continue;
                }
                const auto elementBase = candidate.offset + displacement;
                candidates.push_back({
                    type->child,
                    elementBase,
                    AlignmentAfterOffset(candidate.alignment, displacement),
                });
            }
            else if (type->shape == ROOTPIN_TYPE_SHAPE_BOX_PAYLOAD
                && type->child->size != 0
                && targetOffset >= candidate.offset
                && targetOffset - candidate.offset < type->child->size)
            {
                candidates.push_back({
                    type->child,
                    candidate.offset,
                    std::min(candidate.alignment, type->child->alignment),
                });
            }
        }

        return false;
    }

    bool IsObjectRegistered(RootPinDomainV1 domain, RootPinObjectCellV1 object)
    {
        return domain != nullptr && object != nullptr && domain->objects.contains(object);
    }

    bool IsRootRegistered(RootPinDomainV1 domain, RootPinRootCellV1 root)
    {
        return domain != nullptr && root != nullptr && domain->roots.contains(root);
    }

    bool IsPinRegistered(RootPinDomainV1 domain, RootPinPinCellV1 pin)
    {
        return domain != nullptr && pin != nullptr && domain->pins.contains(pin);
    }

    bool IsLoadedTypeRegistered(
        RootPinDomainV1 domain,
        RootPinLoadedTypeV1* type,
        uint32_t role)
    {
        if (domain == nullptr || type == nullptr)
        {
            return false;
        }

        const auto owned = std::find_if(domain->ownedTypes.begin(), domain->ownedTypes.end(),
            [type](const auto& candidate) {
                return candidate.get() == type;
            });
        if (owned == domain->ownedTypes.end()
            || (*owned)->domain != domain || (*owned)->key.role != role)
        {
            return false;
        }

        const auto found = domain->types.find((*owned)->key);
        return found != domain->types.end() && found->second == type;
    }

    bool ValidateRootRequest(
        RootPinDomainV1 domain,
        RootPinObjectCellV1 object,
        const RootPinPlaceRequestV1& request,
        uint64_t& actualAlignment)
    {
        if (!IsObjectRegistered(domain, object)
            || object->domain != domain || !object->published || !object->live
            || object->payloadType == nullptr
            || object->payloadType->domain != domain
            || object->payloadType->key.role != ROOTPIN_TYPE_ROLE_GC_PAYLOAD
            || (object->payloadType->shape != ROOTPIN_TYPE_SHAPE_FIELDS
                && object->payloadType->shape != ROOTPIN_TYPE_SHAPE_BOX_PAYLOAD)
            || object->payloadSize != object->payloadType->size
            || object->alignment != object->payloadType->alignment
            || object->currentBase == nullptr
            || request.reserved != 0
            || (request.accessBits != ROOTPIN_ACCESS_READ
                && request.accessBits != ROOTPIN_ACCESS_READ_WRITE)
            || request.extent == 0
            || request.offset > object->payloadSize
            || request.extent > object->payloadSize - request.offset
            || !IsPowerOfTwo(request.knownAlignment)
            || request.exactT == nullptr
            || !IsLoadedTypeRegistered(domain, request.exactT, ROOTPIN_TYPE_ROLE_VALUE)
            || request.extent != request.exactT->size)
        {
            return false;
        }

        uint64_t address;
        if (!CheckedPointerAddress(object->currentBase, request.offset, address))
        {
            return false;
        }

        return FindExactPlace(object->payloadType, object->payloadSize, object->alignment,
            request.offset, request.extent, request.knownAlignment, request.exactT,
            actualAlignment);
    }

    bool CanPinObject(RootPinDomainV1 domain, RootPinObjectCellV1 object)
    {
        return IsObjectRegistered(domain, object)
            && object->domain == domain && object->published && object->live
            && object->currentBase != nullptr
            && object->payloadType != nullptr
            && object->payloadType->domain == domain
            && object->payloadType->key.role == ROOTPIN_TYPE_ROLE_GC_PAYLOAD
            && object->payloadType->shape == ROOTPIN_TYPE_SHAPE_FIELDS;
    }

    void RetireRootIfUnheld(RootPinRootCellV1 root)
    {
        if (root->descriptorHolds == 0 && root->supportHolds == 0)
        {
            auto* domain = root->domain;
            if (!root->active || root->object->rootEdges == 0
                || domain->roots.erase(root) != 1)
            {
                ContractViolation();
            }
            root->active = false;
            root->object->rootEdges--;
            ReleaseCellLocked(domain, root, root->arenaChunkIndex);
        }
    }

    uint32_t RootExactEntry(
        RootPinDomainV1 domain,
        RootPinObjectCellV1 object,
        const RootPinPlaceRequestV1* request,
        RootPinRootCellV1* vacantOut)
    {
        if (domain != &s_domain || object == nullptr || request == nullptr || vacantOut == nullptr
            || reinterpret_cast<uintptr_t>(request) % alignof(RootPinPlaceRequestV1) != 0
            || reinterpret_cast<uintptr_t>(vacantOut) % alignof(RootPinRootCellV1) != 0
            || *vacantOut != nullptr)
        {
            ContractViolation();
        }

        RootPinRootCellV1 candidate = nullptr;
        size_t chunkIndex = 0;
        bool published = false;
        try
        {
            ExclusiveLock lock(&domain->gate);
            uint64_t actualAlignment;
            if (!ValidateRootRequest(domain, object, *request, actualAlignment)
                || object->rootEdges == std::numeric_limits<uint64_t>::max())
            {
                ContractViolation();
            }
            if (domain->nextCellIdentity == std::numeric_limits<uint64_t>::max())
            {
                return ROOTPIN_TYPE_STATUS_EXHAUSTED;
            }

            if (domain->roots.size() == std::numeric_limits<size_t>::max()
                || domain->nextCellIdentity == std::numeric_limits<uint64_t>::max())
            {
                return ROOTPIN_TYPE_STATUS_EXHAUSTED;
            }
            domain->roots.reserve(domain->roots.size() + 1);
            candidate = AllocateCellLocked<RootPinRootCellImpl>(domain, chunkIndex);
            if (candidate == nullptr)
            {
                return ROOTPIN_TYPE_STATUS_EXHAUSTED;
            }

            candidate->arenaChunkIndex = chunkIndex;
            MaybeFailAtStage(4);
            candidate->domain = domain;
            candidate->object = object;
            candidate->exactType = request->exactT;
            candidate->identity = domain->nextCellIdentity;
            candidate->generation = object->generation;
            candidate->offset = request->offset;
            candidate->extent = request->extent;
            candidate->knownAlignment = actualAlignment;
            candidate->descriptorHolds = 1;
            candidate->supportHolds = 0;
            candidate->accessBits = request->accessBits;
            candidate->active = true;
            if (!domain->roots.insert(candidate).second)
            {
                ContractViolation();
            }
            published = true;
            domain->nextCellIdentity++;
            object->rootEdges++;
            *vacantOut = candidate;
            return ROOTPIN_TYPE_STATUS_SUCCESS;
        }
        catch (const std::bad_alloc&)
        {
            if (candidate != nullptr && !published)
            {
                ExclusiveLock lock(&domain->gate);
                ReleaseCellLocked(domain, candidate, chunkIndex);
            }
            return ROOTPIN_TYPE_STATUS_EXHAUSTED;
        }
        catch (const std::length_error&)
        {
            if (candidate != nullptr && !published)
            {
                ExclusiveLock lock(&domain->gate);
                ReleaseCellLocked(domain, candidate, chunkIndex);
            }
            return ROOTPIN_TYPE_STATUS_EXHAUSTED;
        }
    }

    uint32_t PinExactEntry(
        RootPinDomainV1 domain,
        RootPinRootCellV1 root,
        RootPinStableValue16V1* vacantOut)
    {
        if (domain != &s_domain || root == nullptr || vacantOut == nullptr
            || reinterpret_cast<uintptr_t>(vacantOut) % alignof(RootPinStableValue16V1) != 0
            || vacantOut->address != 0 || vacantOut->support != nullptr)
        {
            ContractViolation();
        }

        RootPinPinCellV1 candidate = nullptr;
        size_t chunkIndex = 0;
        bool published = false;
        try
        {
            ExclusiveLock lock(&domain->gate);
            if (!IsRootRegistered(domain, root) || root->domain != domain
                || !root->active || root->descriptorHolds == 0
                || !root->object->live || root->object->generation != root->generation
                || root->supportHolds == std::numeric_limits<uint64_t>::max()
                || root->object->activePins == std::numeric_limits<uint64_t>::max())
            {
                ContractViolation();
            }
            if (domain->nextCellIdentity == std::numeric_limits<uint64_t>::max())
            {
                return ROOTPIN_TYPE_STATUS_EXHAUSTED;
            }
            uint64_t stableAddress;
            if (!CheckedPointerAddress(root->object->currentBase, root->offset, stableAddress))
            {
                ContractViolation();
            }

            if (domain->pins.size() == std::numeric_limits<size_t>::max())
            {
                return ROOTPIN_TYPE_STATUS_EXHAUSTED;
            }
            domain->pins.reserve(domain->pins.size() + 1);
            candidate = AllocateCellLocked<RootPinPinCellImpl>(domain, chunkIndex);
            if (candidate == nullptr)
            {
                return ROOTPIN_TYPE_STATUS_EXHAUSTED;
            }

            candidate->arenaChunkIndex = chunkIndex;
            MaybeFailAtStage(5);
            candidate->domain = domain;
            candidate->object = root->object;
            candidate->root = root;
            candidate->exactType = root->exactType;
            candidate->identity = domain->nextCellIdentity;
            candidate->stableAddress = stableAddress;
            candidate->extent = root->extent;
            candidate->alignment = root->knownAlignment;
            candidate->descriptorHolds = 1;
            candidate->wholeObject = false;
            candidate->active = true;
            if (!domain->pins.insert(candidate).second)
            {
                ContractViolation();
            }
            published = true;
            domain->nextCellIdentity++;
            root->supportHolds++;
            root->object->activePins++;
            vacantOut->address = stableAddress;
            vacantOut->support = candidate;
            return ROOTPIN_TYPE_STATUS_SUCCESS;
        }
        catch (const std::bad_alloc&)
        {
            if (candidate != nullptr && !published)
            {
                ExclusiveLock lock(&domain->gate);
                ReleaseCellLocked(domain, candidate, chunkIndex);
            }
            return ROOTPIN_TYPE_STATUS_EXHAUSTED;
        }
        catch (const std::length_error&)
        {
            if (candidate != nullptr && !published)
            {
                ExclusiveLock lock(&domain->gate);
                ReleaseCellLocked(domain, candidate, chunkIndex);
            }
            return ROOTPIN_TYPE_STATUS_EXHAUSTED;
        }
    }

    uint32_t PinObjectEntry(
        RootPinDomainV1 domain,
        RootPinObjectCellV1 object,
        RootPinStableValue16V1* vacantOut)
    {
        if (domain != &s_domain || object == nullptr || vacantOut == nullptr
            || reinterpret_cast<uintptr_t>(vacantOut) % alignof(RootPinStableValue16V1) != 0
            || vacantOut->address != 0 || vacantOut->support != nullptr)
        {
            ContractViolation();
        }

        RootPinPinCellV1 candidate = nullptr;
        size_t chunkIndex = 0;
        bool published = false;
        try
        {
            ExclusiveLock lock(&domain->gate);
            if (!CanPinObject(domain, object)
                || object->activePins == std::numeric_limits<uint64_t>::max())
            {
                ContractViolation();
            }
            if (domain->nextCellIdentity == std::numeric_limits<uint64_t>::max())
            {
                return ROOTPIN_TYPE_STATUS_EXHAUSTED;
            }

            if (domain->pins.size() == std::numeric_limits<size_t>::max())
            {
                return ROOTPIN_TYPE_STATUS_EXHAUSTED;
            }
            domain->pins.reserve(domain->pins.size() + 1);
            candidate = AllocateCellLocked<RootPinPinCellImpl>(domain, chunkIndex);
            if (candidate == nullptr)
            {
                return ROOTPIN_TYPE_STATUS_EXHAUSTED;
            }

            candidate->arenaChunkIndex = chunkIndex;
            MaybeFailAtStage(6);
            candidate->domain = domain;
            candidate->object = object;
            candidate->root = nullptr;
            candidate->exactType = nullptr;
            candidate->identity = domain->nextCellIdentity;
            candidate->stableAddress = reinterpret_cast<uintptr_t>(object->currentBase);
            candidate->extent = object->payloadSize;
            candidate->alignment = object->alignment;
            candidate->descriptorHolds = 1;
            candidate->wholeObject = true;
            candidate->active = true;
            if (!domain->pins.insert(candidate).second)
            {
                ContractViolation();
            }
            published = true;
            domain->nextCellIdentity++;
            object->activePins++;
            vacantOut->address = reinterpret_cast<uintptr_t>(object->currentBase);
            vacantOut->support = candidate;
            return ROOTPIN_TYPE_STATUS_SUCCESS;
        }
        catch (const std::bad_alloc&)
        {
            if (candidate != nullptr && !published)
            {
                ExclusiveLock lock(&domain->gate);
                ReleaseCellLocked(domain, candidate, chunkIndex);
            }
            return ROOTPIN_TYPE_STATUS_EXHAUSTED;
        }
        catch (const std::length_error&)
        {
            if (candidate != nullptr && !published)
            {
                ExclusiveLock lock(&domain->gate);
                ReleaseCellLocked(domain, candidate, chunkIndex);
            }
            return ROOTPIN_TYPE_STATUS_EXHAUSTED;
        }
    }

    uint64_t ResolveExactEntry(RootPinDomainV1 domain, RootPinRootCellV1 root)
    {
        if (domain != &s_domain || root == nullptr)
        {
            ContractViolation();
        }

        ExclusiveLock lock(&domain->gate);
        if (!IsRootRegistered(domain, root) || root->domain != domain
            || !root->active || root->descriptorHolds == 0
            || !root->object->live || root->object->generation != root->generation)
        {
            ContractViolation();
        }

        uint64_t address;
        if (!CheckedPointerAddress(root->object->currentBase, root->offset, address))
        {
            ContractViolation();
        }
        return address;
    }

    void ReleaseRootDescriptorLocked(RootPinRootCellV1 root)
    {
        if (!root->active || root->descriptorHolds == 0)
        {
            ContractViolation();
        }
        root->descriptorHolds--;
        RetireRootIfUnheld(root);
    }

    void ReleasePinDescriptorLocked(RootPinPinCellV1 pin)
    {
        if (!pin->active || pin->descriptorHolds == 0
            || pin->object->activePins == 0)
        {
            ContractViolation();
        }
        pin->descriptorHolds--;
        if (pin->descriptorHolds != 0)
        {
            return;
        }

        auto* domain = pin->domain;
        auto* object = pin->object;
        auto* root = pin->root;
        const auto chunkIndex = pin->arenaChunkIndex;
        if (domain->pins.erase(pin) != 1)
        {
            ContractViolation();
        }
        object->activePins--;
        pin->stableAddress = 0;
        pin->active = false;
        if (root != nullptr)
        {
            if (root->supportHolds == 0)
            {
                ContractViolation();
            }
            root->supportHolds--;
            RetireRootIfUnheld(root);
        }
        ReleaseCellLocked(domain, pin, chunkIndex);
    }

    enum class SupportAction
    {
        ValidateRetain,
        Retain,
        ValidateRelease,
        Release,
        Trace,
    };

    struct ObjectTraceQueue
    {
        uint64_t epoch;
        RootPinObjectCellV1 first;
        RootPinObjectCellV1 last;
        uint64_t count;
    };

    bool ContainsSupport(const RootPinLoadedTypeV1* type)
    {
        if (type == nullptr)
        {
            ContractViolation();
        }
        return type->containsSupport;
    }

    const uint8_t* ValueAtOffset(const uint8_t* value, uint64_t offset, uint64_t extent)
    {
        if (value == nullptr || offset > std::numeric_limits<uintptr_t>::max()
            || extent > std::numeric_limits<size_t>::max())
        {
            ContractViolation();
        }

        const auto address = reinterpret_cast<uintptr_t>(value);
        const auto nativeOffset = static_cast<uintptr_t>(offset);
        if (nativeOffset > std::numeric_limits<uintptr_t>::max() - address
            || extent > std::numeric_limits<uintptr_t>::max() - address - nativeOffset)
        {
            ContractViolation();
        }
        return reinterpret_cast<const uint8_t*>(address + nativeOffset);
    }

    bool IsZeroValue(const uint8_t* value, uint64_t size)
    {
        if (size > std::numeric_limits<size_t>::max())
        {
            ContractViolation();
        }
        for (size_t index = 0; index < static_cast<size_t>(size); index++)
        {
            if (value[index] != 0)
            {
                return false;
            }
        }
        return true;
    }

    RootPinObjectCellV1 ReadObjectCell(const uint8_t* value)
    {
        RootPinObjectCellV1 object;
        std::memcpy(&object, value, sizeof(object));
        return object;
    }

    RootPinRootCellV1 ReadRootCell(const uint8_t* value)
    {
        RootPinRootCellV1 root;
        std::memcpy(&root, value, sizeof(root));
        return root;
    }

    RootPinStableValue16V1 ReadStableValue(const uint8_t* value)
    {
        RootPinStableValue16V1 stable;
        std::memcpy(&stable, value, sizeof(stable));
        return stable;
    }

    void EnqueueObjectLocked(
        RootPinDomainV1 domain,
        RootPinObjectCellV1 object,
        ObjectTraceQueue& queue)
    {
        if (!IsObjectRegistered(domain, object) || object->domain != domain
            || !object->published || !object->live || object->currentBase == nullptr
            || object->payloadType == nullptr
            || !IsLoadedTypeRegistered(domain, object->payloadType, ROOTPIN_TYPE_ROLE_GC_PAYLOAD))
        {
            ContractViolation();
        }
        if (object->traceEpoch == queue.epoch)
        {
            return;
        }

        object->traceEpoch = queue.epoch;
        object->traceNext = nullptr;
        if (queue.last == nullptr)
        {
            queue.first = object;
        }
        else
        {
            queue.last->traceNext = object;
        }
        queue.last = object;
        if (queue.count == std::numeric_limits<uint64_t>::max())
        {
            ContractViolation();
        }
        queue.count++;
    }

    void MarkRootLocked(
        RootPinDomainV1 domain,
        RootPinRootCellV1 root,
        RootPinLoadedTypeV1* expectedType,
        ObjectTraceQueue& queue)
    {
        if (!IsRootRegistered(domain, root) || root->domain != domain
            || !root->active
            || (root->descriptorHolds == 0 && root->supportHolds == 0)
            || root->exactType != expectedType
            || !root->object->live || root->object->generation != root->generation)
        {
            ContractViolation();
        }
        if (root->traceEpoch == queue.epoch)
        {
            return;
        }

        root->traceEpoch = queue.epoch;
        EnqueueObjectLocked(domain, root->object, queue);
    }

    void MarkPinLocked(
        RootPinDomainV1 domain,
        RootPinPinCellV1 pin,
        const RootPinStableValue16V1& stable,
        RootPinLoadedTypeV1* expectedType,
        bool wholeObject,
        ObjectTraceQueue& queue)
    {
        if (!IsPinRegistered(domain, pin) || pin->domain != domain
            || !pin->active || pin->descriptorHolds == 0
            || pin->stableAddress != stable.address || pin->wholeObject != wholeObject)
        {
            ContractViolation();
        }

        if (wholeObject)
        {
            if (pin->root != nullptr || pin->object->payloadType != expectedType
                || pin->extent != pin->object->payloadSize
                || stable.address != reinterpret_cast<uintptr_t>(pin->object->currentBase))
            {
                ContractViolation();
            }
        }
        else
        {
            if (pin->root == nullptr || !IsRootRegistered(domain, pin->root)
                || !pin->root->active || pin->root->object != pin->object)
            {
                ContractViolation();
            }
            MarkRootLocked(domain, pin->root, pin->root->exactType, queue);
        }

        if (pin->traceEpoch == queue.epoch)
        {
            return;
        }
        pin->traceEpoch = queue.epoch;
        if (wholeObject)
        {
            EnqueueObjectLocked(domain, pin->object, queue);
        }
    }

    void WalkSupportValueLocked(
        RootPinDomainV1 domain,
        RootPinLoadedTypeV1* type,
        const uint8_t* value,
        SupportAction action,
        ObjectTraceQueue* queue,
        bool reverse);

    void VisitSupportCellLocked(
        RootPinDomainV1 domain,
        RootPinRootCellV1 root,
        RootPinLoadedTypeV1* exactType,
        SupportAction action,
        ObjectTraceQueue* queue)
    {
        if (!IsRootRegistered(domain, root) || root->domain != domain
            || !root->active || root->descriptorHolds == 0
            || root->exactType != exactType || !root->object->live
            || root->object->generation != root->generation)
        {
            ContractViolation();
        }

        switch (action)
        {
            case SupportAction::ValidateRetain:
                if (root->descriptorHolds == std::numeric_limits<uint64_t>::max())
                {
                    ContractViolation();
                }
                break;
            case SupportAction::Retain:
                root->descriptorHolds++;
                break;
            case SupportAction::ValidateRelease:
                break;
            case SupportAction::Release:
                ReleaseRootDescriptorLocked(root);
                break;
            case SupportAction::Trace:
                if (queue == nullptr)
                {
                    ContractViolation();
                }
                MarkRootLocked(domain, root, exactType, *queue);
                break;
        }
    }

    void VisitPinCellLocked(
        RootPinDomainV1 domain,
        RootPinPinCellV1 pin,
        const RootPinStableValue16V1& stable,
        RootPinLoadedTypeV1* referredType,
        bool wholeObject,
        SupportAction action,
        ObjectTraceQueue* queue)
    {
        if (!IsPinRegistered(domain, pin) || pin->domain != domain
            || !pin->active || pin->descriptorHolds == 0
            || pin->stableAddress != stable.address || pin->wholeObject != wholeObject
            || !pin->object->live)
        {
            ContractViolation();
        }

        if (wholeObject)
        {
            if (pin->root != nullptr || pin->object->payloadType != referredType
                || pin->extent != pin->object->payloadSize
                || stable.address != reinterpret_cast<uintptr_t>(pin->object->currentBase))
            {
                ContractViolation();
            }
        }
        else
        {
            if (pin->root == nullptr || !IsRootRegistered(domain, pin->root)
                || !pin->root->active || pin->root->object != pin->object
                || pin->root->supportHolds == 0
                || stable.address < pin->stableAddress
                || stable.address - pin->stableAddress > pin->extent
                || referredType->size > pin->extent - (stable.address - pin->stableAddress))
            {
                ContractViolation();
            }
        }

        switch (action)
        {
            case SupportAction::ValidateRetain:
                if (pin->descriptorHolds == std::numeric_limits<uint64_t>::max())
                {
                    ContractViolation();
                }
                break;
            case SupportAction::Retain:
                pin->descriptorHolds++;
                break;
            case SupportAction::ValidateRelease:
                break;
            case SupportAction::Release:
                ReleasePinDescriptorLocked(pin);
                break;
            case SupportAction::Trace:
                if (queue == nullptr)
                {
                    ContractViolation();
                }
                MarkPinLocked(domain, pin, stable, referredType, wholeObject, *queue);
                break;
        }
    }

    void WalkSupportValueLocked(
        RootPinDomainV1 domain,
        RootPinLoadedTypeV1* type,
        const uint8_t* value,
        SupportAction action,
        ObjectTraceQueue* queue,
        bool reverse)
    {
        if (type == nullptr || type->domain != domain
            || type->key.role != ROOTPIN_TYPE_ROLE_VALUE || value == nullptr)
        {
            ContractViolation();
        }

        auto& stack = domain->supportWalkStack;
        if (!stack.empty() || stack.size() == stack.capacity())
        {
            ContractViolation();
        }
        stack.push_back({ type, value, 0 });

        while (!stack.empty())
        {
            auto& current = stack.back();
            auto* currentType = current.type;
            switch (currentType->shape)
            {
                case ROOTPIN_TYPE_SHAPE_LEAF:
                    stack.pop_back();
                    break;
                case ROOTPIN_TYPE_SHAPE_FIELDS:
                {
                    const auto fieldCount = static_cast<uint64_t>(currentType->fields.size());
                    if (current.cursor >= fieldCount)
                    {
                        stack.pop_back();
                        break;
                    }

                    const auto fieldOrdinal = reverse
                        ? currentType->fields.size() - 1 - static_cast<size_t>(current.cursor)
                        : static_cast<size_t>(current.cursor);
                    current.cursor++;
                    const auto& field = currentType->fields[fieldOrdinal];
                    if (stack.size() == stack.capacity())
                    {
                        ContractViolation();
                    }
                    stack.push_back({
                        field.valueType,
                        ValueAtOffset(current.value, field.offset, field.valueType->size),
                        0,
                    });
                    break;
                }
                case ROOTPIN_TYPE_SHAPE_OPTIONAL:
                {
                    if (current.cursor != 0)
                    {
                        stack.pop_back();
                        break;
                    }
                    current.cursor = 1;
                    if (!IsZeroValue(current.value, currentType->size))
                    {
                        if (stack.size() == stack.capacity())
                        {
                            ContractViolation();
                        }
                        stack.push_back({ currentType->child, current.value, 0 });
                    }
                    break;
                }
                case ROOTPIN_TYPE_SHAPE_OBJECT_CELL:
                {
                    const auto object = ReadObjectCell(current.value);
                    if (!IsObjectRegistered(domain, object) || object->domain != domain
                        || !object->published || !object->live
                        || object->payloadType != currentType->child)
                    {
                        ContractViolation();
                    }
                    if (action == SupportAction::Trace)
                    {
                        if (queue == nullptr)
                        {
                            ContractViolation();
                        }
                        EnqueueObjectLocked(domain, object, *queue);
                    }
                    stack.pop_back();
                    break;
                }
                case ROOTPIN_TYPE_SHAPE_ROOT_CELL:
                {
                    VisitSupportCellLocked(domain, ReadRootCell(current.value),
                        currentType->child, action, queue);
                    stack.pop_back();
                    break;
                }
                case ROOTPIN_TYPE_SHAPE_STABLE_REF:
                {
                    const auto stable = ReadStableValue(current.value);
                    if (stable.address == 0)
                    {
                        ContractViolation();
                    }
                    if (stable.support != nullptr)
                    {
                        VisitPinCellLocked(domain, stable.support, stable, currentType->child,
                            false, action, queue);
                    }
                    stack.pop_back();
                    break;
                }
                case ROOTPIN_TYPE_SHAPE_STABLE_CLASS:
                {
                    const auto stable = ReadStableValue(current.value);
                    if (stable.address == 0 || stable.support == nullptr)
                    {
                        ContractViolation();
                    }
                    VisitPinCellLocked(domain, stable.support, stable, currentType->child,
                        true, action, queue);
                    stack.pop_back();
                    break;
                }
                case ROOTPIN_TYPE_SHAPE_FIXED_ARRAY:
                {
                    if (!ContainsSupport(currentType->child)
                        || current.cursor >= currentType->elementCount)
                    {
                        stack.pop_back();
                        break;
                    }

                    const auto elementIndex = reverse
                        ? currentType->elementCount - 1 - current.cursor
                        : current.cursor;
                    current.cursor++;
                    if (elementIndex != 0 && currentType->elementStride
                        > std::numeric_limits<uint64_t>::max() / elementIndex)
                    {
                        ContractViolation();
                    }
                    if (stack.size() == stack.capacity())
                    {
                        ContractViolation();
                    }
                    const auto offset = elementIndex * currentType->elementStride;
                    stack.push_back({
                        currentType->child,
                        ValueAtOffset(current.value, offset, currentType->child->size),
                        0,
                    });
                    break;
                }
                default:
                    ContractViolation();
            }
        }
    }

    void RetainSupportLocked(
        RootPinDomainV1 domain,
        RootPinLoadedTypeV1* type,
        const void* value)
    {
        if (!IsLoadedTypeRegistered(domain, type, ROOTPIN_TYPE_ROLE_VALUE)
            || (type->flags & ROOTPIN_TYPE_FLAG_COPY) == 0)
        {
            ContractViolation();
        }
        WalkSupportValueLocked(domain, type, static_cast<const uint8_t*>(value),
            SupportAction::ValidateRetain, nullptr, false);
        WalkSupportValueLocked(domain, type, static_cast<const uint8_t*>(value),
            SupportAction::Retain, nullptr, false);
    }

    void ReleaseSupportLocked(
        RootPinDomainV1 domain,
        RootPinLoadedTypeV1* type,
        const void* value)
    {
        if (!IsLoadedTypeRegistered(domain, type, ROOTPIN_TYPE_ROLE_VALUE))
        {
            ContractViolation();
        }
        WalkSupportValueLocked(domain, type, static_cast<const uint8_t*>(value),
            SupportAction::ValidateRelease, nullptr, true);
        WalkSupportValueLocked(domain, type, static_cast<const uint8_t*>(value),
            SupportAction::Release, nullptr, true);
    }

    void TracePayloadLocked(
        RootPinDomainV1 domain,
        RootPinObjectCellV1 object,
        ObjectTraceQueue& queue)
    {
        const auto* payload = object->currentBase;
        auto* type = object->payloadType;
        if (type->shape == ROOTPIN_TYPE_SHAPE_FIELDS)
        {
            for (const auto& field : type->fields)
            {
                WalkSupportValueLocked(domain, field.valueType,
                    ValueAtOffset(payload, field.offset, field.valueType->size),
                    SupportAction::Trace, &queue, false);
            }
        }
        else if (type->shape == ROOTPIN_TYPE_SHAPE_BOX_PAYLOAD)
        {
            WalkSupportValueLocked(domain, type->child, payload,
                SupportAction::Trace, &queue, false);
        }
        else
        {
            ContractViolation();
        }
    }

    uint64_t NextTraceEpochLocked(RootPinDomainV1 domain)
    {
        if (domain->traceEpoch == std::numeric_limits<uint64_t>::max())
        {
            for (const auto& object : domain->ownedObjects)
            {
                object->traceEpoch = 0;
                object->traceNext = nullptr;
            }
            for (const auto root : domain->roots)
            {
                root->traceEpoch = 0;
            }
            for (const auto pin : domain->pins)
            {
                pin->traceEpoch = 0;
            }
            domain->traceEpoch = 0;
        }
        domain->traceEpoch++;
        if (domain->traceEpoch == 0)
        {
            ContractViolation();
        }
        return domain->traceEpoch;
    }

    void TraceFrameSlotsLocked(
        RootPinDomainV1 domain,
        RootPinSupportFrameV1* frame,
        ObjectTraceQueue& queue)
    {
        for (; frame != nullptr; frame = frame->previous)
        {
            if (frame->flags != 0 || frame->slotCount > std::numeric_limits<size_t>::max()
                / sizeof(*frame->slots)
                || (frame->slotCount != 0 && frame->slots == nullptr))
            {
                ContractViolation();
            }
            for (uint32_t index = 0; index < frame->slotCount; index++)
            {
                const auto& slot = frame->slots[index];
                if (slot.live > 1 || slot.ownsHold > 1
                    || (slot.live == 0 && slot.ownsHold != 0))
                {
                    ContractViolation();
                }
                if (slot.live != 0)
                {
                    if (!IsLoadedTypeRegistered(domain, slot.completeType,
                            ROOTPIN_TYPE_ROLE_VALUE))
                    {
                        ContractViolation();
                    }
                    WalkSupportValueLocked(domain, slot.completeType,
                        static_cast<const uint8_t*>(slot.residence),
                        SupportAction::Trace, &queue, false);
                }
            }
        }
    }

    void TraceQueuedPayloadsLocked(RootPinDomainV1 domain, ObjectTraceQueue& queue)
    {
        for (auto* object = queue.first; object != nullptr; object = object->traceNext)
        {
            TracePayloadLocked(domain, object, queue);
        }
    }

    void TraceCurrentFramesLocked(RootPinDomainV1 domain, ObjectTraceQueue& queue)
    {
        TraceFrameSlotsLocked(domain, s_threadState.frameTop, queue);
        TraceQueuedPayloadsLocked(domain, queue);
    }

    void TraceAllFramesLocked(RootPinDomainV1 domain, ObjectTraceQueue& queue)
    {
        auto* current = &s_threadState.mutator;
        size_t mutatorCount = 0;
        bool sawCollectionOwner = false;
        for (auto* mutator = domain->mutators; mutator != nullptr; mutator = mutator->next)
        {
            if (!mutator->registered || mutator->domain != domain
                || mutator->threadId == 0
                || (mutator->previous == nullptr
                    ? domain->mutators != mutator
                    : mutator->previous->next != mutator)
                || (mutator->next != nullptr && mutator->next->previous != mutator))
            {
                ContractViolation();
            }
            if (mutator == current)
            {
                if (domain->collectionOwner != current || current->parked
                    || current->frameTop != s_threadState.frameTop)
                {
                    ContractViolation();
                }
                sawCollectionOwner = true;
            }
            else if (!mutator->parked)
            {
                ContractViolation();
            }
            TraceFrameSlotsLocked(domain, mutator->frameTop, queue);
            mutatorCount++;
        }
        const size_t ownerCount = domain->collectionOwner == nullptr ? 0 : 1;
        if (mutatorCount != domain->mutatorCount || ownerCount > mutatorCount
            || sawCollectionOwner != (domain->collectionOwner != nullptr)
            || domain->parkedMutatorCount != mutatorCount - ownerCount)
        {
            ContractViolation();
        }
        TraceQueuedPayloadsLocked(domain, queue);
    }

    bool SlotBelongsToFrame(
        const RootPinSupportFrameV1* frame,
        const RootPinSupportSlotV1* slot)
    {
        if (frame == nullptr || slot == nullptr || frame->slots == nullptr
            || frame->slotCount > std::numeric_limits<size_t>::max() / sizeof(*frame->slots))
        {
            return false;
        }
        const auto base = reinterpret_cast<uintptr_t>(frame->slots);
        const auto address = reinterpret_cast<uintptr_t>(slot);
        const auto byteCount = static_cast<size_t>(frame->slotCount) * sizeof(*frame->slots);
        return address >= base && address - base < byteCount
            && (address - base) % sizeof(*frame->slots) == 0;
    }

    size_t FindMethodInput(
        const RootPinLoadedMethodImpl* method,
        uint8_t sourceRole,
        uint32_t formalOrdinal)
    {
        for (size_t index = 0; index < method->inputs.size(); index++)
        {
            const auto& input = method->inputs[index];
            if (input.sourceRole == sourceRole && input.formalOrdinal == formalOrdinal)
            {
                return index;
            }
        }
        ContractViolation();
    }

    RootPinLoadedMethodImpl* RequireLoadedMethod(
        RootPinDomainV1 domain,
        RootPinLoadedMethodV1 method)
    {
        if (method == nullptr)
        {
            ContractViolation();
        }
        const auto found = std::find_if(domain->ownedMethods.begin(), domain->ownedMethods.end(),
            [method](const auto& candidate) {
                return candidate.get() == method;
            });
        if (found == domain->ownedMethods.end() || (*found)->domain != domain)
        {
            ContractViolation();
        }
        return found->get();
    }

}

extern "C" ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
TerrumRootPinAdmitLoadedTypesV2(
    RootPinDomainV1 domain,
    const RootPinTypeBatchV2* batch,
    RootPinLoadedTypeV1** vacantOutTypes,
    uint32_t outCount)
{
    if (vacantOutTypes == nullptr
        || outCount > std::numeric_limits<size_t>::max() / sizeof(*vacantOutTypes))
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }
    MemoryRange batchRange;
    MemoryRange nodesRange;
    MemoryRange fieldsRange;
    MemoryRange originsRange;
    MemoryRange originUsesRange;
    MemoryRange outputRange;
    if (reinterpret_cast<uintptr_t>(vacantOutTypes) % alignof(RootPinLoadedTypeV1*) != 0
        || !GetMemoryRange(vacantOutTypes,
            static_cast<size_t>(outCount) * sizeof(*vacantOutTypes), outputRange))
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }
    if (batch == nullptr)
    {
        std::fill_n(vacantOutTypes, outCount, nullptr);
        return ROOTPIN_TYPE_STATUS_INVALID;
    }
    if (reinterpret_cast<uintptr_t>(batch) % alignof(RootPinTypeBatchV2) != 0
        || !GetMemoryRange(batch, sizeof(*batch), batchRange)
        || Overlaps(outputRange, batchRange)
        || !ValidArray(batch->nodes, batch->nodeCount, sizeof(*batch->nodes),
            alignof(RootPinTypeNodeV2), nodesRange)
        || !ValidArray(batch->fields, batch->fieldCount, sizeof(*batch->fields),
            alignof(RootPinTypeFieldV1), fieldsRange)
        || !ValidArray(batch->origins, batch->originCount, sizeof(*batch->origins),
            alignof(RootPinTypeOriginV1), originsRange)
        || !ValidArray(batch->originUses, batch->originUseCount, sizeof(*batch->originUses),
            alignof(uint32_t), originUsesRange)
        || Overlaps(outputRange, nodesRange)
        || Overlaps(outputRange, fieldsRange)
        || Overlaps(outputRange, originsRange)
        || Overlaps(outputRange, originUsesRange))
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }
    std::fill_n(vacantOutTypes, outCount, nullptr);

    try
    {
        if (domain != &s_domain)
        {
            return ROOTPIN_TYPE_STATUS_UNSUPPORTED;
        }
        if (batch->abiRevision != 2
            || batch->targetProfileId != 2
            || batch->targetProfileRevision != 1)
        {
            return ROOTPIN_TYPE_STATUS_UNSUPPORTED;
        }
        if (batch->byteSize != sizeof(RootPinTypeBatchV2)
            || batch->flags != 0 || batch->reserved != 0
            || batch->nodeCount == 0 || batch->nodeCount != outCount)
        {
            return ROOTPIN_TYPE_STATUS_INVALID;
        }

        ExclusiveLock lock(&domain->gate);
        std::vector<std::vector<uint64_t>> originKeys;
        std::unordered_map<uint64_t, Origin> batchOrigins;
        std::vector<uint8_t> supportByInput;
        if (!ValidateGraph(*batch, originKeys, batchOrigins, supportByInput))
        {
            return ROOTPIN_TYPE_STATUS_INVALID;
        }

        std::vector<RootPinLoadedTypeV1*> canonical(batch->nodeCount);
        std::vector<uint32_t> firstNodeByInput;
        firstNodeByInput.resize(batch->nodeCount);
        std::unordered_map<TypeKey, uint32_t, TypeKeyHash> firstInput;
        std::vector<std::unique_ptr<RootPinLoadedTypeV1>> newTypes;
        std::unordered_map<TypeKey, RootPinLoadedTypeV1*, TypeKeyHash> stagedTypes;

        for (uint32_t index = 0; index < batch->nodeCount; index++)
        {
            const auto& node = batch->nodes[index];
            const TypeKey key = { node.typeKey, node.role };
            const auto existing = domain->types.find(key);
            if (existing != domain->types.end())
            {
                if (!SameNodeLoaded(*batch, index, originKeys, *existing->second))
                {
                    return ROOTPIN_TYPE_STATUS_INVALID;
                }
                canonical[index] = existing->second;
                firstNodeByInput[index] = index;
                continue;
            }

            const auto duplicate = firstInput.find(key);
            if (duplicate != firstInput.end())
            {
                const auto firstIndex = duplicate->second;
                if (!SameNodeInput(*batch, index, firstIndex, originKeys))
                {
                    return ROOTPIN_TYPE_STATUS_INVALID;
                }
                firstNodeByInput[index] = firstIndex;
                continue;
            }

            auto loaded = CreateLoadedType(domain, *batch, index, originKeys, supportByInput);
            auto* pointer = loaded.get();
            newTypes.push_back(std::move(loaded));
            stagedTypes.emplace(key, pointer);
            firstInput.emplace(key, index);
            firstNodeByInput[index] = index;
            canonical[index] = pointer;
        }

        for (uint32_t index = 0; index < batch->nodeCount; index++)
        {
            if (firstNodeByInput[index] != index)
            {
                canonical[index] = canonical[firstNodeByInput[index]];
            }
            else if (canonical[index] == nullptr)
            {
                const auto& node = batch->nodes[index];
                const TypeKey key = { node.typeKey, node.role };
                const auto staged = stagedTypes.find(key);
                if (staged == stagedTypes.end())
                {
                    return ROOTPIN_TYPE_STATUS_INVALID;
                }
                canonical[index] = staged->second;
            }
        }

        for (const auto& loaded : newTypes)
        {
            const auto input = firstInput.find(loaded->key);
            if (input == firstInput.end())
            {
                return ROOTPIN_TYPE_STATUS_INVALID;
            }
            const auto& node = batch->nodes[input->second];
            if (node.child != ROOTPIN_TYPE_NO_INDEX)
            {
                loaded->child = canonical[node.child];
            }
            for (uint32_t ordinal = 0; ordinal < node.fieldCount; ordinal++)
            {
                const auto& field = batch->fields[node.fieldFirst + ordinal];
                loaded->fields.push_back({
                    field.fieldKey,
                    field.offset,
                    field.knownAlignment,
                    canonical[field.valueNode],
                });
            }
        }

        MaybeFailAtStage(1);
        auto nextTypes = domain->types;
        auto nextOrigins = domain->origins;
        const auto maximumSize = std::numeric_limits<size_t>::max();
        if (newTypes.size() > maximumSize - domain->types.size()
            || batchOrigins.size() > maximumSize - domain->origins.size()
            || newTypes.size() > maximumSize - domain->ownedTypes.size())
        {
            return ROOTPIN_TYPE_STATUS_EXHAUSTED;
        }
        const auto nextTypeCount = domain->types.size() + newTypes.size();
        const auto nextOriginCount = domain->origins.size() + batchOrigins.size();
        const auto nextOwnedTypeCount = domain->ownedTypes.size() + newTypes.size();
        if (nextTypeCount == maximumSize)
        {
            return ROOTPIN_TYPE_STATUS_EXHAUSTED;
        }
        if (!domain->supportWalkStack.empty())
        {
            ContractViolation();
        }
        domain->supportWalkStack.reserve(nextTypeCount + 1);
        nextTypes.reserve(nextTypeCount);
        nextOrigins.reserve(nextOriginCount);
        for (const auto& [key, pointer] : stagedTypes)
        {
            nextTypes.emplace(key, pointer);
        }

        std::vector<Origin> newOrigins;
        newOrigins.reserve(batchOrigins.size());
        for (const auto& [key, origin] : batchOrigins)
        {
            const auto existing = domain->origins.find(key);
            if (existing != domain->origins.end())
            {
                if (existing->second.cookie != origin.cookie
                    || existing->second.retain != origin.retain
                    || existing->second.release != origin.release
                    || existing->second.kind != origin.kind)
                {
                    return ROOTPIN_TYPE_STATUS_INVALID;
                }
                continue;
            }
            nextOrigins.emplace(key, origin);
            newOrigins.push_back(origin);
        }

        domain->ownedTypes.reserve(nextOwnedTypeCount);
        MaybeFailAtStage(2);
        MaybeFailAtStage(3);

        for (const auto& origin : newOrigins)
        {
            origin.retain(reinterpret_cast<void*>(static_cast<uintptr_t>(origin.cookie)));
        }
        for (auto& loaded : newTypes)
        {
            domain->ownedTypes.push_back(std::move(loaded));
        }
        domain->types.swap(nextTypes);
        domain->origins.swap(nextOrigins);

        for (uint32_t index = 0; index < batch->nodeCount; index++)
        {
            vacantOutTypes[index] = canonical[index];
        }

        return ROOTPIN_TYPE_STATUS_SUCCESS;
    }
    catch (const std::bad_alloc&)
    {
        return ROOTPIN_TYPE_STATUS_EXHAUSTED;
    }
    catch (const std::length_error&)
    {
        return ROOTPIN_TYPE_STATUS_EXHAUSTED;
    }
}

extern "C" ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
TerrumRootPinAdmitLoadedMethodsV1(
    RootPinDomainV1 domain,
    const RootPinMethodBatchV1* batch,
    RootPinLoadedMethodV1* vacantOutMethods,
    uint32_t outCount)
{
    if (vacantOutMethods == nullptr
        || outCount > std::numeric_limits<size_t>::max() / sizeof(*vacantOutMethods))
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }

    MemoryRange outputRange;
    if (!ValidArray(vacantOutMethods, outCount, sizeof(*vacantOutMethods),
            alignof(RootPinLoadedMethodV1), outputRange))
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }
    for (uint32_t index = 0; index < outCount; index++)
    {
        if (vacantOutMethods[index] != nullptr)
        {
            ContractViolation();
        }
    }
    if (batch == nullptr)
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }

    MemoryRange batchRange;
    if (reinterpret_cast<uintptr_t>(batch) % alignof(RootPinMethodBatchV1) != 0
        || !GetMemoryRange(batch, sizeof(*batch), batchRange)
        || Overlaps(outputRange, batchRange))
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }
    if (domain != &s_domain)
    {
        ContractViolation();
    }
    if (batch->abiRevision != 1
        || batch->targetProfileId != 2
        || batch->targetProfileRevision != 1)
    {
        return ROOTPIN_TYPE_STATUS_UNSUPPORTED;
    }
    if (batch->byteSize != sizeof(RootPinMethodBatchV1)
        || batch->flags != 0 || batch->reserved != 0
        || batch->methodCount == 0 || batch->methodCount != outCount)
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }

    MemoryRange methodsRange;
    MemoryRange inputsRange;
    MemoryRange originsRange;
    MemoryRange originUsesRange;
    if (!ValidArray(batch->methods, batch->methodCount, sizeof(*batch->methods),
            alignof(RootPinMethodNodeV1), methodsRange)
        || !ValidArray(batch->inputs, batch->inputCount, sizeof(*batch->inputs),
            alignof(RootPinMethodInputV1), inputsRange)
        || !ValidArray(batch->origins, batch->originCount, sizeof(*batch->origins),
            alignof(RootPinTypeOriginV1), originsRange)
        || !ValidArray(batch->originUses, batch->originUseCount,
            sizeof(*batch->originUses), alignof(uint32_t), originUsesRange)
        || Overlaps(batchRange, methodsRange)
        || Overlaps(batchRange, inputsRange)
        || Overlaps(batchRange, originsRange)
        || Overlaps(batchRange, originUsesRange)
        || Overlaps(outputRange, methodsRange)
        || Overlaps(outputRange, inputsRange)
        || Overlaps(outputRange, originsRange)
        || Overlaps(outputRange, originUsesRange)
        || Overlaps(methodsRange, inputsRange)
        || Overlaps(methodsRange, originsRange)
        || Overlaps(methodsRange, originUsesRange)
        || Overlaps(inputsRange, originsRange)
        || Overlaps(inputsRange, originUsesRange)
        || Overlaps(originsRange, originUsesRange))
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }

    std::vector<Origin> newOrigins;
    size_t retainedOriginCount = 0;
    try
    {
        ExclusiveLock lock(&domain->gate);
        std::vector<uint8_t> inputOwnership(batch->inputCount);
        std::vector<uint8_t> originUseOwnership(batch->originUseCount);
        std::vector<uint8_t> originOwnership(batch->originCount);
        std::unordered_map<uint64_t, Origin> batchOrigins;
        batchOrigins.reserve(batch->originCount);

        for (uint32_t index = 0; index < batch->originCount; index++)
        {
            const auto& source = batch->origins[index];
            if (source.originKey == 0 || source.retainOrigin == nullptr
                || source.releaseOrigin == nullptr
                || source.kind < ROOTPIN_TYPE_ORIGIN_SEMANTIC_LAYOUT
                || source.kind > ROOTPIN_TYPE_ORIGIN_ALLOCATOR_RUNTIME
                || source.reserved != 0)
            {
                return ROOTPIN_TYPE_STATUS_INVALID;
            }

            const Origin origin = {
                source.originKey,
                source.cookie,
                source.retainOrigin,
                source.releaseOrigin,
                source.kind,
            };
            const auto [existing, inserted] = batchOrigins.emplace(source.originKey, origin);
            if (!inserted && !SameOrigin(existing->second, origin))
            {
                return ROOTPIN_TYPE_STATUS_INVALID;
            }
        }

        std::vector<RootPinLoadedMethodV1> canonical(batch->methodCount);
        std::vector<std::unique_ptr<RootPinLoadedMethodImpl>> newMethods;
        newMethods.reserve(batch->methodCount);
        std::unordered_map<uint64_t, RootPinLoadedMethodImpl*> stagedByKey;
        stagedByKey.reserve(batch->methodCount);

        for (uint32_t index = 0; index < batch->methodCount; index++)
        {
            const auto& node = batch->methods[index];
            if (node.methodKey == 0 || node.resultType == nullptr
                || !IsLoadedTypeRegistered(domain, node.resultType, ROOTPIN_TYPE_ROLE_VALUE)
                || (node.flags & ~ROOTPIN_METHOD_FLAG_HAS_RECEIVER) != 0
                || node.reserved != 0
                || !ValidRange(node.inputFirst, node.inputCount, batch->inputCount)
                || !ValidRange(node.originFirst, node.originCount, batch->originUseCount)
                || node.originCount == 0
                || node.declarationOrigin >= batch->originCount
                || node.entryOrigin >= batch->originCount
                || node.entrySlotCount
                    > std::numeric_limits<size_t>::max() / sizeof(RootPinSupportSlotV1)
                || (ContainsSupport(node.resultType) && node.entrySlotCount == 0))
            {
                return ROOTPIN_TYPE_STATUS_INVALID;
            }

            const auto& declarationOrigin = batch->origins[node.declarationOrigin];
            const auto& entryOrigin = batch->origins[node.entryOrigin];
            if (declarationOrigin.kind != ROOTPIN_TYPE_ORIGIN_SEMANTIC_LAYOUT
                || entryOrigin.kind != ROOTPIN_TYPE_ORIGIN_CODE_UNWIND)
            {
                return ROOTPIN_TYPE_STATUS_INVALID;
            }

            auto candidate = std::make_unique<RootPinLoadedMethodImpl>();
            candidate->domain = domain;
            candidate->identity = node.methodKey;
            candidate->resultType = node.resultType;
            candidate->formalCount = node.formalCount;
            candidate->entrySlotCount = node.entrySlotCount;
            candidate->flags = node.flags;
            candidate->declarationOrigin = declarationOrigin.originKey;
            candidate->entryOrigin = entryOrigin.originKey;
            candidate->inputs.reserve(node.inputCount);
            candidate->origins.reserve(node.originCount);

            std::unordered_set<uint32_t> entrySlots;
            entrySlots.reserve(node.inputCount);
            bool sawReceiver = false;
            bool sawFormal = false;
            bool hasPreviousFormal = false;
            uint32_t previousFormal = 0;
            for (uint32_t ordinal = 0; ordinal < node.inputCount; ordinal++)
            {
                const auto inputIndex = node.inputFirst + ordinal;
                if (inputOwnership[inputIndex] != 0)
                {
                    return ROOTPIN_TYPE_STATUS_INVALID;
                }
                inputOwnership[inputIndex] = 1;
                const auto& input = batch->inputs[inputIndex];
                if (input.sourceRole > ROOTPIN_METHOD_SOURCE_RECEIVER
                    || input.handoffKind > ROOTPIN_METHOD_HANDOFF_OWNED
                    || input.reserved16 != 0 || input.reserved32 != 0
                    || input.entrySlot >= node.entrySlotCount
                    || input.completeType == nullptr
                    || !IsLoadedTypeRegistered(domain, input.completeType,
                        ROOTPIN_TYPE_ROLE_VALUE)
                    || !ContainsSupport(input.completeType)
                    || !entrySlots.insert(input.entrySlot).second)
                {
                    return ROOTPIN_TYPE_STATUS_INVALID;
                }

                if (input.sourceRole == ROOTPIN_METHOD_SOURCE_RECEIVER)
                {
                    if (ordinal != 0 || sawReceiver || sawFormal
                        || input.formalOrdinal != 0
                        || (node.flags & ROOTPIN_METHOD_FLAG_HAS_RECEIVER) == 0)
                    {
                        return ROOTPIN_TYPE_STATUS_INVALID;
                    }
                    sawReceiver = true;
                }
                else
                {
                    if (input.formalOrdinal >= node.formalCount
                        || (hasPreviousFormal
                            && input.formalOrdinal <= previousFormal))
                    {
                        return ROOTPIN_TYPE_STATUS_INVALID;
                    }
                    sawFormal = true;
                    previousFormal = input.formalOrdinal;
                    hasPreviousFormal = true;
                }

                candidate->inputs.push_back({
                    input.formalOrdinal,
                    input.entrySlot,
                    input.completeType,
                    input.sourceRole,
                    input.handoffKind,
                });
            }

            uint64_t previousOriginKey = 0;
            for (uint32_t ordinal = 0; ordinal < node.originCount; ordinal++)
            {
                const auto useIndex = node.originFirst + ordinal;
                if (originUseOwnership[useIndex] != 0)
                {
                    return ROOTPIN_TYPE_STATUS_INVALID;
                }
                originUseOwnership[useIndex] = 1;
                const auto originIndex = batch->originUses[useIndex];
                if (originIndex >= batch->originCount)
                {
                    return ROOTPIN_TYPE_STATUS_INVALID;
                }
                originOwnership[originIndex] = 1;
                const auto originKey = batch->origins[originIndex].originKey;
                if (originKey <= previousOriginKey)
                {
                    return ROOTPIN_TYPE_STATUS_INVALID;
                }
                candidate->origins.push_back(originKey);
                previousOriginKey = originKey;
            }
            if (!std::binary_search(candidate->origins.begin(), candidate->origins.end(),
                    candidate->declarationOrigin)
                || !std::binary_search(candidate->origins.begin(), candidate->origins.end(),
                    candidate->entryOrigin))
            {
                return ROOTPIN_TYPE_STATUS_INVALID;
            }

            const auto existing = domain->methods.find(node.methodKey);
            if (existing != domain->methods.end())
            {
                if (!SameLoadedMethod(*existing->second, *candidate))
                {
                    return ROOTPIN_TYPE_STATUS_INVALID;
                }
                canonical[index] = existing->second;
                continue;
            }

            const auto duplicate = stagedByKey.find(node.methodKey);
            if (duplicate != stagedByKey.end())
            {
                if (!SameLoadedMethod(*duplicate->second, *candidate))
                {
                    return ROOTPIN_TYPE_STATUS_INVALID;
                }
                canonical[index] = duplicate->second;
                continue;
            }

            auto* pointer = candidate.get();
            stagedByKey.emplace(node.methodKey, pointer);
            newMethods.push_back(std::move(candidate));
            canonical[index] = pointer;
        }

        if (!std::all_of(inputOwnership.begin(), inputOwnership.end(),
                [](uint8_t owned) { return owned == 1; })
            || !std::all_of(originUseOwnership.begin(), originUseOwnership.end(),
                [](uint8_t owned) { return owned == 1; })
            || !std::all_of(originOwnership.begin(), originOwnership.end(),
                [](uint8_t owned) { return owned == 1; }))
        {
            return ROOTPIN_TYPE_STATUS_INVALID;
        }

        const auto maximumSize = std::numeric_limits<size_t>::max();
        if (newMethods.size() > maximumSize - domain->methods.size()
            || newMethods.size() > maximumSize - domain->ownedMethods.size()
            || batchOrigins.size() > maximumSize - domain->origins.size())
        {
            return ROOTPIN_TYPE_STATUS_EXHAUSTED;
        }

        auto nextMethods = domain->methods;
        auto nextOrigins = domain->origins;
        nextMethods.reserve(domain->methods.size() + newMethods.size());
        nextOrigins.reserve(domain->origins.size() + batchOrigins.size());
        for (const auto& method : newMethods)
        {
            if (!nextMethods.emplace(method->identity, method.get()).second)
            {
                ContractViolation();
            }
        }

        std::vector<std::pair<uint64_t, Origin>> orderedOrigins;
        orderedOrigins.reserve(batchOrigins.size());
        for (const auto& entry : batchOrigins)
        {
            orderedOrigins.push_back(entry);
        }
        std::sort(orderedOrigins.begin(), orderedOrigins.end(),
            [](const auto& left, const auto& right) {
                return left.first < right.first;
            });
        newOrigins.reserve(orderedOrigins.size());
        for (const auto& [key, origin] : orderedOrigins)
        {
            const auto existing = domain->origins.find(key);
            if (existing != domain->origins.end())
            {
                if (!SameOrigin(existing->second, origin))
                {
                    return ROOTPIN_TYPE_STATUS_INVALID;
                }
                continue;
            }
            nextOrigins.emplace(key, origin);
            newOrigins.push_back(origin);
        }

        domain->ownedMethods.reserve(domain->ownedMethods.size() + newMethods.size());
        MaybeFailAtStage(7);
        for (const auto& origin : newOrigins)
        {
            try
            {
                origin.retain(reinterpret_cast<void*>(static_cast<uintptr_t>(origin.cookie)));
            }
            catch (...)
            {
                ContractViolation();
            }
            retainedOriginCount++;
        }
        MaybeFailAtStage(8);

        for (auto& method : newMethods)
        {
            domain->ownedMethods.push_back(std::move(method));
        }
        domain->methods.swap(nextMethods);
        domain->origins.swap(nextOrigins);
        for (uint32_t index = 0; index < batch->methodCount; index++)
        {
            vacantOutMethods[index] = canonical[index];
        }
        return ROOTPIN_TYPE_STATUS_SUCCESS;
    }
    catch (const std::bad_alloc&)
    {
        ExclusiveLock rollbackLock(&domain->gate);
        while (retainedOriginCount != 0)
        {
            const auto& origin = newOrigins[--retainedOriginCount];
            try
            {
                origin.release(reinterpret_cast<void*>(static_cast<uintptr_t>(origin.cookie)));
            }
            catch (...)
            {
                ContractViolation();
            }
        }
        return ROOTPIN_TYPE_STATUS_EXHAUSTED;
    }
    catch (const std::length_error&)
    {
        ExclusiveLock rollbackLock(&domain->gate);
        while (retainedOriginCount != 0)
        {
            const auto& origin = newOrigins[--retainedOriginCount];
            try
            {
                origin.release(reinterpret_cast<void*>(static_cast<uintptr_t>(origin.cookie)));
            }
            catch (...)
            {
                ContractViolation();
            }
        }
        return ROOTPIN_TYPE_STATUS_EXHAUSTED;
    }
}

#if defined(TERRUM_ROOT_PIN_TESTS)
extern "C" ROOTPIN_LOADED_TYPES_API RootPinDomainV1 ROOTPIN_LOADED_TYPES_CALL
RootPinTestGetDomainV1(void)
{
    return &s_domain;
}

extern "C" ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestFailAdmissionAtStageV2(uint32_t stage)
{
    s_failureStage.store(stage);
}

extern "C" ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestFailRelocationAllocationAtV1(uint32_t oneBasedOrdinal)
{
    s_relocationFailureOrdinal.store(oneBasedOrdinal);
}

extern "C" ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestRegisterObjectV2(
    RootPinDomainV1 domain,
    void* payload,
    uint64_t payloadSize,
    uint64_t payloadAlignment,
    RootPinLoadedTypeV1* payloadType,
    RootPinObjectCellV1* vacantOut)
{
    if (domain != &s_domain || vacantOut == nullptr
        || reinterpret_cast<uintptr_t>(vacantOut) % alignof(RootPinObjectCellV1) != 0
        || *vacantOut != nullptr
        || payloadType == nullptr
        || !HasValidAlignment(payloadSize, payloadAlignment)
        || (payloadSize != 0 && payload == nullptr)
        || (payload != nullptr && payloadAlignment != 0
            && reinterpret_cast<uintptr_t>(payload) % payloadAlignment != 0))
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }

    MemoryRange payloadRange;
    if (!GetMemoryRange(payload, static_cast<size_t>(payloadSize), payloadRange))
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }

    auto candidate = std::unique_ptr<RootPinObjectCellImpl>(
        new (std::nothrow) RootPinObjectCellImpl{});
    if (candidate == nullptr)
    {
        return ROOTPIN_TYPE_STATUS_EXHAUSTED;
    }

    try
    {
        ExclusiveLock lock(&domain->gate);
        if (!IsLoadedTypeRegistered(domain, payloadType, ROOTPIN_TYPE_ROLE_GC_PAYLOAD)
            || (payloadType->shape != ROOTPIN_TYPE_SHAPE_FIELDS
                && payloadType->shape != ROOTPIN_TYPE_SHAPE_BOX_PAYLOAD)
            || payloadType->size != payloadSize
            || payloadType->alignment != payloadAlignment)
        {
            return ROOTPIN_TYPE_STATUS_INVALID;
        }
        if (domain->nextObjectGeneration == std::numeric_limits<uint64_t>::max())
        {
            return ROOTPIN_TYPE_STATUS_EXHAUSTED;
        }
        if (domain->objects.size() == std::numeric_limits<size_t>::max()
            || domain->ownedObjects.size() == std::numeric_limits<size_t>::max())
        {
            return ROOTPIN_TYPE_STATUS_EXHAUSTED;
        }
        domain->objects.reserve(domain->objects.size() + 1);
        domain->ownedObjects.reserve(domain->ownedObjects.size() + 1);

        candidate->domain = domain;
        candidate->currentBase = static_cast<uint8_t*>(payload);
        candidate->currentBaseOwned = false;
        candidate->payloadSize = payloadSize;
        candidate->alignment = payloadAlignment;
        candidate->generation = domain->nextObjectGeneration;
        candidate->rootEdges = 0;
        candidate->activePins = 0;
        candidate->published = true;
        candidate->live = true;
        candidate->payloadType = payloadType;
        auto* published = candidate.get();
        if (!domain->objects.insert(published).second)
        {
            ContractViolation();
        }
        domain->ownedObjects.push_back(std::move(candidate));
        domain->nextObjectGeneration++;
        *vacantOut = published;
        return ROOTPIN_TYPE_STATUS_SUCCESS;
    }
    catch (const std::bad_alloc&)
    {
        return ROOTPIN_TYPE_STATUS_EXHAUSTED;
    }
    catch (const std::length_error&)
    {
        return ROOTPIN_TYPE_STATUS_EXHAUSTED;
    }
}

extern "C" ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestCanRootV2(
    RootPinDomainV1 domain,
    RootPinObjectCellV1 object,
    const RootPinPlaceRequestV1* request,
    uint32_t* outCanRoot)
{
    if (outCanRoot == nullptr
        || reinterpret_cast<uintptr_t>(outCanRoot) % alignof(uint32_t) != 0)
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }
    *outCanRoot = 0;
    if (domain != &s_domain || request == nullptr
        || reinterpret_cast<uintptr_t>(request) % alignof(RootPinPlaceRequestV1) != 0)
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }

    try
    {
        ExclusiveLock lock(&domain->gate);
        uint64_t actualAlignment;
        *outCanRoot = ValidateRootRequest(domain, object, *request, actualAlignment) ? 1u : 0u;
        return ROOTPIN_TYPE_STATUS_SUCCESS;
    }
    catch (const std::bad_alloc&)
    {
        return ROOTPIN_TYPE_STATUS_EXHAUSTED;
    }
    catch (const std::length_error&)
    {
        return ROOTPIN_TYPE_STATUS_EXHAUSTED;
    }
}

extern "C" ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestCanPinObjectV2(
    RootPinDomainV1 domain,
    RootPinObjectCellV1 object,
    uint32_t* outCanPin)
{
    if (outCanPin == nullptr
        || reinterpret_cast<uintptr_t>(outCanPin) % alignof(uint32_t) != 0)
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }
    *outCanPin = 0;
    if (domain != &s_domain)
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }

    ExclusiveLock lock(&domain->gate);
    *outCanPin = CanPinObject(domain, object) ? 1u : 0u;
    return ROOTPIN_TYPE_STATUS_SUCCESS;
}

extern "C" ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestRootExactV2(
    RootPinDomainV1 domain,
    RootPinObjectCellV1 object,
    const RootPinPlaceRequestV1* request,
    RootPinRootCellV1* vacantOut)
{
    return RootExactEntry(domain, object, request, vacantOut);
}

extern "C" ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestPinExactV2(
    RootPinDomainV1 domain,
    RootPinRootCellV1 root,
    RootPinStableValue16V1* vacantOut)
{
    return PinExactEntry(domain, root, vacantOut);
}

extern "C" ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestPinObjectV2(
    RootPinDomainV1 domain,
    RootPinObjectCellV1 object,
    RootPinStableValue16V1* vacantOut)
{
    return PinObjectEntry(domain, object, vacantOut);
}

extern "C" ROOTPIN_LOADED_TYPES_API uint64_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestResolveExactV2(
    RootPinDomainV1 domain,
    RootPinRootCellV1 root)
{
    return ResolveExactEntry(domain, root);
}

extern "C" ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestReplaceObjectPayloadV2(
    RootPinDomainV1 domain,
    RootPinObjectCellV1 object,
    void* payload)
{
    if (domain != &s_domain || object == nullptr)
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }

    ExclusiveLock lock(&domain->gate);
    if (!IsObjectRegistered(domain, object) || object->domain != domain
        || !object->published || !object->live
        || (object->payloadSize != 0 && payload == nullptr)
        || (payload != nullptr && object->alignment != 0
            && reinterpret_cast<uintptr_t>(payload) % object->alignment != 0))
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }

    MemoryRange payloadRange;
    if (!GetMemoryRange(payload, static_cast<size_t>(object->payloadSize), payloadRange))
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }
    if (object->activePins != 0)
    {
        return ROOTPIN_TYPE_STATUS_UNSUPPORTED;
    }
    if (object->currentBase == payload)
    {
        return ROOTPIN_TYPE_STATUS_SUCCESS;
    }
    auto* previousBase = object->currentBase;
    const bool previousBaseOwned = object->currentBaseOwned;
    object->currentBase = static_cast<uint8_t*>(payload);
    object->currentBaseOwned = false;
    if (previousBaseOwned)
    {
        _aligned_free(previousBase);
    }
    return ROOTPIN_TYPE_STATUS_SUCCESS;
}

extern "C" ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestReleaseRootV2(
    RootPinDomainV1 domain,
    RootPinRootCellV1 root)
{
    if (domain != &s_domain || root == nullptr)
    {
        ContractViolation();
    }

    ExclusiveLock lock(&domain->gate);
    if (!IsRootRegistered(domain, root) || root->domain != domain)
    {
        ContractViolation();
    }
    ReleaseRootDescriptorLocked(root);
}

extern "C" ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestReleasePinV2(
    RootPinDomainV1 domain,
    RootPinPinCellV1 pin)
{
    if (domain != &s_domain || pin == nullptr)
    {
        ContractViolation();
    }

    ExclusiveLock lock(&domain->gate);
    if (!IsPinRegistered(domain, pin) || pin->domain != domain)
    {
        ContractViolation();
    }
    ReleasePinDescriptorLocked(pin);
}

extern "C" ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestGetObjectCountsV2(
    RootPinDomainV1 domain,
    RootPinObjectCellV1 object,
    uint64_t* rootEdges,
    uint64_t* activePins)
{
    if (domain != &s_domain || object == nullptr || rootEdges == nullptr || activePins == nullptr)
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }

    ExclusiveLock lock(&domain->gate);
    if (!IsObjectRegistered(domain, object) || object->domain != domain)
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }
    *rootEdges = object->rootEdges;
    *activePins = object->activePins;
    return ROOTPIN_TYPE_STATUS_SUCCESS;
}

extern "C" ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestRetainSupportV1(
    RootPinDomainV1 domain,
    RootPinLoadedTypeV1* completeType,
    const void* value)
{
    if (domain != &s_domain)
    {
        ContractViolation();
    }

    ExclusiveLock lock(&domain->gate);
    RetainSupportLocked(domain, completeType, value);
}

extern "C" ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestReleaseSupportV1(
    RootPinDomainV1 domain,
    RootPinLoadedTypeV1* completeType,
    const void* value)
{
    if (domain != &s_domain)
    {
        ContractViolation();
    }

    ExclusiveLock lock(&domain->gate);
    ReleaseSupportLocked(domain, completeType, value);
}

#endif

namespace
{
void ROOTPIN_LOADED_TYPES_CALL EnterFrameEntry(
    RootPinDomainV1 domain,
    RootPinSupportFrameV1* frame)
{
    if (domain != &s_domain || frame == nullptr
        || reinterpret_cast<uintptr_t>(frame) % alignof(RootPinSupportFrameV1) != 0)
    {
        ContractViolation();
    }

    MemoryRange frameRange;
    MemoryRange slotsRange;
    if (!GetMemoryRange(frame, sizeof(*frame), frameRange))
    {
        ContractViolation();
    }

    ExclusiveLock coordination(&domain->coordination);
    auto* mutator = &s_threadState.mutator;
    if (mutator->registered && (mutator->domain != domain
        || mutator->threadId != GetCurrentThreadId()
        || mutator->frameTop != s_threadState.frameTop))
    {
        ContractViolation();
    }
    if (!mutator->registered)
    {
        WaitForCollectionLocked(domain);
    }

    ExclusiveLock lock(&domain->gate, false);
    if (frame->previous != nullptr || frame->flags != 0
        || !ValidArray(frame->slots, frame->slotCount, sizeof(*frame->slots),
            alignof(RootPinSupportSlotV1), slotsRange)
        || Overlaps(frameRange, slotsRange))
    {
        ContractViolation();
    }

    auto* method = frame->entryMethod == nullptr
        ? nullptr
        : RequireLoadedMethod(domain, frame->entryMethod);
    auto* call = s_threadState.callTop;
    const bool enteringCall = call != nullptr && call->state == 1;
    if (method != nullptr
        && (frame->slotCount != method->entrySlotCount
            || (!enteringCall && !method->inputs.empty())))
    {
        ContractViolation();
    }
    if (enteringCall && (method == nullptr || call->target != frame->entryMethod))
    {
        ContractViolation();
    }

    for (uint32_t index = 0; index < frame->slotCount; index++)
    {
        auto& slot = frame->slots[index];
        if (slot.residence == nullptr || slot.live > 1 || slot.ownsHold > 1
            || (slot.live == 0 && slot.ownsHold != 0)
            || !IsLoadedTypeRegistered(domain, slot.completeType, ROOTPIN_TYPE_ROLE_VALUE))
        {
            ContractViolation();
        }
        if (slot.live != 0)
        {
            WalkSupportValueLocked(domain, slot.completeType,
                static_cast<const uint8_t*>(slot.residence),
                SupportAction::ValidateRelease, nullptr, false);
        }
        if (slot.ownsHold != 0)
        {
            if (!ContainsSupport(slot.completeType))
            {
                ContractViolation();
            }
            for (uint32_t previous = 0; previous < index; previous++)
            {
                if (frame->slots[previous].ownsHold != 0
                    && frame->slots[previous].residence == slot.residence)
                {
                    ContractViolation();
                }
            }
        }
    }

    if (enteringCall)
    {
        auto* caller = s_threadState.frameTop;
        if (caller == nullptr
            || (method->resultType != nullptr
                && !IsLoadedTypeRegistered(domain, method->resultType,
                    ROOTPIN_TYPE_ROLE_VALUE)))
        {
            ContractViolation();
        }

        for (const auto& input : method->inputs)
        {
            if (input.entrySlot >= frame->slotCount)
            {
                ContractViolation();
            }
            const auto& destination = frame->slots[input.entrySlot];
            if (destination.completeType != input.completeType
                || (input.handoffKind == ROOTPIN_METHOD_HANDOFF_BORROWED
                    && (destination.live != 1 || destination.ownsHold != 0))
                || (input.handoffKind == ROOTPIN_METHOD_HANDOFF_OWNED
                    && (destination.live != 0 || destination.ownsHold != 0)))
            {
                ContractViolation();
            }
        }

        size_t previousMapIndex = 0;
        bool hasPreviousMapIndex = false;
        for (uint32_t index = 0; index < call->inputCount; index++)
        {
            const auto& input = call->inputs[index];
            const auto mapIndex = FindMethodInput(method, input.sourceRole, input.formalOrdinal);
            if ((hasPreviousMapIndex && mapIndex <= previousMapIndex)
                || !SlotBelongsToFrame(caller, input.stagedSource)
                || input.stagedSource->live != 1 || input.stagedSource->ownsHold != 1
                || !ContainsSupport(input.stagedSource->completeType))
            {
                ContractViolation();
            }
            WalkSupportValueLocked(domain, input.stagedSource->completeType,
                static_cast<const uint8_t*>(input.stagedSource->residence),
                SupportAction::ValidateRelease, nullptr, false);
            previousMapIndex = mapIndex;
            hasPreviousMapIndex = true;

            const auto& mapped = method->inputs[mapIndex];
            if (mapped.completeType != input.stagedSource->completeType
                || mapped.handoffKind != ROOTPIN_METHOD_HANDOFF_OWNED
                || mapped.entrySlot >= frame->slotCount)
            {
                ContractViolation();
            }
            auto& destination = frame->slots[mapped.entrySlot];
            if (destination.live != 0 || destination.ownsHold != 0
                || destination.completeType != mapped.completeType
                || std::memcmp(destination.residence, input.stagedSource->residence,
                    static_cast<size_t>(mapped.completeType->size)) != 0)
            {
                ContractViolation();
            }
            for (uint32_t previous = 0; previous < index; previous++)
            {
                const auto& previousInput = call->inputs[previous];
                const auto priorMapIndex = FindMethodInput(
                    method, previousInput.sourceRole, previousInput.formalOrdinal);
                if (method->inputs[priorMapIndex].entrySlot == mapped.entrySlot)
                {
                    ContractViolation();
                }
            }
        }
    }

    if (!mutator->registered)
    {
        RegisterMutatorLocked(domain);
    }
    frame->previous = s_threadState.frameTop;
    s_threadState.frameTop = frame;
    mutator->frameTop = frame;
    if (enteringCall)
    {
        for (uint32_t index = 0; index < call->inputCount; index++)
        {
            const auto& input = call->inputs[index];
            const auto mapIndex = FindMethodInput(method, input.sourceRole, input.formalOrdinal);
            auto& destination = frame->slots[method->inputs[mapIndex].entrySlot];
            input.stagedSource->live = 0;
            input.stagedSource->ownsHold = 0;
            destination.live = 1;
            destination.ownsHold = 1;
        }
        call->state = 2;
    }
}

void ROOTPIN_LOADED_TYPES_CALL ExitFrameEntry(
    RootPinDomainV1 domain,
    RootPinSupportFrameV1* frame)
{
    if (domain != &s_domain || frame == nullptr
        || reinterpret_cast<uintptr_t>(frame) % alignof(RootPinSupportFrameV1) != 0)
    {
        ContractViolation();
    }

    ExclusiveLock coordination(&domain->coordination);
    auto* mutator = &s_threadState.mutator;
    if (!mutator->registered || mutator->domain != domain
        || mutator->threadId != GetCurrentThreadId()
        || mutator->frameTop != frame || s_threadState.frameTop != frame)
    {
        ContractViolation();
    }

    ExclusiveLock lock(&domain->gate, false);
    if (s_threadState.frameTop != frame || frame->flags != 0)
    {
        ContractViolation();
    }

    for (uint32_t index = frame->slotCount; index > 0; index--)
    {
        auto& slot = frame->slots[index - 1];
        if (slot.live > 1 || slot.ownsHold > 1
            || (slot.live == 0 && slot.ownsHold != 0))
        {
            ContractViolation();
        }
        if (slot.live != 0 && slot.ownsHold != 0)
        {
            ReleaseSupportLocked(domain, slot.completeType, slot.residence);
        }
        slot.live = 0;
        slot.ownsHold = 0;
    }

    s_threadState.frameTop = frame->previous;
    mutator->frameTop = s_threadState.frameTop;
    frame->previous = nullptr;
    if (mutator->frameTop == nullptr)
    {
        UnregisterMutatorLocked(domain);
    }
}

void ROOTPIN_LOADED_TYPES_CALL PollEntry(RootPinDomainV1 domain)
{
    if (domain != &s_domain)
    {
        ContractViolation();
    }

    ExclusiveLock coordination(&domain->coordination);
    WaitForCollectionLocked(domain);
}

void ROOTPIN_LOADED_TYPES_CALL BeginCallEntry(
    RootPinDomainV1 domain,
    RootPinCallTransferV1* transfer)
{
    if (domain != &s_domain || transfer == nullptr
        || reinterpret_cast<uintptr_t>(transfer) % alignof(RootPinCallTransferV1) != 0)
    {
        ContractViolation();
    }

    MemoryRange transferRange;
    MemoryRange inputsRange;
    if (!GetMemoryRange(transfer, sizeof(*transfer), transferRange))
    {
        ContractViolation();
    }

    ExclusiveLock lock(&domain->gate);
    if (transfer->previous != nullptr || transfer->state != 0
        || !ValidArray(transfer->inputs, transfer->inputCount, sizeof(*transfer->inputs),
            alignof(RootPinInputTransferV1), inputsRange)
        || Overlaps(transferRange, inputsRange)
        || s_threadState.frameTop == nullptr)
    {
        ContractViolation();
    }

    auto* method = RequireLoadedMethod(domain, transfer->target);
    if (method->resultType == nullptr)
    {
        ContractViolation();
    }
    const bool resultNeedsSupport = ContainsSupport(method->resultType);
    if (resultNeedsSupport != (transfer->prospectiveResult != nullptr))
    {
        ContractViolation();
    }
    const auto requiredTransfers = static_cast<uint32_t>(std::count_if(
        method->inputs.begin(), method->inputs.end(), [](const MethodInputSlot& input) {
            return input.handoffKind == ROOTPIN_METHOD_HANDOFF_OWNED;
        }));
    if (transfer->inputCount != requiredTransfers)
    {
        ContractViolation();
    }
    if (transfer->prospectiveResult != nullptr)
    {
        auto* result = transfer->prospectiveResult;
        if (!SlotBelongsToFrame(s_threadState.frameTop, result)
            || result->live != 0 || result->ownsHold != 0
            || result->completeType != method->resultType
            || IsZeroValue(static_cast<const uint8_t*>(result->residence),
                method->resultType->size) == false)
        {
            ContractViolation();
        }
    }

    size_t previousMapIndex = 0;
    bool hasPreviousMapIndex = false;
    for (uint32_t index = 0; index < transfer->inputCount; index++)
    {
        const auto& input = transfer->inputs[index];
        if (input.flags != 0 || input.reserved != 0
            || input.sourceRole > 1 || (input.sourceRole == 1 && input.formalOrdinal != 0)
            || !SlotBelongsToFrame(s_threadState.frameTop, input.stagedSource)
            || input.stagedSource->live != 1 || input.stagedSource->ownsHold != 1
            || !ContainsSupport(input.stagedSource->completeType))
        {
            ContractViolation();
        }
        WalkSupportValueLocked(domain, input.stagedSource->completeType,
            static_cast<const uint8_t*>(input.stagedSource->residence),
            SupportAction::ValidateRelease, nullptr, false);
        const auto mapIndex = FindMethodInput(method, input.sourceRole, input.formalOrdinal);
        if ((hasPreviousMapIndex && mapIndex <= previousMapIndex)
            || method->inputs[mapIndex].completeType != input.stagedSource->completeType
            || method->inputs[mapIndex].handoffKind != ROOTPIN_METHOD_HANDOFF_OWNED)
        {
            ContractViolation();
        }
        previousMapIndex = mapIndex;
        hasPreviousMapIndex = true;

        for (uint32_t previous = 0; previous < index; previous++)
        {
            if (transfer->inputs[previous].stagedSource == input.stagedSource)
            {
                ContractViolation();
            }
        }
    }

    transfer->previous = s_threadState.callTop;
    transfer->state = 1;
    s_threadState.callTop = transfer;
}

void ROOTPIN_LOADED_TYPES_CALL ReturnSupportEntry(
    RootPinDomainV1 domain,
    RootPinSupportSlotV1* savedResult)
{
    if (domain != &s_domain || savedResult == nullptr
        || reinterpret_cast<uintptr_t>(savedResult) % alignof(RootPinSupportSlotV1) != 0)
    {
        ContractViolation();
    }

    ExclusiveLock lock(&domain->gate);
    auto* frame = s_threadState.frameTop;
    auto* call = s_threadState.callTop;
    if (frame == nullptr || call == nullptr || call->state != 2
        || frame->entryMethod != call->target || call->prospectiveResult == nullptr
        || !SlotBelongsToFrame(frame, savedResult)
        || savedResult->live != 1 || savedResult->ownsHold != 1
        || savedResult->completeType == nullptr
        || savedResult->completeType != RequireLoadedMethod(domain, call->target)->resultType)
    {
        ContractViolation();
    }

    auto* result = call->prospectiveResult;
    if (result->residence == nullptr || result->live != 0 || result->ownsHold != 0
        || result->completeType != savedResult->completeType
        || std::memcmp(result->residence, savedResult->residence,
            static_cast<size_t>(savedResult->completeType->size)) != 0)
    {
        ContractViolation();
    }
    WalkSupportValueLocked(domain, savedResult->completeType,
        static_cast<const uint8_t*>(savedResult->residence),
        SupportAction::ValidateRelease, nullptr, false);

    savedResult->live = 0;
    savedResult->ownsHold = 0;
    result->live = 1;
    result->ownsHold = 1;
    call->state = 3;
}

void ROOTPIN_LOADED_TYPES_CALL FinishCallEntry(
    RootPinDomainV1 domain,
    RootPinCallTransferV1* transfer,
    uint32_t completion)
{
    if (domain != &s_domain || transfer == nullptr || completion > 1
        || reinterpret_cast<uintptr_t>(transfer) % alignof(RootPinCallTransferV1) != 0)
    {
        ContractViolation();
    }

    ExclusiveLock lock(&domain->gate);
    if (s_threadState.callTop != transfer || transfer->state < 1 || transfer->state > 3)
    {
        ContractViolation();
    }

    auto* method = RequireLoadedMethod(domain, transfer->target);
    const bool resultNeedsSupport = method->resultType != nullptr
        && ContainsSupport(method->resultType);
    if (completion == 1
        && (transfer->state != (resultNeedsSupport ? 3u : 2u)
            || (resultNeedsSupport
                && (transfer->prospectiveResult == nullptr
                    || transfer->prospectiveResult->live != 1
                    || transfer->prospectiveResult->ownsHold != 1))))
    {
        ContractViolation();
    }

    if (completion == 1)
    {
        for (uint32_t index = 0; index < transfer->inputCount; index++)
        {
            const auto* source = transfer->inputs[index].stagedSource;
            if (source->live != 0 || source->ownsHold != 0)
            {
                ContractViolation();
            }
        }
        if (resultNeedsSupport)
        {
            const auto* result = transfer->prospectiveResult;
            WalkSupportValueLocked(domain, result->completeType,
                static_cast<const uint8_t*>(result->residence),
                SupportAction::ValidateRelease, nullptr, false);
        }
    }

    if (completion == 0)
    {
        if (transfer->state == 1)
        {
            for (uint32_t index = transfer->inputCount; index > 0; index--)
            {
                auto* source = transfer->inputs[index - 1].stagedSource;
                if (source->live != 1 || source->ownsHold != 1)
                {
                    ContractViolation();
                }
                ReleaseSupportLocked(domain, source->completeType, source->residence);
                source->live = 0;
                source->ownsHold = 0;
            }
        }
        else
        {
            for (uint32_t index = 0; index < transfer->inputCount; index++)
            {
                auto* source = transfer->inputs[index].stagedSource;
                if (source->live != 0 || source->ownsHold != 0)
                {
                    ContractViolation();
                }
            }
        }

        if (transfer->prospectiveResult != nullptr)
        {
            auto* result = transfer->prospectiveResult;
            if (transfer->state == 3)
            {
                if (result->live != 1 || result->ownsHold != 1)
                {
                    ContractViolation();
                }
                ReleaseSupportLocked(domain, result->completeType, result->residence);
                result->live = 0;
                result->ownsHold = 0;
            }
            else if (result->live != 0 || result->ownsHold != 0)
            {
                ContractViolation();
            }
        }
    }

    auto* previous = transfer->previous;
    transfer->previous = nullptr;
    transfer->state = 4;
    s_threadState.callTop = previous;
}

void ROOTPIN_LOADED_TYPES_CALL RetainSupportEntry(
    RootPinDomainV1 domain,
    RootPinLoadedTypeV1* completeType,
    const void* value)
{
    if (domain != &s_domain)
    {
        ContractViolation();
    }

    ExclusiveLock lock(&domain->gate);
    RetainSupportLocked(domain, completeType, value);
}

void ROOTPIN_LOADED_TYPES_CALL ReleaseSupportEntry(
    RootPinDomainV1 domain,
    RootPinLoadedTypeV1* completeType,
    const void* value)
{
    if (domain != &s_domain)
    {
        ContractViolation();
    }

    ExclusiveLock lock(&domain->gate);
    ReleaseSupportLocked(domain, completeType, value);
}

const RootPinRuntimeInfoV1 s_runtimeInfo = {
    &s_domain,
    {
        1,
        sizeof(RootPinRuntimeFunctionTableV1),
        RootExactEntry,
        PinExactEntry,
        PinObjectEntry,
        ResolveExactEntry,
        RetainSupportEntry,
        ReleaseSupportEntry,
        EnterFrameEntry,
        ExitFrameEntry,
        PollEntry,
        BeginCallEntry,
        ReturnSupportEntry,
        FinishCallEntry,
    },
};
}

#if defined(TERRUM_ROOT_PIN_TESTS)
extern "C" ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestEnterFrameV1(RootPinDomainV1 domain, RootPinSupportFrameV1* frame)
{
    EnterFrameEntry(domain, frame);
}

extern "C" ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestExitFrameV1(RootPinDomainV1 domain, RootPinSupportFrameV1* frame)
{
    ExitFrameEntry(domain, frame);
}

extern "C" ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestPollV1(RootPinDomainV1 domain)
{
    PollEntry(domain);
}

extern "C" ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestBeginCallV1(RootPinDomainV1 domain, RootPinCallTransferV1* transfer)
{
    BeginCallEntry(domain, transfer);
}

extern "C" ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestReturnSupportV1(
    RootPinDomainV1 domain,
    RootPinSupportSlotV1* savedResult)
{
    ReturnSupportEntry(domain, savedResult);
}

extern "C" ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestFinishCallV1(
    RootPinDomainV1 domain,
    RootPinCallTransferV1* transfer,
    uint32_t completion)
{
    FinishCallEntry(domain, transfer, completion);
}

extern "C" ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestTraceCurrentFramesV1(
    RootPinDomainV1 domain,
    RootPinObjectCellV1* objects,
    uint32_t capacity,
    uint32_t* outCount)
{
    if (domain != &s_domain || outCount == nullptr
        || reinterpret_cast<uintptr_t>(outCount) % alignof(uint32_t) != 0)
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }

    MemoryRange objectsRange;
    if (!ValidArray(objects, capacity, sizeof(*objects), alignof(RootPinObjectCellV1),
            objectsRange))
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }

    try
    {
        ExclusiveLock lock(&domain->gate);
        ObjectTraceQueue queue = { NextTraceEpochLocked(domain), nullptr, nullptr, 0 };
        TraceCurrentFramesLocked(domain, queue);
        if (queue.count > std::numeric_limits<uint32_t>::max()
            || queue.count > capacity)
        {
            return ROOTPIN_TYPE_STATUS_EXHAUSTED;
        }

        *outCount = static_cast<uint32_t>(queue.count);
        uint32_t index = 0;
        for (auto object = queue.first; object != nullptr; object = object->traceNext)
        {
            objects[index++] = object;
        }
        return ROOTPIN_TYPE_STATUS_SUCCESS;
    }
    catch (const std::bad_alloc&)
    {
        return ROOTPIN_TYPE_STATUS_EXHAUSTED;
    }
    catch (const std::length_error&)
    {
        return ROOTPIN_TYPE_STATUS_EXHAUSTED;
    }
}

extern "C" ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestTraceAllFramesV1(
    RootPinDomainV1 domain,
    RootPinObjectCellV1* objects,
    uint32_t capacity,
    uint32_t* outCount)
{
    if (domain != &s_domain || outCount == nullptr
        || reinterpret_cast<uintptr_t>(outCount) % alignof(uint32_t) != 0)
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }

    MemoryRange objectsRange;
    if (!ValidArray(objects, capacity, sizeof(*objects), alignof(RootPinObjectCellV1),
            objectsRange))
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }

    try
    {
        StopTheWorld stop(domain);
        ExclusiveLock lock(&domain->gate, false);
        ObjectTraceQueue queue = { NextTraceEpochLocked(domain), nullptr, nullptr, 0 };
        TraceAllFramesLocked(domain, queue);
        if (queue.count > std::numeric_limits<uint32_t>::max()
            || queue.count > capacity)
        {
            return ROOTPIN_TYPE_STATUS_EXHAUSTED;
        }

        *outCount = static_cast<uint32_t>(queue.count);
        uint32_t index = 0;
        for (auto object = queue.first; object != nullptr; object = object->traceNext)
        {
            objects[index++] = object;
        }
        return ROOTPIN_TYPE_STATUS_SUCCESS;
    }
    catch (const std::bad_alloc&)
    {
        return ROOTPIN_TYPE_STATUS_EXHAUSTED;
    }
    catch (const std::length_error&)
    {
        return ROOTPIN_TYPE_STATUS_EXHAUSTED;
    }
}

extern "C" ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestRelocateReachableV1(
    RootPinDomainV1 domain,
    uint32_t* outMovedCount)
{
    if (domain != &s_domain || outMovedCount == nullptr
        || reinterpret_cast<uintptr_t>(outMovedCount) % alignof(uint32_t) != 0)
    {
        return ROOTPIN_TYPE_STATUS_INVALID;
    }

    *outMovedCount = 0;
    const auto failOrdinal = s_relocationFailureOrdinal.exchange(0);
    struct Relocation
    {
        RootPinObjectCellV1 object;
        uint8_t* previousBase;
        uint8_t* destinationBase;
        bool previousBaseOwned;
    };

    try
    {
        StopTheWorld stop(domain);
        ExclusiveLock lock(&domain->gate, false);
        ObjectTraceQueue queue = { NextTraceEpochLocked(domain), nullptr, nullptr, 0 };
        TraceAllFramesLocked(domain, queue);
        if (queue.count > std::numeric_limits<uint32_t>::max())
        {
            return ROOTPIN_TYPE_STATUS_EXHAUSTED;
        }

        std::vector<Relocation> relocations;
        if (queue.count > relocations.max_size())
        {
            return ROOTPIN_TYPE_STATUS_EXHAUSTED;
        }
        relocations.reserve(static_cast<size_t>(queue.count));

        const auto discardStaged = [&relocations]() {
            for (const auto& relocation : relocations)
            {
                _aligned_free(relocation.destinationBase);
            }
        };

        uint32_t allocationOrdinal = 0;
        for (auto* object = queue.first; object != nullptr; object = object->traceNext)
        {
            if (object->activePins != 0 || object->payloadSize == 0)
            {
                continue;
            }
            if (object->currentBase == nullptr || object->alignment == 0
                || !IsPowerOfTwo(object->alignment))
            {
                ContractViolation();
            }
            if (allocationOrdinal == std::numeric_limits<uint32_t>::max())
            {
                discardStaged();
                return ROOTPIN_TYPE_STATUS_EXHAUSTED;
            }
            allocationOrdinal++;
            if (failOrdinal == allocationOrdinal)
            {
                discardStaged();
                return ROOTPIN_TYPE_STATUS_EXHAUSTED;
            }

            const auto alignment = std::max<size_t>(
                static_cast<size_t>(object->alignment), alignof(void*));
            auto* destination = static_cast<uint8_t*>(
                _aligned_malloc(static_cast<size_t>(object->payloadSize), alignment));
            if (destination == nullptr)
            {
                discardStaged();
                return ROOTPIN_TYPE_STATUS_EXHAUSTED;
            }
            std::memcpy(destination, object->currentBase,
                static_cast<size_t>(object->payloadSize));
            relocations.push_back({
                object, object->currentBase, destination, object->currentBaseOwned });
        }

        for (const auto& relocation : relocations)
        {
            relocation.object->currentBase = relocation.destinationBase;
            relocation.object->currentBaseOwned = true;
        }
        for (const auto& relocation : relocations)
        {
            if (relocation.previousBaseOwned)
            {
                _aligned_free(relocation.previousBase);
            }
        }

        *outMovedCount = static_cast<uint32_t>(relocations.size());
        return ROOTPIN_TYPE_STATUS_SUCCESS;
    }
    catch (const std::bad_alloc&)
    {
        return ROOTPIN_TYPE_STATUS_EXHAUSTED;
    }
    catch (const std::length_error&)
    {
        return ROOTPIN_TYPE_STATUS_EXHAUSTED;
    }
}

#endif

extern "C" ROOTPIN_LOADED_TYPES_API const RootPinRuntimeInfoV1*
ROOTPIN_LOADED_TYPES_CALL TerrumRootPinGetRuntimeV1(void)
{
    return &s_runtimeInfo;
}
