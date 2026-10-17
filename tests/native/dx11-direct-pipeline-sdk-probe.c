#include "dx11-direct-shader-sdk-probe.c"

ASSERT_SIZE(direct_pipeline_viewport_size, D3D11_VIEWPORT, 24);
ASSERT_ALIGNMENT(direct_pipeline_viewport_alignment, D3D11_VIEWPORT, 4);
ASSERT_OFFSET(direct_pipeline_viewport_x, D3D11_VIEWPORT, TopLeftX, 0);
ASSERT_OFFSET(direct_pipeline_viewport_y, D3D11_VIEWPORT, TopLeftY, 4);
ASSERT_OFFSET(direct_pipeline_viewport_width, D3D11_VIEWPORT, Width, 8);
ASSERT_OFFSET(direct_pipeline_viewport_height, D3D11_VIEWPORT, Height, 12);
ASSERT_OFFSET(direct_pipeline_viewport_min, D3D11_VIEWPORT, MinDepth, 16);
ASSERT_OFFSET(direct_pipeline_viewport_max, D3D11_VIEWPORT, MaxDepth, 20);
ASSERT_OFFSET(direct_pipeline_ps_slot, ID3D11DeviceContextVtbl, PSSetShader, 72);
ASSERT_OFFSET(direct_pipeline_vs_slot, ID3D11DeviceContextVtbl, VSSetShader, 88);
ASSERT_OFFSET(direct_pipeline_draw_slot, ID3D11DeviceContextVtbl, Draw, 104);
ASSERT_OFFSET(direct_pipeline_layout_slot, ID3D11DeviceContextVtbl, IASetInputLayout, 136);
ASSERT_OFFSET(direct_pipeline_buffer_slot, ID3D11DeviceContextVtbl, IASetVertexBuffers, 144);
ASSERT_OFFSET(direct_pipeline_topology_slot, ID3D11DeviceContextVtbl, IASetPrimitiveTopology, 192);
ASSERT_OFFSET(direct_pipeline_target_slot, ID3D11DeviceContextVtbl, OMSetRenderTargets, 264);
ASSERT_OFFSET(direct_pipeline_viewport_slot, ID3D11DeviceContextVtbl, RSSetViewports, 352);
ASSERT_OFFSET(direct_pipeline_get_viewports_slot, ID3D11DeviceContextVtbl, RSGetViewports, 760);
ASSERT_OFFSET(direct_pipeline_clear_slot, ID3D11DeviceContextVtbl, ClearState, 880);

void NativeFfiDirectPipelineSdkSignatures(const ID3D11DeviceContextVtbl* context)
{
    void (STDMETHODCALLTYPE *ps)(ID3D11DeviceContext*, ID3D11PixelShader*,
        ID3D11ClassInstance* const*, UINT) = context->PSSetShader;
    void (STDMETHODCALLTYPE *vs)(ID3D11DeviceContext*, ID3D11VertexShader*,
        ID3D11ClassInstance* const*, UINT) = context->VSSetShader;
    void (STDMETHODCALLTYPE *draw)(ID3D11DeviceContext*, UINT, UINT) = context->Draw;
    void (STDMETHODCALLTYPE *layout)(ID3D11DeviceContext*, ID3D11InputLayout*) = context->IASetInputLayout;
    void (STDMETHODCALLTYPE *buffers)(ID3D11DeviceContext*, UINT, UINT, ID3D11Buffer* const*,
        const UINT*, const UINT*) = context->IASetVertexBuffers;
    void (STDMETHODCALLTYPE *topology)(ID3D11DeviceContext*, D3D11_PRIMITIVE_TOPOLOGY) = context->IASetPrimitiveTopology;
    void (STDMETHODCALLTYPE *targets)(ID3D11DeviceContext*, UINT, ID3D11RenderTargetView* const*,
        ID3D11DepthStencilView*) = context->OMSetRenderTargets;
    void (STDMETHODCALLTYPE *viewports)(ID3D11DeviceContext*, UINT, const D3D11_VIEWPORT*) = context->RSSetViewports;
    void (STDMETHODCALLTYPE *getViewports)(ID3D11DeviceContext*, UINT*, D3D11_VIEWPORT*) =
        context->RSGetViewports;
    void (STDMETHODCALLTYPE *clear)(ID3D11DeviceContext*) = context->ClearState;
    (void)ps; (void)vs; (void)draw; (void)layout; (void)buffers; (void)topology;
    (void)targets; (void)viewports; (void)getViewports; (void)clear;
}
