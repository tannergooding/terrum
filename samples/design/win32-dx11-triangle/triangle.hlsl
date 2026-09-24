float4 VertexMain(float3 position : POSITION) : SV_POSITION
{
    return float4(position, 1.0f);
}

float4 PixelMain() : SV_TARGET
{
    return float4(0.1f, 0.8f, 0.9f, 1.0f);
}
