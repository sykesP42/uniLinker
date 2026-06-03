/**
 * UniLinker Web Client — WebRTC-based screen mirror viewer.
 *
 * Protocol:
 *   GET  /info          — device metadata
 *   POST /signaling     — SDP exchange (browser sends offer, gets answer)
 *   POST /ice           — browser ICE candidate → server
 *   GET  /ice-pending   — pull server-side ICE candidates (polling)
 */
class UniLinkerClient {
  constructor() {
    this.pc = null;
    this.peerId = 'web-' + Math.random().toString(36).substring(2, 10);
    this.serverUrl = '';
    this._pollTimer = null;
  }

  /** Fetch device info */
  async fetchInfo(serverUrl) {
    const url = `${serverUrl}/info`;
    const res = await fetch(url);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return res.json();
  }

  /** Initiate WebRTC connection to the given server URL */
  async connect(serverUrl) {
    this.serverUrl = serverUrl;

    // 1. Fetch device info
    let info;
    try {
      info = await this.fetchInfo(serverUrl);
    } catch (e) {
      throw new Error(`无法连接服务器: ${e.message}`);
    }

    // 2. Create RTCPeerConnection with STUN for LAN
    this.pc = new RTCPeerConnection({
      iceServers: [
        { urls: 'stun:stun.l.google.com:19302' },
      ],
      // H.264 hardware decode is preferred; fallback handled by browser
    });

    // 3. Add recvonly video transceiver (request H.264 if possible)
    this.pc.addTransceiver('video', {
      direction: 'recvonly',
      streams: [],
      sendEncodings: [],
    });

    // 4. Wait for remote video track
    this.pc.ontrack = (event) => {
      const video = document.getElementById('video');
      video.srcObject = event.streams[0];
      video.play().catch(() => {});
    };

    // 5. Handle connection state
    let iceConnected = false;
    this.pc.onconnectionstatechange = () => {
      const state = this.pc.connectionState;
      if (state === 'connected' && !iceConnected) {
        iceConnected = true;
        setStatus('connected', '🟢 已连接');
      } else if (state === 'disconnected' || state === 'failed') {
        setStatus('error', '🔴 连接断开');
        this.stopPolling();
      }
    };

    // 6. ICE candidate → server
    this.pc.onicecandidate = (e) => {
      if (e.candidate) {
        this.sendIceCandidate(e.candidate).catch(() => {});
      }
    };

    // 7. Create SDP offer (with ICE gathering)
    const offer = await this.pc.createOffer({
      offerToReceiveVideo: true,
    });

    // Set local description (triggers ICE gathering)
    await this.pc.setLocalDescription(offer);

    // Wait briefly for initial ICE candidates to gather
    await new Promise((r) => setTimeout(r, 1500));

    // 8. Send offer → server, receive answer
    setStatus('connecting', '🟡 信令交换中…');
    const answer = await this.postJson('/signaling', {
      type: 'offer',
      sdp: this.pc.localDescription.sdp,
      fromPeerId: this.peerId,
      capability: 'screen-capture',
    });

    if (!answer || !answer.sdp) {
      throw new Error('信令服务器返回无效应答');
    }

    await this.pc.setRemoteDescription(
      new RTCSessionDescription({ type: 'answer', sdp: answer.sdp })
    );

    // 9. Start polling for server-side ICE candidates
    this.startPolling();

    // 10. Start stats collection
    this.startStats();

    return info;
  }

  /** Send ICE candidate to server */
  async sendIceCandidate(candidate) {
    await this.postJson('/ice', {
      peerId: this.peerId,
      candidate: candidate.candidate,
      sdpMid: candidate.sdpMid ?? '0',
      sdpMLineIndex: candidate.sdpMLineIndex ?? 0,
    });
  }

  /** Poll for pending server ICE candidates */
  async pollIceCandidates() {
    try {
      const data = await this.getJson(`/ice-pending?peerId=${this.peerId}`);
      if (data && data.candidates && this.pc) {
        for (const c of data.candidates) {
          try {
            await this.pc.addIceCandidate(
              new RTCIceCandidate({
                candidate: c.candidate,
                sdpMid: c.sdpMid,
                sdpMLineIndex: c.sdpMLineIndex,
              })
            );
          } catch { /* candidate may already be incorporated */ }
        }
      }
    } catch { /* polling error; ignore */ }
  }

