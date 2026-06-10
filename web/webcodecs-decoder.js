/**
 * WebCodecs H.264 Decoder for UniLinker Screen Mirror
 *
 * This module provides a WebCodecs-based video decoder as an alternative
 * to the browser's built-in WebRTC video decoder. WebCodecs offers:
 * - Lower latency (direct access to decoded frames)
 * - Better control over decoding parameters
 * - Direct rendering to Canvas/WebGL for post-processing
 *
 * Usage:
 * 1. Browser checks WebCodecs availability: WebCodecsDecoder.isAvailable()
 * 2. Create decoder: const decoder = new WebCodecsDecoder(canvas)
 * 3. Feed H.264 NAL units: decoder.decode(nalUnit, isKeyFrame)
 *
 * Note: WebCodecs is only available in Chrome/Edge 94+ and Safari 16.4+
 * Firefox support is still in development.
 */

class WebCodecsDecoder {
    constructor(canvas) {
        this.canvas = canvas;
        this.decoder = null;
        this.renderer = null;
        this.isRunning = false;
        this.frameCount = 0;
        this.lastFrameTime = 0;
        this.codec = 'avc1.42001E'; // H.264 Baseline Profile Level 3.0

        // Callbacks
        this.onFrameDecoded = null;
        this.onError = null;
        this.onStatsUpdate = null;
    }

    /**
     * Check if WebCodecs is available in this browser
     */
    static isAvailable() {
        return 'VideoDecoder' in window;
    }

    /**
     * Initialize the decoder
     * @param {string} codec - H.264 codec string (default: avc1.42001E)
     * @returns {Promise<boolean>} - Whether initialization succeeded
     */
    async init(codec = null) {
        if (!WebCodecsDecoder.isAvailable()) {
            console.warn('WebCodecs not available in this browser');
            return false;
        }

        if (codec) {
            this.codec = codec;
        }

        // Check codec support
        const support = await VideoDecoder.isConfigSupported({
            codec: this.codec,
            hardwareAcceleration: 'prefer-hardware',
        });

        if (!support.supported) {
            console.warn(`Codec ${this.codec} not supported`);
            // Try software decoding
            const softwareSupport = await VideoDecoder.isConfigSupported({
                codec: this.codec,
                hardwareAcceleration: 'no-preference',
            });
            if (!softwareSupport.supported) {
                console.error('No supported H.264 codec configuration found');
                return false;
            }
        }

        // Create decoder
        this.decoder = new VideoDecoder({
            output: (frame) => this._handleFrame(frame),
            error: (e) => this._handleError(e),
        });

        // Configure decoder
        this.decoder.configure({
            codec: this.codec,
            hardwareAcceleration: 'prefer-hardware',
            optimizeForLatency: true,
        });

        // Setup canvas renderer
        this._setupRenderer();

        this.isRunning = true;
        console.log('WebCodecs decoder initialized');
        return true;
    }

    /**
     * Setup the canvas renderer
     */
    _setupRenderer() {
        const ctx = this.canvas.getContext('2d');
        this.renderer = ctx;
    }

    /**
     * Handle decoded video frame
     */
    _handleFrame(frame) {
        this.frameCount++;
        const now = performance.now();
        const decodeTime = now - this.lastFrameTime;
        this.lastFrameTime = now;

        // Render frame to canvas
        this.canvas.width = frame.displayWidth;
        this.canvas.height = frame.displayHeight;
        this.renderer.drawImage(frame, 0, 0);

        // Close frame to release memory
        frame.close();

        // Fire callbacks
        if (this.onFrameDecoded) {
            this.onFrameDecoded(frame.displayWidth, frame.displayHeight);
        }

        if (this.onStatsUpdate && this.frameCount % 30 === 0) {
            this.onStatsUpdate({
                frames: this.frameCount,
                decodeTime: decodeTime.toFixed(1),
                fps: (1000 / decodeTime).toFixed(1),
            });
        }
    }

    /**
     * Handle decoder error
     */
    _handleError(e) {
        console.error('WebCodecs decoder error:', e);
        if (this.onError) {
            this.onError(e.message);
        }
    }

    /**
     * Decode an H.264 NAL unit
     * @param {Uint8Array} nalUnit - Raw H.264 NAL unit data
     * @param {boolean} isKeyFrame - Whether this is a keyframe (IDR)
     * @param {number} timestamp - Presentation timestamp in microseconds
     */
    decode(nalUnit, isKeyFrame = false, timestamp = 0) {
        if (!this.isRunning || !this.decoder) return;

        // Create EncodedVideoChunk from NAL unit
        const chunk = new EncodedVideoChunk({
            type: isKeyFrame ? 'key' : 'delta',
            timestamp: timestamp,
            data: nalUnit,
        });

        try {
            this.decoder.decode(chunk);
        } catch (e) {
            console.warn('Decode error:', e);
        }
    }

    /**
     * Flush decoder to output all pending frames
     */
    async flush() {
        if (this.decoder) {
            try {
                await this.decoder.flush();
            } catch (e) {
                console.warn('Flush error:', e);
            }
        }
    }

    /**
     * Reset decoder state
     */
    reset() {
        if (this.decoder) {
            this.decoder.reset();
            this.frameCount = 0;
        }
    }

    /**
     * Close and cleanup decoder
     */
    close() {
        this.isRunning = false;
        if (this.decoder) {
            this.decoder.close();
            this.decoder = null;
        }
    }

    /**
     * Get decoder state
     */
    getState() {
        if (!this.decoder) return 'closed';
        return this.decoder.state;
    }
}

// Export for module usage
if (typeof module !== 'undefined' && module.exports) {
    module.exports = WebCodecsDecoder;
}

// Make available globally
window.WebCodecsDecoder = WebCodecsDecoder;