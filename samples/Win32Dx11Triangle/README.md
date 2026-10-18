# Win32 + Direct3D 11 triangle

[WARP source](main.terrum) | [hardware source](main-hardware.terrum) | [HLSL](triangle.hlsl) | [sample index](../../docs/samples.md)

This sample renders a triangle from Terrum source without a C application
bridge. `main.terrum` requests WARP; `main-hardware.terrum` requests hardware
directly and does not fall back to WARP. Both author window creation, shader
loading, rendering, resize handling, message processing, and cleanup in source.
The vertex shader uses `SV_VertexID`, so the render path needs no input layout
or vertex buffer.
Build outputs are kept under
`artifacts\bin\samples\Win32Dx11Triangle\<configuration>`.

## Build and run

Build the compiler, then build either launcher-compatible executable. The
script copies `triangle.hlsl` beside the output and does not start a window.

```powershell
dotnet build sources\terrumc\terrumc.csproj
& samples\Win32Dx11Triangle\Build-Dx11Triangle.ps1 -Warp
& samples\Win32Dx11Triangle\Build-Dx11Triangle.ps1
```

Without `-Warp`, the build script selects direct hardware. `-Configuration
Release` selects the Release compiler. Launch with the matching driver option;
`-WhatIf` prints the selected executable without starting it:

```powershell
& samples\Win32Dx11Triangle\Start-Dx11Triangle.ps1 -Warp -WhatIf
& samples\Win32Dx11Triangle\Start-Dx11Triangle.ps1 -WhatIf
```

Pass the same `-Configuration` to the start script when launching a Release
build.

The build script selects `checked-exceptions`; ordinary tests also emit both
entries under `fail-fast`, without establishing failure-path execution.
Both entries require Windows x64 and the D3D11 runtime, and load
`triangle.hlsl` from beside the executable. Normal shutdown
clears context bindings before releasing COM resources and destroys the window
before unregistering its class.

The source uses the implemented legacy `bool`/`out` ownership-output profile;
these signatures are not canonical target `init ref<T>` declarations.
Ordinary CLI tests bind and emit both entry points without opening a window.
Window execution is separately gated and is not implied by emission.

## Compiler boundary

The compiler lowers named native imports, committed layouts, and ABI-level
vtable calls through generic contracts. It has no Win32 or DX11 API identities
and no COM ownership or acquisition policy. `GetClientRect` is declared in
[`win32-api.terrum`](win32-api.terrum) as `int32(ptr<void>, ptr<ClientRectangle>)`;
the other Windows imports and the DX11 layouts, slots, signatures, and
ordinary unique/Cleanup wrappers are authored in the API source files.

The Windows FFI and vtable source/metadata contracts remain **UNAPPROVED**.
This sample is implementation evidence, not acceptance of those contracts.