/**
 * KodaClaw Browser Bridge — Popup 交互逻辑
 *
 * 职责：
 *   1. 加载保存的配置并填充 UI
 *   2. 显示真实连接状态（来自 background）
 *   3. 显示已配对设备信息（deviceId / pairedAt）
 *   4. 配对按钮触发完整配对流程
 *   5. 断开按钮
 *   6. Toast 通知
 */

// ============================================================
// DOM 元素引用
// ============================================================

const elStatusDot    = document.getElementById('statusDot');
const elStatusText   = document.getElementById('statusText');
const elDeviceInfo   = document.getElementById('deviceInfo');
const elDeviceId     = document.getElementById('deviceId');
const elPairedAt     = document.getElementById('pairedAt');
const elGatewayAddr  = document.getElementById('gatewayAddress');
const elPairingToken = document.getElementById('pairingToken');
const elTokenStatus  = document.getElementById('tokenStatus');
const btnPair        = document.getElementById('btnPair');
const btnDisconnect  = document.getElementById('btnDisconnect');
const toastContainer = document.getElementById('toastContainer');

// ============================================================
// 状态映射
// ============================================================

const STATUS_MAP = {
  disconnected: { text: '未连接',   dotClass: '',            paired: false },
  connecting:   { text: '连接中…', dotClass: 'connecting',  paired: false },
  connected:    { text: '已连接',   dotClass: 'connected',   paired: true  },
  reconnecting: { text: '重连中…', dotClass: 'reconnecting', paired: false },
};

// ============================================================
// Toast
// ============================================================

/**
 * 显示短暂的 toast 通知
 * @param {string} message
 * @param {'info'|'success'|'error'} level
 * @param {number} duration  毫秒，默认 2500
 */
function showToast(message, level = 'info', duration = 2500) {
  const el = document.createElement('div');
  el.className = `toast ${level}`;
  el.textContent = message;
  toastContainer.appendChild(el);

  // 触发过渡
  requestAnimationFrame(() => {
    requestAnimationFrame(() => el.classList.add('show'));
  });

  setTimeout(() => {
    el.classList.remove('show');
    setTimeout(() => el.remove(), 250);
  }, duration);
}

// ============================================================
// UI 更新
// ============================================================

function updateStatusUI(status) {
  const info = STATUS_MAP[status] || STATUS_MAP.disconnected;

  elStatusDot.className = 'status-dot';
  if (info.dotClass) elStatusDot.classList.add(info.dotClass);
  elStatusText.textContent = info.text;

  btnPair.disabled      = status === 'connected' || status === 'connecting';
  btnDisconnect.disabled = status === 'disconnected';
}

function updateDeviceInfo(deviceId, pairedAt) {
  if (deviceId) {
    elDeviceInfo.classList.add('visible');
    elDeviceId.textContent = deviceId.slice(0, 8) + '…';
    elDeviceId.title = deviceId;
    if (pairedAt) {
      elPairedAt.textContent = '配对于 ' + new Date(pairedAt).toLocaleString();
    }
    elTokenStatus.textContent = '✅';
  } else {
    elDeviceInfo.classList.remove('visible');
    elTokenStatus.textContent = '⚠️';
  }
}

// ============================================================
// 配置加载
// ============================================================

async function loadSavedConfig() {
  try {
    const result = await chrome.storage.local.get(['kodaclaw_config', 'kodaclaw_credentials']);
    const cfg   = result.kodaclaw_config || {};
    const creds = result.kodaclaw_credentials || null;

    if (cfg.gatewayUrl) {
      // 从 URL 提取 host:port 显示在输入框
      try {
        const url = new URL(cfg.gatewayUrl);
        elGatewayAddr.value = url.host;
      } catch {
        elGatewayAddr.value = cfg.gatewayUrl;
      }
    }

    updateDeviceInfo(creds ? creds.deviceId : null, creds ? creds.pairedAt : null);
  } catch (err) {
    console.error('[KodaClaw Popup] 配置加载失败:', err);
  }
}

async function fetchConnectionStatus() {
  try {
    const resp = await chrome.runtime.sendMessage({ action: 'getStatus' });
    if (resp) {
      updateStatusUI(resp.status);
      if (resp.deviceId) {
        updateDeviceInfo(resp.deviceId, resp.pairedAt);
      }
    }
  } catch {
    updateStatusUI('disconnected');
  }
}

// ============================================================
// 事件处理
// ============================================================

btnPair.addEventListener('click', async () => {
  const rawAddr = elGatewayAddr.value.trim();
  const token   = elPairingToken.value.trim();

  if (!rawAddr) { showToast('请输入 Gateway 地址', 'error'); return; }
  if (!token)   { showToast('请输入配对 Token', 'error'); return; }

  // 规范化 gatewayUrl
  let gatewayUrl = rawAddr;
  if (!gatewayUrl.startsWith('http')) {
    gatewayUrl = 'http://' + gatewayUrl;
  }

  btnPair.disabled = true;
  showToast('正在配对…', 'info');

  try {
    const resp = await chrome.runtime.sendMessage({ action: 'pair', token, gatewayUrl });

    if (resp && resp.success) {
      showToast('配对成功！', 'success');
      updateDeviceInfo(resp.deviceId, Date.now());
      // 清空 token 输入框（一次性使用）
      elPairingToken.value = '';
    } else {
      showToast('配对失败：' + (resp?.error || '未知错误'), 'error', 4000);
      btnPair.disabled = false;
    }
  } catch (err) {
    showToast('配对失败：' + err.message, 'error', 4000);
    btnPair.disabled = false;
  }
});

btnDisconnect.addEventListener('click', async () => {
  btnDisconnect.disabled = true;
  try {
    await chrome.runtime.sendMessage({ action: 'disconnect' });
    updateStatusUI('disconnected');
    showToast('已断开连接', 'info');
  } catch (err) {
    showToast('断开失败：' + err.message, 'error');
  }
});

elGatewayAddr.addEventListener('change', async () => {
  const raw = elGatewayAddr.value.trim();
  if (!raw) return;
  const url = raw.startsWith('http') ? raw : 'http://' + raw;
  await chrome.storage.local.set({ kodaclaw_config: { gatewayUrl: url } });
});

// ============================================================
// 来自 background 的状态广播
// ============================================================

chrome.runtime.onMessage.addListener(message => {
  if (message.action === 'connectionStateChanged') {
    updateStatusUI(message.status);
  } else if (message.action === 'requireRepair') {
    showToast('设备认证失败，需要重新配对', 'error', 5000);
    updateStatusUI('disconnected');
    updateDeviceInfo(null, null);
  }
});

// ============================================================
// 初始化
// ============================================================

async function init() {
  await loadSavedConfig();
  await fetchConnectionStatus();
}

init();
