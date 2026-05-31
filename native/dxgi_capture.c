#include "dxgi_capture.h"
#include <d3d11.h>
#include <dxgi1_2.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#pragma comment(lib, "d3d11")
#pragma comment(lib, "dxgi")

/* C-compatible COM macro helpers */
#define COM_CALL(obj, method, ...) (obj)->lpVtbl->method(obj, ##__VA_ARGS__)

struct D3D11CaptureContext {
    ID3D11Device* device;
    ID3D11DeviceContext* context;
    IDXGIOutputDuplication* duplication;
    ID3D11Texture2D* staging_texture;
    int width;
    int height;
    int timeout_ms;
    int monitor_index;

    /* Frame buffer */
    uint8_t* frame_buffer;
    int frame_pitch;

    /* Stats */
    int64_t total_frames;
    double total_capture_ms;
    LARGE_INTEGER freq;
};

static double elapsed_ms(LARGE_INTEGER* freq, LARGE_INTEGER start, LARGE_INTEGER end) {
    return (double)(end.QuadPart - start.QuadPart) * 1000.0 / freq->QuadPart;
}

D3D11CaptureContext* dxgi_capture_init(const D3D11CaptureConfig* config) {
    if (!config) return NULL;

    D3D11CaptureContext* ctx = (D3D11CaptureContext*)calloc(1, sizeof(D3D11CaptureContext));
    if (!ctx) return NULL;

    QueryPerformanceFrequency(&ctx->freq);
    ctx->monitor_index = config->monitor_index;
    ctx->timeout_ms = config->timeout_ms > 0 ? config->timeout_ms : 100;

    /* Create D3D11 device */
    D3D_FEATURE_LEVEL feature_level;
    HRESULT hr = D3D11CreateDevice(
        NULL, D3D_DRIVER_TYPE_HARDWARE, NULL, 0,
        NULL, 0, D3D11_SDK_VERSION,
        &ctx->device, &feature_level, &ctx->context);
    if (FAILED(hr)) {
        free(ctx);
        return NULL;
    }

    /* Get DXGI adapter */
    IDXGIDevice* dxgi_device = NULL;
    hr = COM_CALL(ctx->device, QueryInterface, &IID_IDXGIDevice, (void**)&dxgi_device);
    if (FAILED(hr)) { free(ctx); return NULL; }

    IDXGIAdapter* adapter = NULL;
    hr = COM_CALL(dxgi_device, GetAdapter, &adapter);
    COM_CALL(dxgi_device, Release);
    if (FAILED(hr)) { free(ctx); return NULL; }

    /* Get target output (monitor) */
    IDXGIOutput* output = NULL;
    hr = COM_CALL(adapter, EnumOutputs, config->monitor_index, &output);
    COM_CALL(adapter, Release);
    if (FAILED(hr)) { free(ctx); return NULL; }

    IDXGIOutput1* output1 = NULL;
    hr = COM_CALL(output, QueryInterface, &IID_IDXGIOutput1, (void**)&output1);
    COM_CALL(output, Release);
    if (FAILED(hr)) { free(ctx); return NULL; }

    /* Get output dimensions */
    DXGI_OUTPUT_DESC output_desc;
    COM_CALL(output1, GetDesc, &output_desc);
    RECT rect = output_desc.DesktopCoordinates;
    ctx->width = rect.right - rect.left;
    ctx->height = rect.bottom - rect.top;

    if (config->width > 0 && config->height > 0) {
        ctx->width = config->width;
        ctx->height = config->height;
    }

    /* Create desktop duplication */
    hr = COM_CALL(output1, DuplicateOutput, (IUnknown*)ctx->device, &ctx->duplication);
    COM_CALL(output1, Release);
    if (FAILED(hr)) { free(ctx); return NULL; }

    /* Create staging texture for CPU readback */
    D3D11_TEXTURE2D_DESC tex_desc;
    memset(&tex_desc, 0, sizeof(tex_desc));
    tex_desc.Width = ctx->width;
    tex_desc.Height = ctx->height;
    tex_desc.MipLevels = 1;
    tex_desc.ArraySize = 1;
    tex_desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    tex_desc.SampleDesc.Count = 1;
    tex_desc.Usage = D3D11_USAGE_STAGING;
    tex_desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;

    hr = COM_CALL(ctx->device, CreateTexture2D, &tex_desc, NULL, &ctx->staging_texture);
    if (FAILED(hr)) {
        COM_CALL(ctx->duplication, Release);
        free(ctx);
        return NULL;
    }

    /* Allocate frame buffer */
    ctx->frame_pitch = ctx->width * 4;
    ctx->frame_buffer = (uint8_t*)malloc(ctx->frame_pitch * ctx->height);
    if (!ctx->frame_buffer) {
        COM_CALL(ctx->staging_texture, Release);
        COM_CALL(ctx->duplication, Release);
        free(ctx);
        return NULL;
    }

    return ctx;
}

