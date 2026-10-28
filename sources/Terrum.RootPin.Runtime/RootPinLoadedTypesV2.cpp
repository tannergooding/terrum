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
static_assert(sizeof(RootPinPlaceRequestV1) == 40);
static_assert(sizeof(RootPinStableValue16V1) == 16);
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
        std::unordered_map<uint64_t, Origin>& origins)
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

        std::vector<uint8_t> containsSupport(batch.nodeCount);
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
    RootPinLoadedTypeV1* child;
    std::vector<Field> fields;
    std::vector<uint64_t> origins;
};

struct RootPinObjectCellImpl
{
    RootPinDomainV1 domain;
    uint8_t* currentBase;
    uint64_t payloadSize;
    uint64_t alignment;
    uint64_t generation;
    uint64_t rootEdges;
    uint64_t activePins;
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
    size_t arenaChunkIndex;
    bool wholeObject;
    bool active;
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
    SRWLOCK gate = SRWLOCK_INIT;
    std::unordered_map<TypeKey, RootPinLoadedTypeV1*, TypeKeyHash> types;
    std::unordered_map<uint64_t, Origin> origins;
    std::vector<std::unique_ptr<RootPinLoadedTypeV1>> ownedTypes;
    std::unordered_set<RootPinObjectCellV1> objects;
    std::unordered_set<RootPinRootCellV1> roots;
    std::unordered_set<RootPinPinCellV1> pins;
    std::vector<std::unique_ptr<RootPinObjectCellImpl>> ownedObjects;
    std::vector<CellChunk> cellChunks;
    uint64_t nextObjectGeneration = 1;
    uint64_t nextCellIdentity = 1;
};

namespace
{
    class ExclusiveLock
    {
    public:
        explicit ExclusiveLock(SRWLOCK* gate) noexcept
            : _gate(gate)
        {
            AcquireSRWLockExclusive(_gate);
        }

        ~ExclusiveLock()
        {
            ReleaseSRWLockExclusive(_gate);
        }

        ExclusiveLock(const ExclusiveLock&) = delete;
        ExclusiveLock& operator=(const ExclusiveLock&) = delete;

    private:
        SRWLOCK* _gate;
    };

    [[noreturn]] void ContractViolation() noexcept;

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
        const std::vector<std::vector<uint64_t>>& originKeys)
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
        if (domain == nullptr || type == nullptr || type->domain != domain || type->key.role != role)
        {
            return false;
        }

        const auto found = domain->types.find(type->key);
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
        if (!ValidateGraph(*batch, originKeys, batchOrigins))
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

            auto loaded = CreateLoadedType(domain, *batch, index, originKeys);
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
    object->currentBase = static_cast<uint8_t*>(payload);
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

#endif