  startPolling() {
    this.stopPolling();
    this._pollTimer = setInterval(() => this.pollIceCandidates(), 500);
    // Stop after 10 seconds (LAN ICE exchange should be complete by then)
    setTimeout(() => this.stopPolling(), 10000);
  }

  stopPolling() {
    if (this._pollTimer) {
      clearInterval(this._pollTimer);
      this._pollTimer = null;
    }
  }

  /** Start periodic stats collection via getStats() */
  startStats() {
    const el = (id) => document.getElementById(id);
    const statsEl = document.getElementById('stats');

    const tick = async () => {
      if (!this.pc || this.pc.connectionState !== 'connected') return;

      try {
        const report = await this.pc.getStats();
        for (const s of report.values()) {
          if (s.type === 'inbound-rtp' && s.kind === 'video' && s.codecId) {
            const w = s.frameWidth ?? '—';
            const h = s.frameHeight ?? '—';
            el('statRes').textContent = `📐 ${w}×${h}`;
            el('statFps').textContent = `🎞 ${Math.round(s.framesPerSecond ?? 0)} fps`;
            el('statRtt').textContent = `⏱ ${Math.round((s.totalRoundTripTime ?? 0) * 1000)} ms`;
            statsEl.classList.add('visible');
            break;
          }
        }
      } catch { /* ignore */ }
    };

    tick();
    this._statsTimer = setInterval(tick, 2000);
  }

  stopStats() {
    if (this._statsTimer) {
      clearInterval(this._statsTimer);
      this._statsTimer = null;
    }
    document.getElementById('stats').classList.remove('visible');
  }

  /** Disconnect */
  disconnect() {
    this.stopPolling();
    this.stopStats();
    if (this.pc) {
      this.pc.close();
      this.pc = null;
    }
    const video = document.getElementById('video');
    video.srcObject = null;
    video.load();
  }

  // ── HTTP helpers ──

  async postJson(path, body) {
    const url = this.serverUrl + path;
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    if (!res.ok) throw new Error(`POST ${path} HTTP ${res.status}`);
    const text = await res.text();
    return text ? JSON.parse(text) : null;
  }

  async getJson(path) {
    const url = this.serverUrl + path;
    const res = await fetch(url);
    if (!res.ok) return null;
    return res.json();
  }
}

// ═══════════════════════════════════════════════════════════════
// UI Bindings
// ═══════════════════════════════════════════════════════════════

const client = new UniLinkerClient();

function $(id) { return document.getElementById(id); }

function setStatus(type, text) {
  const badge = $('statusBadge');
  badge.textContent = text;
  badge.className = 'status-badge';
  if (type) badge.classList.add(type);
}

async function connect() {
  const addr = $('serverAddr').value.trim();
  if (!addr) return;
  if (!addr.includes(':')) {
    setStatus('error', '⚠️ 格式错误：请输入 IP:Port');
    return;
  }

  const url = `http://${addr}`;
  $('connectBtn').disabled = true;
  setStatus('connecting', '🟡 连接中…');

  try {
    const info = await client.connect(url);

    // Show device info
    const di = $('deviceInfo');
    di.textContent = `→ ${info.name ?? '?'} · ${info.version ?? '?'}`;
    di.classList.add('visible');

    // Hide placeholder, show video
    $('placeholder').style.display = 'none';

    // Toggle buttons
    $('connectBtn').style.display = 'none';
    $('disconnectBtn').style.display = 'inline-block';

    setStatus('connected', `🟢 已连接 · ${info.name ?? addr}`);
  } catch (e) {
    setStatus('error', `🔴 ${e.message}`);
  } finally {
    $('connectBtn').disabled = false;
  }
}

function disconnect() {
  client.disconnect();
  $('placeholder').style.display = '';
  $('deviceInfo').classList.remove('visible');
  $('deviceInfo').textContent = '';
  $('connectBtn').style.display = 'inline-block';
  $('disconnectBtn').style.display = 'none';
  $('stats').classList.remove('visible');
  setStatus('', '空闲');
}

// Auto-connect to default address on load (for quick testing)
window.addEventListener('load', () => {
  setTimeout(() => {
    const addr = $('serverAddr').value.trim();
    if (addr) connect();
  }, 300);
});