int dxgi_capture_frame(D3D11CaptureContext* ctx, FrameData* frame) {
    if (!ctx || !frame) return -1;
    frame->data = NULL;

    DXGI_OUTDUPL_FRAME_INFO frame_info;
    IDXGIResource* resource = NULL;

    HRESULT hr = COM_CALL(ctx->duplication, AcquireNextFrame,
        ctx->timeout_ms, &frame_info, &resource);

    if (hr == DXGI_ERROR_WAIT_TIMEOUT) return 1;
    if (FAILED(hr)) return -1;

    LARGE_INTEGER t_start, t_end;
    QueryPerformanceCounter(&t_start);

    /* Get the texture */
    ID3D11Texture2D* texture = NULL;
    hr = COM_CALL(resource, QueryInterface, &IID_ID3D11Texture2D, (void**)&texture);
    COM_CALL(resource, Release);
    if (FAILED(hr)) {
        COM_CALL(ctx->duplication, ReleaseFrame);
        return -1;
    }

    /* Copy to staging texture (GPU → CPU readable) */
    ID3D11Resource* src = NULL;
    COM_CALL(texture, QueryInterface, &IID_ID3D11Resource, (void**)&src);
    ID3D11Resource* dst = NULL;
    COM_CALL(ctx->staging_texture, QueryInterface, &IID_ID3D11Resource, (void**)&dst);
    COM_CALL(ctx->context, CopyResource, dst, src);
    COM_CALL(src, Release);
    COM_CALL(dst, Release);
    COM_CALL(texture, Release);
    COM_CALL(ctx->duplication, ReleaseFrame);

    /* Map staging texture */
    D3D11_MAPPED_SUBRESOURCE mapped;
    hr = COM_CALL(ctx->context, Map,
        (ID3D11Resource*)ctx->staging_texture, 0, D3D11_MAP_READ, 0, &mapped);
    if (FAILED(hr)) return -1;

    /* Copy to frame buffer */
    uint8_t* src_row = (uint8_t*)mapped.pData;
    uint8_t* dst_row = ctx->frame_buffer;
    int copy_width = ctx->width * 4;
    for (int y = 0; y < ctx->height; y++) {
        memcpy(dst_row, src_row, copy_width);
        src_row += mapped.RowPitch;
        dst_row += ctx->frame_pitch;
    }

    COM_CALL(ctx->context, Unmap,
        (ID3D11Resource*)ctx->staging_texture, 0);

    QueryPerformanceCounter(&t_end);

    frame->data = ctx->frame_buffer;
    frame->width = ctx->width;
    frame->height = ctx->height;
    frame->pitch = ctx->frame_pitch;
    frame->timestamp = (int64_t)(t_start.QuadPart * 1000000 / ctx->freq.QuadPart);

    ctx->total_frames++;
    ctx->total_capture_ms += elapsed_ms(&ctx->freq, t_start, t_end);

    return 0;
}

void dxgi_capture_release(D3D11CaptureContext* ctx) {
    (void)ctx;
}

void dxgi_capture_stats(D3D11CaptureContext* ctx,
    int64_t* total_frames, double* avg_capture_ms) {
    if (!ctx) return;
    if (total_frames) *total_frames = ctx->total_frames;
    if (avg_capture_ms) {
        *avg_capture_ms = ctx->total_frames > 0
            ? ctx->total_capture_ms / ctx->total_frames : 0;
    }
}

void dxgi_capture_destroy(D3D11CaptureContext* ctx) {
    if (!ctx) return;
    if (ctx->frame_buffer) free(ctx->frame_buffer);
    if (ctx->staging_texture) COM_CALL(ctx->staging_texture, Release);
    if (ctx->duplication) COM_CALL(ctx->duplication, Release);
    if (ctx->context) COM_CALL(ctx->context, Release);
    if (ctx->device) COM_CALL(ctx->device, Release);
    free(ctx);
}

int dxgi_monitor_count(void) {
    IDXGIFactory1* factory = NULL;
    HRESULT hr = CreateDXGIFactory1(&IID_IDXGIFactory1, (void**)&factory);
    if (FAILED(hr)) return 0;

    int count = 0;
    IDXGIAdapter1* adapter = NULL;
    for (UINT i = 0; COM_CALL(factory, EnumAdapters1, i, &adapter) != DXGI_ERROR_NOT_FOUND; i++) {
        IDXGIOutput* output = NULL;
        for (UINT j = 0; COM_CALL(adapter, EnumOutputs, j, &output) != DXGI_ERROR_NOT_FOUND; j++) {
            count++;
            COM_CALL(output, Release);
        }
        COM_CALL(adapter, Release);
    }
    COM_CALL(factory, Release);
    return count;
}

int dxgi_monitor_info(int index, int* width, int* height) {
    IDXGIFactory1* factory = NULL;
    HRESULT hr = CreateDXGIFactory1(&IID_IDXGIFactory1, (void**)&factory);
    if (FAILED(hr)) return -1;

    int current = 0;
    IDXGIAdapter1* adapter = NULL;
    for (UINT i = 0; COM_CALL(factory, EnumAdapters1, i, &adapter) != DXGI_ERROR_NOT_FOUND; i++) {
        IDXGIOutput* output = NULL;
        for (UINT j = 0; COM_CALL(adapter, EnumOutputs, j, &output) != DXGI_ERROR_NOT_FOUND; j++) {
            if (current == index) {
                DXGI_OUTPUT_DESC desc;
                COM_CALL(output, GetDesc, &desc);
                *width = desc.DesktopCoordinates.right - desc.DesktopCoordinates.left;
                *height = desc.DesktopCoordinates.bottom - desc.DesktopCoordinates.top;
                COM_CALL(output, Release);
                COM_CALL(adapter, Release);
                COM_CALL(factory, Release);
                return 0;
            }
            current++;
            COM_CALL(output, Release);
        }
        COM_CALL(adapter, Release);
    }
    COM_CALL(factory, Release);
    return -1;
}
