#include <stddef.h>
#include <windows.h>
#include <dxgi.h>
#include <d3d11.h>

#ifndef _WIN64
#error This probe requires the Windows x64 ABI.
#endif

#define ASSERT_SIZE(name, type, expected) \
    typedef char name[(sizeof(type) == (expected)) ? 1 : -1]
#define ASSERT_ALIGNMENT(name, type, expected) \
    typedef char name[(__alignof(type) == (expected)) ? 1 : -1]
#define ASSERT_OFFSET(name, type, member, expected) \
    typedef char name[(offsetof(type, member) == (expected)) ? 1 : -1]

ASSERT_SIZE(dxgi_mode_desc_size, DXGI_MODE_DESC, 28);
ASSERT_ALIGNMENT(dxgi_mode_desc_alignment, DXGI_MODE_DESC, 4);
ASSERT_OFFSET(dxgi_mode_desc_width, DXGI_MODE_DESC, Width, 0);
ASSERT_OFFSET(dxgi_mode_desc_height, DXGI_MODE_DESC, Height, 4);
ASSERT_OFFSET(dxgi_mode_desc_refresh_rate, DXGI_MODE_DESC, RefreshRate, 8);
ASSERT_OFFSET(dxgi_mode_desc_format, DXGI_MODE_DESC, Format, 16);
ASSERT_OFFSET(dxgi_mode_desc_scanline_ordering, DXGI_MODE_DESC, ScanlineOrdering, 20);
ASSERT_OFFSET(dxgi_mode_desc_scaling, DXGI_MODE_DESC, Scaling, 24);

ASSERT_SIZE(dxgi_sample_desc_size, DXGI_SAMPLE_DESC, 8);
ASSERT_ALIGNMENT(dxgi_sample_desc_alignment, DXGI_SAMPLE_DESC, 4);
ASSERT_OFFSET(dxgi_sample_desc_count, DXGI_SAMPLE_DESC, Count, 0);
ASSERT_OFFSET(dxgi_sample_desc_quality, DXGI_SAMPLE_DESC, Quality, 4);

ASSERT_SIZE(dxgi_swap_chain_desc_size, DXGI_SWAP_CHAIN_DESC, 72);
ASSERT_ALIGNMENT(dxgi_swap_chain_desc_alignment, DXGI_SWAP_CHAIN_DESC, 8);
ASSERT_OFFSET(dxgi_swap_chain_desc_buffer_desc, DXGI_SWAP_CHAIN_DESC, BufferDesc, 0);
ASSERT_OFFSET(dxgi_swap_chain_desc_sample_desc, DXGI_SWAP_CHAIN_DESC, SampleDesc, 28);
ASSERT_OFFSET(dxgi_swap_chain_desc_buffer_usage, DXGI_SWAP_CHAIN_DESC, BufferUsage, 36);
ASSERT_OFFSET(dxgi_swap_chain_desc_buffer_count, DXGI_SWAP_CHAIN_DESC, BufferCount, 40);
ASSERT_OFFSET(dxgi_swap_chain_desc_output_window, DXGI_SWAP_CHAIN_DESC, OutputWindow, 48);
ASSERT_OFFSET(dxgi_swap_chain_desc_windowed, DXGI_SWAP_CHAIN_DESC, Windowed, 56);
ASSERT_OFFSET(dxgi_swap_chain_desc_swap_effect, DXGI_SWAP_CHAIN_DESC, SwapEffect, 60);
ASSERT_OFFSET(dxgi_swap_chain_desc_flags, DXGI_SWAP_CHAIN_DESC, Flags, 64);

ASSERT_SIZE(d3d11_buffer_desc_size, D3D11_BUFFER_DESC, 24);
ASSERT_ALIGNMENT(d3d11_buffer_desc_alignment, D3D11_BUFFER_DESC, 4);
ASSERT_OFFSET(d3d11_buffer_desc_byte_width, D3D11_BUFFER_DESC, ByteWidth, 0);
ASSERT_OFFSET(d3d11_buffer_desc_usage, D3D11_BUFFER_DESC, Usage, 4);
ASSERT_OFFSET(d3d11_buffer_desc_bind_flags, D3D11_BUFFER_DESC, BindFlags, 8);
ASSERT_OFFSET(d3d11_buffer_desc_cpu_access_flags, D3D11_BUFFER_DESC, CPUAccessFlags, 12);
ASSERT_OFFSET(d3d11_buffer_desc_misc_flags, D3D11_BUFFER_DESC, MiscFlags, 16);
ASSERT_OFFSET(d3d11_buffer_desc_structure_byte_stride, D3D11_BUFFER_DESC, StructureByteStride, 20);

