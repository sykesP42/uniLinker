#ifndef DXGI_CAPTURE_H
#define DXGI_CAPTURE_H

#include <stdint.h>
#include <stddef.h>

#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    int monitor_index;
    int width;
    int height;
    int timeout_ms;
} D3D11CaptureConfig;

typedef struct {
    uint8_t* data;      /* BGRA pixel data */
    int width;
    int height;
    int pitch;          /* bytes per row */
    int64_t timestamp;  /* microseconds */
} FrameData;

typedef struct D3D11CaptureContext D3D11CaptureContext;

/* Initialize DXGI desktop duplication capture */
EXPORT D3D11CaptureContext* dxgi_capture_init(const D3D11CaptureConfig* config);

/* Capture next frame. Returns 0 on success, negative on error.
 * On timeout (no new frame), returns 1 and frame->data is NULL.
 * Caller must NOT free frame->data - it's managed internally. */
EXPORT int dxgi_capture_frame(D3D11CaptureContext* ctx, FrameData* frame);

/* Release the current frame buffer (call after processing) */
EXPORT void dxgi_capture_release(D3D11CaptureContext* ctx);

/* Get capture statistics */
EXPORT void dxgi_capture_stats(D3D11CaptureContext* ctx,
    int64_t* total_frames, double* avg_capture_ms);

/* Cleanup */
EXPORT void dxgi_capture_destroy(D3D11CaptureContext* ctx);

/* Get number of monitors */
EXPORT int dxgi_monitor_count(void);

/* Get monitor dimensions */
EXPORT int dxgi_monitor_info(int index, int* width, int* height);

#ifdef __cplusplus
}
#endif

#endif /* DXGI_CAPTURE_H */
