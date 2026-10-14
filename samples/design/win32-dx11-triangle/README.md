# Win32 + Direct3D 11 triangle

[WARP source](main.terrum) | [hardware source](main-hardware.terrum) | [HLSL](triangle.hlsl) | [sample index](../../../docs/samples.md)

This sample renders a triangle from Terrum source without a C application
bridge. `main.terrum` requests WARP; `main-hardware.terrum` requests hardware
directly and does not fall back to WARP. Both author window creation, shader
loading, rendering, resize handling, message processing, and cleanup in source.
The vertex shader uses `SV_VertexID`, so the render path needs no input layout
or vertex buffer.

## Build and run

Build the compiler, then build either launcher-compatible executable. The
script copies `triangle.hlsl` beside the output and does not start a window.

```powershell
dotnet build sources\terrumc\terrumc.csproj
& samples\design\win32-dx11-triangle\Build-Dx11Triangle.ps1 -Warp
& samples\design\win32-dx11-triangle\Build-Dx11Triangle.ps1
```

Without `-Warp`, the build script selects direct hardware. `-Configuration
Release` selects the Release compiler. Launch with the matching driver option;
`-WhatIf` prints the selected executable without starting it:

```powershell
& samples\design\win32-dx11-triangle\Start-Dx11Triangle.ps1 -Warp -WhatIf
& samples\design\win32-dx11-triangle\Start-Dx11Triangle.ps1 -WhatIf
```

Both entry points use `checked-exceptions`, require Windows x64 and the D3D11
runtime, and load `triangle.hlsl` from beside the executable. Normal shutdown
clears context bindings before releasing COM resources and destroys the window
before unregistering its class.

`device-buffer-stage.terrum` is a separate generic storage-record and COM
`CreateBuffer` example. Older stage and evidence sources in this directory are
historical prototypes; they are not maintained entry points and may depend on
compiler features that have been retired.

## Compiler boundary

The compiler lowers native imports, storage records, COM ownership, and vtable
calls through generic contracts. DX11-specific imports, COM signatures, and
layouts remain data in the selected Windows x64 catalog; the compiler no longer
has DX11 primitives, intrinsics, source gates, verifier cases, or emitter
branches. The Windows FFI, COM, and ownership proposals remain **UNAPPROVED**;
this sample is implementation evidence, not acceptance of those contracts.

The [helper API sketch](interop.api.terrum) is not an input to these builds.