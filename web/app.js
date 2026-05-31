// UniLinker WebRTC Client
// Standard WebRTC receiver — works in any browser (Chrome, Firefox, Safari, Edge)

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
const hostInput = document.getElementById('hostInput');

function setStatus(state, text) {
    statusDot.className = 'status-dot ' + state;
    statusText.textContent = text;
}

async function connect() {
    if (pc) {
        disconnect();
        return;
    }

    const host = hostInput.value.trim();
    if (!host) {
        setStatus('error', 'Enter a host address');
        return;
    }

    btnConnect.disabled = true;
    btnConnect.textContent = '...';
    setStatus('connecting', 'Connecting...');

    try {
        pc = new RTCPeerConnection({
            iceServers: [{ urls: 'stun:stun.l.google.com:19302' }]
        });

        pc.ontrack = (event) => {
            console.log('Track received:', event.track.kind);
            video.srcObject = event.streams[0];
            video.style.display = 'block';
            placeholder.style.display = 'none';
            btnFullscreen.disabled = false;
            overlayInfo.classList.add('visible');
            startStats();
        };

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

        // ICE candidate forwarding
        pc.onicecandidate = (event) => {
            if (event.candidate) {
                console.log('ICE candidate:', event.candidate);
                // Send candidate to host
                fetch(`http://${host}:9527/ice`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        peerId: 'browser-' + Date.now(),
                        candidate: event.candidate.candidate,
                        sdpMid: event.candidate.sdpMid,
                        sdpMLineIndex: event.candidate.sdpMLineIndex
                    })
                }).catch(err => console.warn('ICE send error:', err));
            }
        };

        // Add transceiver to receive video
        pc.addTransceiver('video', { direction: 'recvonly' });

        // Create offer
        const offer = await pc.createOffer();
        await pc.setLocalDescription(offer);

        // Wait for ICE gathering
        await waitForIceGathering(pc);

        // Send offer to host's signaling server
        const response = await fetch(`http://${host}:9527/signaling`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                type: 'offer',
                sdp: pc.localDescription.sdp,
                fromPeerId: 'browser-' + Date.now(),
                capability: 'screen-capture'
            })
        });

        if (!response.ok) {
            throw new Error('Server error: ' + await response.text());
        }

        const answer = await response.json();
        console.log('Answer received:', answer);

        await pc.setRemoteDescription(
            new RTCSessionDescription({ type: 'answer', sdp: answer.sdp })
        );

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

function waitForIceGathering(peerConnection) {
    return new Promise((resolve) => {
        if (peerConnection.iceGatheringState === 'complete') {
            resolve();
            return;
        }
        peerConnection.onicegatheringstatechange = () => {
            if (peerConnection.iceGatheringState === 'complete') {
                resolve();
            }
        };
        // Timeout fallback
        setTimeout(resolve, 3000);
    });
}

function toggleFullscreen() {
    const container = document.querySelector('.main-area');
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

                    const bitrate = lastBytes > 0
                        ? ((report.bytesReceived - lastBytes) * 8 / dt / 1000).toFixed(0)
                        : '0';
                    lastBytes = report.bytesReceived;
                    lastTime = now;

                    const fps = report.framesPerSecond || '-';
                    const w = report.frameWidth || '-';
                    const h = report.frameHeight || '-';

                    infoText.textContent = `${w}x${h} | ${fps} fps | ${bitrate} kbps`;
                    overlayInfo.innerHTML =
                        `${w} x ${h}<br>${fps} fps<br>${bitrate} kbps`;

                    if (report.totalDecodeTime && report.framesDecoded) {
                        const avgDecode = (report.totalDecodeTime / report.framesDecoded * 1000).toFixed(1);
                        overlayInfo.innerHTML += `<br>${avgDecode}ms decode`;
                    }
                }

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
