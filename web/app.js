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
const signalIndicator = document.getElementById('signalIndicator');
const toastContainer = document.getElementById('toastContainer');

// ═══════════════════════════════════════════════════════════════════════
// Toast 通知系统
// ═══════════════════════════════════════════════════════════════════════

function showToast(message, type = 'info', duration = 3000) {
    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    toast.innerHTML = `
        <span class="message">${message}</span>
        <button class="close-btn" aria-label="关闭">✕</button>
    `;

    const closeBtn = toast.querySelector('.close-btn');
    closeBtn.onclick = () => removeToast(toast);

    toastContainer.appendChild(toast);

    if (duration > 0) {
        setTimeout(() => removeToast(toast), duration);
    }
}

function removeToast(toast) {
    toast.style.animation = 'toastIn 0.3s ease-out reverse';
    setTimeout(() => toast.remove(), 300);
}

// ═══════════════════════════════════════════════════════════════════════
// 信号质量指示器
// ═══════════════════════════════════════════════════════════════════════

function updateSignalQuality(rttMs, packetLoss, bitrate) {
    const bars = signalIndicator.querySelectorAll('.signal-bar');

    // 计算质量等级 (1-4)
    let score = 4;
    if (rttMs > 100) score--;
    if (rttMs > 200) score--;
    if (packetLoss > 1) score--;
    if (packetLoss > 5) score--;
    if (bitrate < 2000) score--;
    score = Math.max(1, Math.min(4, score));

    // 确定质量等级名称
    const qualityClass = ['poor', 'fair', 'good', 'excellent'][score - 1];

    // 更新信号条
    bars.forEach((bar, index) => {
        bar.classList.remove('active', 'excellent', 'good', 'fair', 'poor');
        if (index < score) {
            bar.classList.add('active', qualityClass);
        }
    });

    // 更新 aria-label
    const qualityLabel = ['较差', '一般', '良好', '优秀'][score - 1];
    signalIndicator.setAttribute('aria-label', `连接质量: ${qualityLabel}`);
}

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
        showToast('请输入主机地址', 'warning');
        return;
    }

    btnConnect.disabled = true;
    btnConnect.textContent = '...';
    setStatus('connecting', '连接中...');

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
            showToast('已连接到 ' + host, 'success');
        };

        pc.onconnectionstatechange = () => {
            console.log('Connection state:', pc.connectionState);
            switch (pc.connectionState) {
                case 'connected':
                    setStatus('connected', '已连接');
                    break;
                case 'disconnected':
                    setStatus('error', '已断开');
                    showToast('连接已断开', 'warning');
                    break;
                case 'failed':
                    setStatus('error', '连接失败');
                    showToast('连接失败，请检查网络', 'error');
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

        btnConnect.textContent = '断开';
        btnConnect.disabled = false;
        btnConnect.classList.remove('primary');

    } catch (err) {
        console.error('Connection failed:', err);
        setStatus('error', '错误: ' + err.message);
        showToast('连接失败: ' + err.message, 'error');
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

    // 重置信号指示器
    const bars = signalIndicator.querySelectorAll('.signal-bar');
    bars.forEach(bar => bar.classList.remove('active', 'excellent', 'good', 'fair', 'poor'));

    btnConnect.textContent = '连接';
    btnConnect.disabled = false;
    btnConnect.classList.add('primary');
    btnFullscreen.disabled = true;

    setStatus('', '已断开');
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
            showToast('无法进入全屏模式', 'error');
        });
    } else {
        document.exitFullscreen();
    }
}

function startStats() {
    let lastBytes = 0;
    let lastTime = Date.now();
    let lastPacketsLost = 0;
    let lastPacketsReceived = 0;

    statsInterval = setInterval(async () => {
        if (!pc) return;

        try {
            const stats = await pc.getStats();
            let rttMs = 0;
            let bitrate = 0;
            let packetLossPercent = 0;

            stats.forEach(report => {
                if (report.type === 'inbound-rtp' && report.kind === 'video') {
                    const now = Date.now();
                    const dt = (now - lastTime) / 1000;

                    bitrate = lastBytes > 0
                        ? ((report.bytesReceived - lastBytes) * 8 / dt / 1000).toFixed(0)
                        : 0;
                    lastBytes = report.bytesReceived;
                    lastTime = now;

                    // 计算丢包率
                    if (lastPacketsReceived > 0) {
                        const packetsLost = report.packetsLost - lastPacketsLost;
                        const packetsReceived = report.packetsReceived - lastPacketsReceived;
                        if (packetsReceived > 0) {
                            packetLossPercent = (packetsLost / (packetsLost + packetsReceived) * 100).toFixed(1);
                        }
                    }
                    lastPacketsLost = report.packetsLost || 0;
                    lastPacketsReceived = report.packetsReceived || 0;

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
                    rttMs = (report.currentRoundTripTime || 0) * 1000;
                    if (rttMs > 0) {
                        infoText.textContent += ` | ${rttMs.toFixed(0)}ms RTT`;
                    }
                }
            });

            // 更新信号质量
            updateSignalQuality(rttMs, packetLossPercent, bitrate);

        } catch (e) {
            // ignore stats errors
        }
    }, 1000);
}

// ═══════════════════════════════════════════════════════════════════════
// 键盘快捷键
// ═══════════════════════════════════════════════════════════════════════

document.addEventListener('keydown', (e) => {
    // F 或 F11 - 全屏
    if (e.key === 'f' || e.key === 'F' || e.key === 'F11') {
        e.preventDefault();
        toggleFullscreen();
    }
    // Escape - 退出全屏
    if (e.key === 'Escape' && document.fullscreenElement) {
        document.exitFullscreen();
    }
    // Ctrl+D - 断开连接
    if (e.ctrlKey && e.key === 'd') {
        e.preventDefault();
        if (pc) {
            disconnect();
            showToast('已断开连接', 'info');
        }
    }
});

// Initialize
setStatus('', '已断开');
