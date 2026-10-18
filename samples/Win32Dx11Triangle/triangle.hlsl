float4 VertexMain(uint vertexId : SV_VertexID) : SV_POSITION
{
    float2 position = vertexId == 0 ? float2(0.0f, 0.6f)
        : vertexId == 1 ? float2(0.6f, -0.5f) : float2(-0.6f, -0.5f);
    return float4(position, 0.5f, 1.0f);
}

float4 PixelMain() : SV_TARGET
{
    return float4(0.1f, 0.8f, 0.9f, 1.0f);
}
