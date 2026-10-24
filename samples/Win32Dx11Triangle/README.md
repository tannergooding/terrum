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

## Window and resize behavior

The window procedure posts a target-window `WM_APP` marker for `WM_SIZE` and
does not call DXGI or D3D11. The render thread drains the queue, coalesces
resize markers to the latest minimized state, and tracks `WM_PAINT` redraws
separately. It obtains one authoritative `GetClientRect` snapshot after the
queue drain; zero/minimized extents do no rendering, and only a changed
positive extent calls `ResizeBuffers`. A paint at the existing positive
extent redraws without resizing.

For each frame, the sample clears context bindings before releasing or
replacing surface owners, acquires the current back buffer and render target,
sets the viewport from the same client rectangle, binds pipeline state, draws
and presents. Per-frame cleanup clears bindings before the render-target and
back-buffer owners retire. Quit takes precedence over pending resize/redraw;
the callback's BOOL result, `GetMessageW`'s positive/zero/negative result and
DXGI HRESULT success are tested according to their distinct contracts. Post
failure preserves failure precedence over a previously queued normal quit.
These are application behaviors, not compiler-recognized DX11 rules.

## Image evidence boundary

The `SV_VertexID` path has no vertex buffer or input layout. If an opt-in
readback check is added, it must validate row pitch, complete extent and
padding before examining active RGBA8 pixels; Map failure must not be unmapped,
and a successful Map must be unmapped exactly once. Current evidence may check
the clear/shader color domain with interior and exterior witnesses, but this sample currently claims no image-readback
qualification. Any future check must not claim an exact cross-host coverage
mask or natural exposure/scanout behavior. The optional historical
POSITION/input-layout fixture is not part of the maintained shader path.

## Compiler boundary

The compiler lowers named native imports, committed layouts, and ABI-level
vtable calls through generic contracts. It has no Win32 or DX11 API identities
and no COM ownership or acquisition policy. `GetClientRect` is declared in
[`win32-api.terrum`](win32-api.terrum) as `int32(ptr<void>, ptr<ClientRectangle>)`;
the other Windows imports and the DX11 layouts, slots, signatures, and
ordinary unique/Cleanup wrappers are authored in the API source files.

The generic native declaration, vtable, target-ABI and layout contracts are
accepted in their canonical owners. This sample still uses prototype
`NativeParameter`, `NativeOutput` and `[NoThrow]` forms and is not conformance
evidence for the current metadata schema or source grammar.