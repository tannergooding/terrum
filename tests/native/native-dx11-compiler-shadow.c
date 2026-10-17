#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <stdio.h>

#pragma comment(linker, "/export:D3DCompileFromFile=NativeFfiShadowCompiler")

HRESULT WINAPI NativeFfiShadowCompiler(LPCWSTR path,
    const D3D_SHADER_MACRO* defines, ID3DInclude* include, LPCSTR entry, LPCSTR target,
    UINT flags1, UINT flags2, ID3DBlob** code, ID3DBlob** diagnostics)
{
    (void)path;
    (void)defines;
    (void)include;
    (void)entry;
    (void)target;
    (void)flags1;
    (void)flags2;
    *code = NULL;
    *diagnostics = NULL;
    fputs("APP_LOCAL_COMPILER_EXECUTED\n", stderr);
    return E_ACCESSDENIED;
}