ASSERT_SIZE(d3d11_subresource_data_size, D3D11_SUBRESOURCE_DATA, 16);
ASSERT_ALIGNMENT(d3d11_subresource_data_alignment, D3D11_SUBRESOURCE_DATA, 8);
ASSERT_OFFSET(d3d11_subresource_data_memory, D3D11_SUBRESOURCE_DATA, pSysMem, 0);
ASSERT_OFFSET(d3d11_subresource_data_pitch, D3D11_SUBRESOURCE_DATA, SysMemPitch, 8);
ASSERT_OFFSET(d3d11_subresource_data_slice_pitch, D3D11_SUBRESOURCE_DATA, SysMemSlicePitch, 12);

ASSERT_SIZE(d3d11_input_element_desc_size, D3D11_INPUT_ELEMENT_DESC, 32);
ASSERT_ALIGNMENT(d3d11_input_element_desc_alignment, D3D11_INPUT_ELEMENT_DESC, 8);
ASSERT_OFFSET(d3d11_input_element_desc_semantic_name, D3D11_INPUT_ELEMENT_DESC, SemanticName, 0);
ASSERT_OFFSET(d3d11_input_element_desc_semantic_index, D3D11_INPUT_ELEMENT_DESC, SemanticIndex, 8);
ASSERT_OFFSET(d3d11_input_element_desc_format, D3D11_INPUT_ELEMENT_DESC, Format, 12);
ASSERT_OFFSET(d3d11_input_element_desc_input_slot, D3D11_INPUT_ELEMENT_DESC, InputSlot, 16);
ASSERT_OFFSET(d3d11_input_element_desc_aligned_byte_offset, D3D11_INPUT_ELEMENT_DESC, AlignedByteOffset, 20);
ASSERT_OFFSET(d3d11_input_element_desc_input_slot_class, D3D11_INPUT_ELEMENT_DESC, InputSlotClass, 24);
ASSERT_OFFSET(d3d11_input_element_desc_instance_step_rate, D3D11_INPUT_ELEMENT_DESC, InstanceDataStepRate, 28);

ASSERT_SIZE(d3d11_viewport_size, D3D11_VIEWPORT, 24);
ASSERT_ALIGNMENT(d3d11_viewport_alignment, D3D11_VIEWPORT, 4);
ASSERT_OFFSET(d3d11_viewport_top_left_x, D3D11_VIEWPORT, TopLeftX, 0);
ASSERT_OFFSET(d3d11_viewport_top_left_y, D3D11_VIEWPORT, TopLeftY, 4);
ASSERT_OFFSET(d3d11_viewport_width, D3D11_VIEWPORT, Width, 8);
ASSERT_OFFSET(d3d11_viewport_height, D3D11_VIEWPORT, Height, 12);
ASSERT_OFFSET(d3d11_viewport_min_depth, D3D11_VIEWPORT, MinDepth, 16);
ASSERT_OFFSET(d3d11_viewport_max_depth, D3D11_VIEWPORT, MaxDepth, 20);

ASSERT_SIZE(rect_size, RECT, 16);
ASSERT_ALIGNMENT(rect_alignment, RECT, 4);
ASSERT_OFFSET(rect_left, RECT, left, 0);
ASSERT_OFFSET(rect_top, RECT, top, 4);
ASSERT_OFFSET(rect_right, RECT, right, 8);
ASSERT_OFFSET(rect_bottom, RECT, bottom, 12);

ASSERT_SIZE(window_class_ex_size, WNDCLASSEXW, 80);
ASSERT_ALIGNMENT(window_class_ex_alignment, WNDCLASSEXW, 8);
ASSERT_OFFSET(window_class_ex_size_field, WNDCLASSEXW, cbSize, 0);
ASSERT_OFFSET(window_class_ex_window_proc, WNDCLASSEXW, lpfnWndProc, 8);
ASSERT_OFFSET(window_class_ex_instance, WNDCLASSEXW, hInstance, 24);
ASSERT_OFFSET(window_class_ex_class_name, WNDCLASSEXW, lpszClassName, 64);

ASSERT_SIZE(message_size, MSG, 48);
ASSERT_ALIGNMENT(message_alignment, MSG, 8);
ASSERT_OFFSET(message_window, MSG, hwnd, 0);
ASSERT_OFFSET(message_kind, MSG, message, 8);
ASSERT_OFFSET(message_wparam, MSG, wParam, 16);
ASSERT_OFFSET(message_lparam, MSG, lParam, 24);
ASSERT_OFFSET(message_time, MSG, time, 32);
ASSERT_OFFSET(message_point, MSG, pt, 36);
