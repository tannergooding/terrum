#include "dx11-direct-acquisition-sdk-probe.c"

typedef struct DirectBufferVertex
{
    float X;
    float Y;
    float Z;
} DirectBufferVertex;

ASSERT_SIZE(direct_buffer_vertex_size, DirectBufferVertex, 12);
ASSERT_ALIGNMENT(direct_buffer_vertex_alignment, DirectBufferVertex, 4);
ASSERT_OFFSET(direct_buffer_vertex_x, DirectBufferVertex, X, 0);
ASSERT_OFFSET(direct_buffer_vertex_y, DirectBufferVertex, Y, 4);
ASSERT_OFFSET(direct_buffer_vertex_z, DirectBufferVertex, Z, 8);
typedef char direct_buffer_payload_size[(sizeof(DirectBufferVertex[3]) == 36) ? 1 : -1];
