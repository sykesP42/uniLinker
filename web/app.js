// UniLinker Frontend
let refreshInterval = null;

// Tab switching
document.querySelectorAll('.tab-btn').forEach(btn => {
  btn.addEventListener('click', () => {
    document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
    document.querySelectorAll('.tab-panel').forEach(p => p.classList.remove('active'));
    btn.classList.add('active');
    const tabId = 'tab-' + btn.dataset.tab;
    document.getElementById(tabId)?.classList.add('active');
  });
});

// Device list refresh
async function refreshDevices() {
  try {
    const bridge = window.chrome?.webview?.hostObjects?.bridge;
    if (!bridge) {
      document.getElementById('statusText').textContent = '🔴 No bridge';
      return;
    }
    const json = await bridge.GetDevices();
    const devices = JSON.parse(json);
    const el = document.getElementById('deviceList');
    if (!el) return;

    if (!devices || devices.length === 0) {
      el.innerHTML = '<p class="placeholder">No devices found on LAN</p>';
    } else {
      el.innerHTML = devices.map(d =>
        `<div class="device-card">
          <span>🖥️ <strong>${d.Name}</strong> (${d.IpAddress})</span>
          <span class="device-state">${d.State === 0 ? 'Found' : d.State === 1 ? 'Connected' : 'Offline'}</span>
          <button onclick="window.chrome.webview.hostObjects.bridge.WatchDevice('${d.Id}')">Watch</button>
        </div>`
      ).join('');
    }

    const status = JSON.parse(await bridge.GetStatus());
    document.getElementById('statusText').textContent =
      `🟢 ${status.deviceName} | ${status.peers} peers`;
    document.getElementById('connectionStatus').textContent = status.status;
  } catch (e) {
    console.error('Refresh error:', e);
  }
}

// Start polling
refreshDevices();
refreshInterval = setInterval(refreshDevices, 3000);

// Screen mirror buttons
document.getElementById('btnStartShare')?.addEventListener('click', async () => {
  const bridge = window.chrome?.webview?.hostObjects?.bridge;
  if (bridge) {
    await bridge.StartSharing('{}');
    document.getElementById('btnStartShare').disabled = true;
    document.getElementById('btnStopShare').disabled = false;
  }
});

document.getElementById('btnStopShare')?.addEventListener('click', async () => {
  const bridge = window.chrome?.webview?.hostObjects?.bridge;
  if (bridge) {
    await bridge.StopSharing();
    document.getElementById('btnStartShare').disabled = false;
    document.getElementById('btnStopShare').disabled = true;
  }
});
