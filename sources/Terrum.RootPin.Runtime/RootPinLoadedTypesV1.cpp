#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include "RootPinLoadedTypesV1.h"

#include <algorithm>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>
#include <memory>
#include <new>
#include <stdexcept>
#include <unordered_map>
#include <utility>
#include <vector>

static_assert(sizeof(void*) == 8);
static_assert(sizeof(RootPinTypeNodeV1) == 80);
static_assert(sizeof(RootPinTypeFieldV1) == 32);
static_assert(sizeof(RootPinTypeOriginV1) == 40);
static_assert(sizeof(RootPinTypeBatchV1) == 72);
static_assert(offsetof(RootPinTypeNodeV1, role) == 8);
static_assert(offsetof(RootPinTypeNodeV1, shape) == 12);
static_assert(offsetof(RootPinTypeNodeV1, size) == 16);
static_assert(offsetof(RootPinTypeNodeV1, alignment) == 24);
static_assert(offsetof(RootPinTypeNodeV1, retirementKey) == 32);
static_assert(offsetof(RootPinTypeNodeV1, child) == 40);
static_assert(offsetof(RootPinTypeNodeV1, fieldFirst) == 44);
static_assert(offsetof(RootPinTypeNodeV1, fieldCount) == 48);
static_assert(offsetof(RootPinTypeNodeV1, originFirst) == 52);
static_assert(offsetof(RootPinTypeNodeV1, originCount) == 56);
static_assert(offsetof(RootPinTypeNodeV1, retirementOrigin) == 60);
static_assert(offsetof(RootPinTypeNodeV1, retireGcPayloadThunk) == 64);
static_assert(offsetof(RootPinTypeNodeV1, layoutKind) == 72);
static_assert(offsetof(RootPinTypeNodeV1, flags) == 76);
static_assert(offsetof(RootPinTypeFieldV1, fieldKey) == 0);
static_assert(offsetof(RootPinTypeFieldV1, offset) == 8);
static_assert(offsetof(RootPinTypeFieldV1, knownAlignment) == 16);
static_assert(offsetof(RootPinTypeFieldV1, valueNode) == 24);
static_assert(offsetof(RootPinTypeOriginV1, originKey) == 0);
static_assert(offsetof(RootPinTypeOriginV1, cookie) == 8);
static_assert(offsetof(RootPinTypeOriginV1, retainOrigin) == 16);
static_assert(offsetof(RootPinTypeOriginV1, releaseOrigin) == 24);
static_assert(offsetof(RootPinTypeOriginV1, kind) == 32);
static_assert(offsetof(RootPinTypeBatchV1, abiRevision) == 0);
static_assert(offsetof(RootPinTypeBatchV1, targetProfileId) == 8);
static_assert(offsetof(RootPinTypeBatchV1, nodeCount) == 16);
static_assert(offsetof(RootPinTypeBatchV1, nodes) == 32);
static_assert(offsetof(RootPinTypeBatchV1, fields) == 40);
static_assert(offsetof(RootPinTypeBatchV1, origins) == 48);
static_assert(offsetof(RootPinTypeBatchV1, originUses) == 56);
static_assert(offsetof(RootPinTypeBatchV1, flags) == 64);

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

    uint64_t ChildKey(const RootPinTypeBatchV1& batch, uint32_t index)
    {
        return index == ROOTPIN_TYPE_NO_INDEX ? 0 : batch.nodes[index].typeKey;
    }

    uint32_t ChildRole(const RootPinTypeBatchV1& batch, uint32_t index)
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
        const RootPinTypeBatchV1& batch,
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
        const RootPinTypeBatchV1& batch,
        uint32_t inputIndex,
        const std::vector<std::vector<uint64_t>>& originKeys,
        const RootPinLoadedTypeV1& loaded);

    bool ValidateNodeShapes(const RootPinTypeBatchV1& batch)
    {
        for (uint32_t index = 0; index < batch.nodeCount; index++)
        {
            const auto& node = batch.nodes[index];
            if (node.typeKey == 0
                || (node.role != ROOTPIN_TYPE_ROLE_VALUE
                    && node.role != ROOTPIN_TYPE_ROLE_GC_PAYLOAD)
                || node.shape < ROOTPIN_TYPE_SHAPE_LEAF
                || node.shape > ROOTPIN_TYPE_SHAPE_BOX_PAYLOAD
                || !HasValidAlignment(node.size, node.alignment))
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

            const RootPinTypeNodeV1* child = node.child == ROOTPIN_TYPE_NO_INDEX
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
                default:
                    return false;
            }
        }

        return true;
    }

    bool ValidateStorageAcyclic(const RootPinTypeBatchV1& batch)
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
                || node.shape == ROOTPIN_TYPE_SHAPE_BOX_PAYLOAD)
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
                || node.shape == ROOTPIN_TYPE_SHAPE_BOX_PAYLOAD)
                && --indegree[node.child] == 0)
            {
                ready.push_back(node.child);
            }
        }

        return visited == batch.nodeCount;
    }

    bool ValidateFields(
        const RootPinTypeBatchV1& batch,
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
        const RootPinTypeBatchV1& batch,
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
    TypeKey key;
    uint32_t shape;
    uint64_t size;
    uint64_t alignment;
    uint64_t retirementKey;
    uint64_t retirementThunk;
    uint64_t retirementOriginKey;
    uint32_t layoutKind;
    uint32_t flags;
    RootPinLoadedTypeV1* child;
    std::vector<Field> fields;
    std::vector<uint64_t> origins;
};

struct RootPinDomainImpl
{
    SRWLOCK gate = SRWLOCK_INIT;
    std::unordered_map<TypeKey, RootPinLoadedTypeV1*, TypeKeyHash> types;
    std::unordered_map<uint64_t, Origin> origins;
    std::vector<std::unique_ptr<RootPinLoadedTypeV1>> ownedTypes;
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
        const RootPinTypeBatchV1& batch,
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
        const RootPinTypeBatchV1& batch,
        uint32_t index,
        const std::vector<std::vector<uint64_t>>& originKeys)
    {
        const auto& node = batch.nodes[index];
        auto loaded = std::make_unique<RootPinLoadedTypeV1>();
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
        loaded->child = nullptr;
        loaded->origins = originKeys[index];
        loaded->fields.reserve(node.fieldCount);
        return loaded;
    }
}

extern "C" ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
TerrumRootPinAdmitLoadedTypesV1(
    RootPinDomainV1 domain,
    const RootPinTypeBatchV1* batch,
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
    if (reinterpret_cast<uintptr_t>(batch) % alignof(RootPinTypeBatchV1) != 0
        || !GetMemoryRange(batch, sizeof(*batch), batchRange)
        || Overlaps(outputRange, batchRange)
        || !ValidArray(batch->nodes, batch->nodeCount, sizeof(*batch->nodes),
            alignof(RootPinTypeNodeV1), nodesRange)
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
        if (batch->abiRevision != 1
            || batch->targetProfileId != 2
            || batch->targetProfileRevision != 1)
        {
            return ROOTPIN_TYPE_STATUS_UNSUPPORTED;
        }
        if (batch->byteSize != sizeof(RootPinTypeBatchV1)
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

            auto loaded = CreateLoadedType(*batch, index, originKeys);
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
RootPinTestFailAdmissionAtStageV1(uint32_t stage)
{
    s_failureStage.store(stage);
}
#endif
