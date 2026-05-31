// UniLinker WebRTC Client
let pc = null;
let statsInterval = null;

const video = document.getElementById('remoteVideo');
const placeholder = document.getElementById('placeholder');
const statusDot = document.getElementById('statusDot');
const statusText = document.getElementById('statusText');
const infoText = document.getElementById('infoText');
const overlayInfo = document.getElementById('overlayInfo');
const btnConnect = document.getElementById('btnConnect');
const btnFullscreen = document.getElementById('btnFullscreen');

function setStatus(state, text) {
    statusDot.className = 'status-dot ' + state;
    statusText.textContent = text;
}

async function connect() {
    if (pc) {
        disconnect();
        return;
    }

    btnConnect.disabled = true;
    setStatus('connecting', 'Connecting...');

    try {
        // Create peer connection
        pc = new RTCPeerConnection({
            iceServers: [{ urls: 'stun:stun.l.google.com:19302' }]
        });

        // Handle incoming tracks
        pc.ontrack = (event) => {
            console.log('Track received:', event.track.kind);
            video.srcObject = event.streams[0];
            video.style.display = 'block';
            placeholder.style.display = 'none';
            btnFullscreen.disabled = false;
            overlayInfo.classList.add('visible');
            startStats();
        };

        // Handle connection state
        pc.onconnectionstatechange = () => {
            console.log('Connection state:', pc.connectionState);
            switch (pc.connectionState) {
                case 'connected':
                    setStatus('connected', 'Connected');
                    break;
                case 'disconnected':
                    setStatus('error', 'Disconnected');
                    break;
                case 'failed':
                    setStatus('error', 'Connection Failed');
                    disconnect();
                    break;
            }
        };

        pc.oniceconnectionstatechange = () => {
            console.log('ICE state:', pc.iceConnectionState);
        };

        // Add transceiver to receive video
        pc.addTransceiver('video', { direction: 'recvonly' });

        // Create offer
        const offer = await pc.createOffer();
        await pc.setLocalDescription(offer);

        // Wait for ICE gathering
        await waitForIceGathering(pc);

        // Send offer to server
        const response = await fetch('/offer', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(pc.localDescription)
        });

        if (!response.ok) {
            throw new Error('Server error: ' + await response.text());
        }

        const answer = await response.json();
        await pc.setRemoteDescription(answer);

        btnConnect.textContent = 'Disconnect';
        btnConnect.disabled = false;
        btnConnect.classList.remove('primary');

    } catch (err) {
        console.error('Connection failed:', err);
        setStatus('error', 'Error: ' + err.message);
        disconnect();
    }
}

function disconnect() {
    if (statsInterval) {
        clearInterval(statsInterval);
        statsInterval = null;
    }

    if (pc) {
        pc.close();
        pc = null;
    }

    video.srcObject = null;
    video.style.display = 'none';
    placeholder.style.display = 'flex';
    overlayInfo.classList.remove('visible');
    infoText.textContent = '';

    btnConnect.textContent = 'Connect';
    btnConnect.disabled = false;
    btnConnect.classList.add('primary');
    btnFullscreen.disabled = true;

    setStatus('', 'Disconnected');
}

function waitForIceGathering(pc) {
    return new Promise((resolve) => {
        if (pc.iceGatheringState === 'complete') {
            resolve();
            return;
        }
        pc.onicegatheringstatechange = () => {
            if (pc.iceGatheringState === 'complete') {
                resolve();
            }
        };
        // Timeout fallback
        setTimeout(resolve, 3000);
    });
}

function toggleFullscreen() {
    const container = document.getElementById('videoContainer');
    if (!document.fullscreenElement) {
        container.requestFullscreen().catch(err => {
            console.error('Fullscreen error:', err);
        });
    } else {
        document.exitFullscreen();
    }
}

function startStats() {
    let lastBytes = 0;
    let lastTime = Date.now();

    statsInterval = setInterval(async () => {
        if (!pc) return;

        try {
            const stats = await pc.getStats();
            stats.forEach(report => {
                if (report.type === 'inbound-rtp' && report.kind === 'video') {
                    const now = Date.now();
                    const dt = (now - lastTime) / 1000;

                    // Bitrate
                    const bitrate = lastBytes > 0
                        ? ((report.bytesReceived - lastBytes) * 8 / dt / 1000).toFixed(0)
                        : '0';
                    lastBytes = report.bytesReceived;
                    lastTime = now;

                    // FPS
                    const fps = report.framesPerSecond || '-';

                    // Resolution
                    const w = report.frameWidth || '-';
                    const h = report.frameHeight || '-';

                    // Info text in toolbar
                    infoText.textContent = `${w}x${h} | ${fps} fps | ${bitrate} kbps`;

                    // Overlay info
                    overlayInfo.innerHTML =
                        `${w} × ${h}<br>` +
                        `${fps} fps<br>` +
                        `${bitrate} kbps`;

                    // Decode time
                    if (report.totalDecodeTime && report.framesDecoded) {
                        const avgDecode = (report.totalDecodeTime / report.framesDecoded * 1000).toFixed(1);
                        overlayInfo.innerHTML += `<br>${avgDecode}ms decode`;
                    }
                }

                // Round-trip time
                if (report.type === 'candidate-pair' && report.state === 'succeeded') {
                    const rtt = report.currentRoundTripTime;
                    if (rtt !== undefined) {
                        infoText.textContent += ` | ${(rtt * 1000).toFixed(0)}ms RTT`;
                    }
                }
            });
        } catch (e) {
            // ignore stats errors
        }
    }, 1000);
}

// Keyboard shortcuts
document.addEventListener('keydown', (e) => {
    if (e.key === 'f' || e.key === 'F') {
        toggleFullscreen();
    }
    if (e.key === 'Escape' && document.fullscreenElement) {
        document.exitFullscreen();
    }
});

// Initialize
setStatus('', 'Disconnected');
