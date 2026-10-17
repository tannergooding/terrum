#include <d3d11.h>
#include <dxgi.h>
#include <type_traits>

static_assert(std::is_base_of_v<IUnknown, IDXGIObject>);
static_assert(std::is_base_of_v<IDXGIObject, IDXGIDeviceSubObject>);
static_assert(std::is_base_of_v<IDXGIDeviceSubObject, IDXGISwapChain>);
static_assert(std::is_base_of_v<IUnknown, ID3D11Device>);
static_assert(std::is_base_of_v<IUnknown, ID3D11DeviceChild>);
static_assert(std::is_base_of_v<ID3D11DeviceChild, ID3D11DeviceContext>);
static_assert(std::is_base_of_v<ID3D11DeviceChild, ID3D11Resource>);
static_assert(std::is_base_of_v<ID3D11Resource, ID3D11Buffer>);
static_assert(std::is_base_of_v<ID3D11Resource, ID3D11Texture2D>);
static_assert(std::is_base_of_v<ID3D11DeviceChild, ID3D11View>);
static_assert(std::is_base_of_v<ID3D11View, ID3D11RenderTargetView>);
static_assert(std::is_base_of_v<ID3D11DeviceChild, ID3D11InputLayout>);
static_assert(std::is_base_of_v<ID3D11DeviceChild, ID3D11VertexShader>);
static_assert(std::is_base_of_v<ID3D11DeviceChild, ID3D11PixelShader>);
static_assert(std::is_base_of_v<IUnknown, ID3D10Blob>);
static_assert(sizeof(ID3D11Texture2D*) == sizeof(ID3D11Resource*));

ID3D11Resource* NativeFfiCheckTextureResourceBaseConversion(ID3D11Texture2D* texture)
{
    return texture;
}
