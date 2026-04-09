/**
 * KodaClaw Browser Bridge — Service Worker (background.js)
 *
 * 实现的核心功能：
 *   1. 配对流程（ECDSA P-256 + ECDH P-256 + HKDF-SHA256）
 *   2. 重连认证（HMAC-SHA256）
 *   3. WebSocket 连接管理（心跳 / 指数退避重连）
 *   4. BridgeRequest 处理（CDP Phase 1 操作）
 *   5. Popup 消息路由
 *
 * 加密约束：全部使用 Web Crypto API（crypto.subtle），不引入第三方库。
 * Service Worker 约束：不使用 top-level await。
 */

// ============================================================
// 常量
// ============================================================

const PROTOCOL_VERSION = '1.0';
const WS_SUBPROTOCOL = 'kodaclaw.bridge.v1';
const HEARTBEAT_INTERVAL_MS = 30_000;
const HEARTBEAT_TIMEOUT_MS = 90_000;
const RECONNECT_BASE_DELAY_MS = 2_000;
const RECONNECT_MAX_DELAY_MS = 60_000;
const DEBUGGER_IDLE_DETACH_MS = 5_000;
const TRANSIENT_ACTION_RETRY_DELAY_MS = 400;
const HKDF_SALT = 'kodaclaw-bridge-v1';
const PRELOAD_SCRIPT_PATHS = [
  'content/stealth.js',
  'content/dom-snapshot.js',
  'content/evaluate-sandbox.js',
  'content/data-extract.js',
];
const TRANSIENT_RETRYABLE_ACTIONS = new Set([
  'snapshot',
  'screenshot',
  'get_url',
  'evaluate',
  'evaluate_dom',
  'extract_links',
  'extract_results',
  'cookies',
  'form_state',
  'wait',
]);

// ============================================================
// 辅助：Base64 / 字节 转换
// ============================================================

function ab2b64(buf) {
  const bytes = new Uint8Array(buf);
  let s = '';
  for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
  return btoa(s);
}

function b64toAb(b64) {
  const std = b64.replace(/-/g, '+').replace(/_/g, '/');
  const binary = atob(std);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes.buffer;
}

function ab2b64url(buf) {
  return ab2b64(buf).replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
}

function textEncode(str) {
  return new TextEncoder().encode(str);
}

function generateUuidV4() {
  return ([1e7] + -1e3 + -4e3 + -8e3 + -1e11).replace(/[018]/g, c =>
    (c ^ (crypto.getRandomValues(new Uint8Array(1))[0] & (15 >> (c / 4)))).toString(16)
  );
}

// ============================================================
// 辅助：ASN.1 DER ↔ IEEE P1363 签名格式转换
// （Web Crypto 使用 P1363，C# 使用 DER/Rfc3279DerSequence）
// ============================================================

/**
 * IEEE P1363（r||s, 各 32 字节）→ ASN.1 DER SEQUENCE { INTEGER r, INTEGER s }
 * @param {ArrayBuffer} p1363Buf
 * @returns {ArrayBuffer}
 */
function p1363ToDer(p1363Buf) {
  const p = new Uint8Array(p1363Buf);
  const r = p.slice(0, 32);
  const s = p.slice(32, 64);

  // 若最高位为 1，需在前补 0x00 保证为正整数
  const rPad = r[0] & 0x80 ? new Uint8Array([0x00, ...r]) : r;
  const sPad = s[0] & 0x80 ? new Uint8Array([0x00, ...s]) : s;

  const rDer = new Uint8Array([0x02, rPad.length, ...rPad]);
  const sDer = new Uint8Array([0x02, sPad.length, ...sPad]);
  const content = new Uint8Array([...rDer, ...sDer]);

  return new Uint8Array([0x30, content.length, ...content]).buffer;
}

/**
 * ASN.1 DER SEQUENCE → IEEE P1363（r||s, 各 32 字节）
 * @param {ArrayBuffer} derBuf
 * @returns {ArrayBuffer}
 */
function derToP1363(derBuf) {
  const d = new Uint8Array(derBuf);
  let i = 2; // 跳过 0x30 和总长度字节

  // 解析 r
  i++; // 0x02
  const rLen = d[i++];
  const r = d.slice(i, i + rLen);
  i += rLen;

  // 解析 s
  i++; // 0x02
  const sLen = d[i++];
  const s = d.slice(i, i + sLen);

  // 去掉前导零，左对齐至 32 字节
  const rTrim = r[0] === 0 ? r.slice(1) : r;
  const sTrim = s[0] === 0 ? s.slice(1) : s;

  const rPad = new Uint8Array(32);
  const sPad = new Uint8Array(32);
  rPad.set(rTrim, 32 - rTrim.length);
  sPad.set(sTrim, 32 - sTrim.length);

  const result = new Uint8Array(64);
  result.set(rPad);
  result.set(sPad, 32);
  return result.buffer;
}

// ============================================================
// 加密模块
// ============================================================

/** 生成 ECDSA P-256 密钥对（用于设备身份签名） */
async function generateEcdsaKeyPair() {
  return crypto.subtle.generateKey(
    { name: 'ECDSA', namedCurve: 'P-256' },
    true,
    ['sign', 'verify']
  );
}

/** 生成 ECDH P-256 密钥对（用于密钥协商） */
async function generateEcdhKeyPair() {
  return crypto.subtle.generateKey(
    { name: 'ECDH', namedCurve: 'P-256' },
    true,
    ['deriveBits']
  );
}

/** 导出公钥为 SubjectPublicKeyInfo（SPKI）base64，与 C# ImportSubjectPublicKeyInfo 兼容 */
async function exportSpkiBase64(key) {
  const buf = await crypto.subtle.exportKey('spki', key);
  return ab2b64(buf);
}

/** 导入 ECDSA P-256 公钥（SPKI base64） */
async function importEcdsaSpki(b64) {
  return crypto.subtle.importKey(
    'spki',
    b64toAb(b64),
    { name: 'ECDSA', namedCurve: 'P-256' },
    false,
    ['verify']
  );
}

/** 导入 ECDH P-256 公钥（SPKI base64） */
async function importEcdhSpki(b64) {
  return crypto.subtle.importKey(
    'spki',
    b64toAb(b64),
    { name: 'ECDH', namedCurve: 'P-256' },
    false,
    []
  );
}

/**
 * ECDSA P-256 签名 → DER base64
 * Web Crypto 产生 P1363 格式，C# 期望 DER 格式
 * @param {CryptoKey} privateKey
 * @param {ArrayBuffer} data
 * @returns {Promise<string>} DER base64
 */
async function ecdsaSignDer(privateKey, data) {
  const p1363 = await crypto.subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, privateKey, data);
  return ab2b64(p1363ToDer(p1363));
}

/**
 * ECDSA P-256 验签（签名为 DER base64，C# 产出）
 * @param {CryptoKey} publicKey
 * @param {ArrayBuffer} data
 * @param {string} signatureDerBase64
 * @returns {Promise<boolean>}
 */
async function ecdsaVerifyDer(publicKey, data, signatureDerBase64) {
  const p1363 = derToP1363(b64toAb(signatureDerBase64));
  return crypto.subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, publicKey, p1363, data);
}

/**
 * ECDH P-256 + HKDF-SHA256 推导共享密钥
 *
 * 与 C# 端完全对齐：
 *   sharedSecret = SHA256(ECDH_Z)        // C# DeriveKeyFromHash(SHA256, null, null)
 *   sharedKey = HKDF(salt, sharedSecret, info=deviceId, L=32)
 *
 * @param {CryptoKey} privateKey  本端 ECDH 私钥
 * @param {CryptoKey} publicKey   对端 ECDH 公钥
 * @param {string} deviceId
 * @returns {Promise<string>} sharedKey base64
 */
async function deriveSharedKey(privateKey, publicKey, deviceId) {
  // Step 1: ECDH 原始共享秘密 Z（X 坐标，32 字节）
  const z = await crypto.subtle.deriveBits({ name: 'ECDH', public: publicKey }, privateKey, 256);

  // Step 2: SHA-256(Z)，与 C# DeriveKeyFromHash(SHA256, null, null) 保持一致
  const hashedZ = await crypto.subtle.digest('SHA-256', z);

  // Step 3: HKDF-SHA256
  const hkdfKey = await crypto.subtle.importKey('raw', hashedZ, 'HKDF', false, ['deriveBits']);
  const derived = await crypto.subtle.deriveBits(
    {
      name: 'HKDF',
      hash: 'SHA-256',
      salt: textEncode(HKDF_SALT),
      info: textEncode(deviceId),
    },
    hkdfKey,
    256
  );

  return ab2b64(derived);
}

/**
 * HMAC-SHA256（用于重连认证），返回 base64url 编码
 * @param {string} sharedKeyBase64
 * @param {string} message  deviceId + timestamp + nonce 拼接
 * @returns {Promise<string>}
 */
async function computeHmac(sharedKeyBase64, message) {
  const keyMaterial = await crypto.subtle.importKey(
    'raw',
    b64toAb(sharedKeyBase64),
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign']
  );
  const sig = await crypto.subtle.sign('HMAC', keyMaterial, textEncode(message));
  return ab2b64url(sig);
}

// ============================================================
// 存储管理
// ============================================================

/** @typedef {{ deviceId:string, sharedKey:string, gwEcdsaPubKey:string, ecdsaPrivKeyJwk:object, pairedAt:number, sessionToken?:string }} DeviceCredentials */

/** @returns {Promise<DeviceCredentials|null>} */
async function loadCredentials() {
  const r = await chrome.storage.local.get(['kodaclaw_credentials']);
  return r.kodaclaw_credentials || null;
}

/** @param {DeviceCredentials} creds */
async function saveCredentials(creds) {
  await chrome.storage.local.set({ kodaclaw_credentials: creds });
}

async function clearCredentials() {
  await chrome.storage.local.remove(['kodaclaw_credentials']);
}

/** @returns {Promise<{gatewayUrl:string}>} */
async function loadConfig() {
  const r = await chrome.storage.local.get(['kodaclaw_config']);
  return r.kodaclaw_config || { gatewayUrl: 'http://localhost:5076' };
}

async function saveConfig(cfg) {
  await chrome.storage.local.set({ kodaclaw_config: cfg });
}

// ============================================================
// 配对流程（REST-based, 与 DevicePairingService 对齐）
// ============================================================

/**
 * 执行完整配对握手
 *
 * REST 调用：
 *   POST {gateway}/api/browser/pairing/challenge  { token }
 *     → { challenge, gwEcdsaPubKey, gwSignature, gwEcdhPubKey }
 *   POST {gateway}/api/browser/pairing/complete
 *     { token, extEcdsaPubKey, extSignature, extEcdhPubKey, deviceId, label }
 *     → { sessionToken }
 *
 * @param {string} gatewayUrl  例如 "http://localhost:5076"
 * @param {string} token       用户输入的配对 Token
 * @returns {Promise<{deviceId:string, sessionToken:string}>}
 */
async function performPairing(gatewayUrl, token) {
  // ── Step A：从 Gateway 获取 challenge + 公钥 ──────────────────────────
  const challengeResp = await fetch(`${gatewayUrl}/api/browser/pairing/challenge`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ token }),
  });

  if (!challengeResp.ok) {
    const txt = await challengeResp.text();
    throw new Error(`配对失败（获取 challenge）：${challengeResp.status} ${txt}`);
  }

  const { challenge, gwEcdsaPubKey, gwSignature, gwEcdhPubKey } = await challengeResp.json();

  if (!challenge || !gwEcdsaPubKey || !gwSignature || !gwEcdhPubKey) {
    throw new Error('配对失败：Gateway 返回数据不完整');
  }

  // ── Step B：验证 Gateway 签名 ─────────────────────────────────────────
  const challengeBytes = b64toAb(challenge); // 原始 32 字节 challenge
  const gwEcdsaKey = await importEcdsaSpki(gwEcdsaPubKey);
  const sigValid = await ecdsaVerifyDer(gwEcdsaKey, challengeBytes, gwSignature);
  if (!sigValid) {
    throw new Error('配对失败：Gateway 签名验证未通过，可能遭受中间人攻击');
  }

  // ── Step C：生成扩展 ECDSA 密钥对并签名 challenge ─────────────────────
  const ecdsaKeyPair = await generateEcdsaKeyPair();
  const extEcdsaPubKey = await exportSpkiBase64(ecdsaKeyPair.publicKey);
  const extSignature = await ecdsaSignDer(ecdsaKeyPair.privateKey, challengeBytes);

  // ── Step D：生成扩展 ECDH 密钥对 ──────────────────────────────────────
  const ecdhKeyPair = await generateEcdhKeyPair();
  const extEcdhPubKey = await exportSpkiBase64(ecdhKeyPair.publicKey);

  // ── Step E：生成 Device ID ────────────────────────────────────────────
  const deviceId = generateUuidV4();

  // ── Step F：发送配对完成请求 ──────────────────────────────────────────
  const completeResp = await fetch(`${gatewayUrl}/api/browser/pairing/complete`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      token,
      extEcdsaPubKey,
      extSignature,
      extEcdhPubKey,
      deviceId,
      label: 'KodaClaw Browser Bridge',
    }),
  });

  if (!completeResp.ok) {
    const txt = await completeResp.text();
    throw new Error(`配对失败（complete）：${completeResp.status} ${txt}`);
  }

  const { sessionToken: initialSessionToken } = await completeResp.json();
  if (!initialSessionToken) {
    throw new Error('配对失败：未收到 sessionToken');
  }

  // ── Step G：派生共享密钥并持久化 ──────────────────────────────────────
  const gwEcdhKey = await importEcdhSpki(gwEcdhPubKey);
  const sharedKey = await deriveSharedKey(ecdhKeyPair.privateKey, gwEcdhKey, deviceId);

  // 导出 ECDSA 私钥（JWK）以便后续重连时仍可使用（如需重新签名）
  const ecdsaPrivKeyJwk = await crypto.subtle.exportKey('jwk', ecdsaKeyPair.privateKey);

  await saveCredentials({
    deviceId,
    sharedKey,
    gwEcdsaPubKey,
    ecdsaPrivKeyJwk,
    pairedAt: Date.now(),
    sessionToken: initialSessionToken,
  });

  console.log('[KodaClaw] 配对成功，deviceId =', deviceId);
  return { deviceId, sessionToken: initialSessionToken };
}

// ============================================================
// 状态变量
// ============================================================

/** @type {WebSocket|null} */
let ws = null;
/** @type {"disconnected"|"connecting"|"connected"|"reconnecting"} */
let connectionState = 'disconnected';
let reconnectAttempt = 0;
let reconnectTimerId = null;
let heartbeatTimerId = null;
let heartbeatTimeoutId = null;
/** @type {string|null} 当前会话 sessionToken */
let currentSessionToken = null;
/** @type {string|null} 当前 Gateway URL */
let currentGatewayUrl = null;

// ============================================================
// 模块：WebSocket 连接
// ============================================================

/** 建立 WebSocket 连接（含重连认证） */
async function connect() {
  if (ws && (ws.readyState === WebSocket.CONNECTING || ws.readyState === WebSocket.OPEN)) {
    return;
  }

  const config = await loadConfig();
  const gwUrl = config.gatewayUrl || 'http://localhost:5076';
  currentGatewayUrl = gwUrl;

  const wsUrl = gwUrl.replace(/^http/, 'ws');
  const creds = await loadCredentials();

  if (!creds) {
    // 未配对，等待用户主动配对
    connectionState = 'disconnected';
    broadcastConnectionState();
    return;
  }

  connectionState = 'connecting';
  broadcastConnectionState();

  try {
    ws = new WebSocket(`${wsUrl}/ws/bridge?deviceId=${encodeURIComponent(creds.deviceId)}`, WS_SUBPROTOCOL);
  } catch (err) {
    console.error('[KodaClaw] WebSocket 创建失败:', err);
    connectionState = 'disconnected';
    broadcastConnectionState();
    scheduleReconnect();
    return;
  }

  ws.addEventListener('open', () => {
    console.log('[KodaClaw] WebSocket 已连接');
    reconnectAttempt = 0;
    // 发送认证消息
    sendAuthMessage(creds).catch(err => {
      console.error('[KodaClaw] 认证失败:', err);
      ws.close(4001, 'Auth failed');
    });
  });

  ws.addEventListener('message', event => {
    handleIncomingMessage(event.data);
  });

  ws.addEventListener('close', event => {
    console.log('[KodaClaw] WebSocket 关闭:', event.code, event.reason);
    stopHeartbeat();
    connectionState = 'disconnected';
    broadcastConnectionState();
    if (event.code !== 1000) {
      scheduleReconnect();
    }
  });

  ws.addEventListener('error', err => {
    console.error('[KodaClaw] WebSocket 错误:', err);
  });
}

function disconnect() {
  stopHeartbeat();
  cancelReconnect();
  if (ws) {
    ws.close(1000, 'User disconnect');
    ws = null;
  }
  connectionState = 'disconnected';
  broadcastConnectionState();
}

function sendWsMessage(data) {
  if (!ws || ws.readyState !== WebSocket.OPEN) {
    console.warn('[KodaClaw] 无法发送消息，WebSocket 未就绪');
    return false;
  }
  try {
    ws.send(JSON.stringify(data));
    return true;
  } catch (err) {
    console.error('[KodaClaw] 发送失败:', err);
    return false;
  }
}

// ============================================================
// 模块：重连认证（HMAC-SHA256）
// ============================================================

/**
 * 发送 BridgeAuthRequest 认证消息
 * @param {DeviceCredentials} creds
 */
async function sendAuthMessage(creds) {
  const nonce = generateUuidV4();
  const timestamp = Date.now();
  // 签名输入 = deviceId + timestamp + nonce（与 C# DeviceAuthService 保持一致）
  const message = creds.deviceId + timestamp.toString() + nonce;
  const hmac = await computeHmac(creds.sharedKey, message);

  sendWsMessage({
    deviceId: creds.deviceId,
    timestamp,
    nonce,
    hmac,
  });

  console.log('[KodaClaw] 认证消息已发送');
}

// ============================================================
// 模块：消息处理
// ============================================================

/**
 * 处理来自 WebSocket 的消息
 * 消息类型由字段存在性判断：
 *   - 含 "ok"      → BridgeResponse（认证结果或其他响应）
 *   - 含 "action"  → BridgeRequest（Gateway 下发命令）
 *   - 含 "type"="pong" → 心跳响应（兜底）
 */
function handleIncomingMessage(rawData) {
  let msg;
  try {
    msg = JSON.parse(rawData);
  } catch (err) {
    console.error('[KodaClaw] 消息解析失败:', err);
    return;
  }

  // 任何消息均重置心跳超时
  resetHeartbeatTimeout();

  if (Object.prototype.hasOwnProperty.call(msg, 'ok')) {
    // BridgeResponse（认证结果）
    handleBridgeResponse(msg);
  } else if (msg.action) {
    // BridgeRequest（Gateway 下发操作指令）
    handleBridgeRequest(msg).catch(err => {
      console.error('[KodaClaw] BridgeRequest 处理失败:', err);
    });
  } else if (msg.type === 'pong') {
    // 兜底心跳响应
  } else {
    console.log('[KodaClaw] 未知消息:', msg);
  }
}

/**
 * 处理 BridgeResponse（含认证响应）
 * @param {{version:string, id:string, ok:boolean, data:any, error?:string}} msg
 */
function handleBridgeResponse(msg) {
  if (!msg.ok) {
    console.error('[KodaClaw] 收到失败响应:', msg.error);
    if (msg.error && msg.error.includes('BRIDGE_006')) {
      // 认证失败，清除凭证要求重新配对
      clearCredentials();
      broadcastEvent('requireRepair', { reason: msg.error });
    }
    return;
  }

  // 认证成功响应：data.sessionToken
  if (msg.data && msg.data.sessionToken) {
    currentSessionToken = msg.data.sessionToken;
    // 更新存储中的 sessionToken
    loadCredentials().then(creds => {
      if (creds) {
        creds.sessionToken = msg.data.sessionToken;
        saveCredentials(creds);
      }
    });
    console.log('[KodaClaw] 认证成功，session token 已更新');
    connectionState = 'connected';
    broadcastConnectionState();
    startHeartbeat();
  }
}

// ============================================================
// 模块：BridgeRequest 处理（CDP Phase 1）
// ============================================================

/**
 * 处理 Gateway 下发的 BridgeRequest
 * @param {{version:string, id:string, sessionId:string, action:string, tabId:string|null, payload:object, timeoutMs:number}} req
 */
async function handleBridgeRequest(req) {
  const { id, action, tabId, payload, timeoutMs = 30000 } = req;
  const actionTracker = createActionTracker();

  let result;
  try {
    result = await withTimeout(executeCdpActionWithRetry(action, tabId, payload, actionTracker), timeoutMs);
    sendWsMessage({
      version: PROTOCOL_VERSION,
      id,
      ok: true,
      data: result,
      error: null,
    });
  } catch (err) {
    const errorCode = classifyCdpError(err);
    sendWsMessage({
      version: PROTOCOL_VERSION,
      id,
      ok: false,
      data: null,
      error: `${errorCode}: ${err.message}`,
    });
  } finally {
    await finalizeTrackedDebuggerUsage(actionTracker);
  }
}

/**
 * 执行 CDP 操作
 * @param {string} action
 * @param {string|null} tabId
 * @param {object} payload
 * @returns {Promise<any>}
 */
async function executeCdpActionWithRetry(action, tabId, payload, tracker) {
  try {
    return await executeCdpAction(action, tabId, payload, tracker);
  } catch (err) {
    if (!shouldRetryCdpAction(action, err)) {
      throw err;
    }

    await sleep(getActionRetryDelayMs(action));
    return executeCdpAction(action, tabId, payload, tracker);
  }
}

async function executeCdpAction(action, tabId, payload, tracker) {
  switch (action) {
    case 'navigate':     return cdpNavigate(tabId, payload.url, tracker);
    case 'snapshot':     return cdpSnapshot(tabId, payload, tracker);
    case 'screenshot':   return cdpScreenshot(tabId, payload, tracker);
    case 'list_tabs':    return cdpListTabs();
    case 'get_url':      return cdpGetUrl(tabId, tracker);
    case 'evaluate':     return cdpEvaluate(tabId, payload.script, tracker);
    case 'evaluate_dom': return cdpEvaluateDom(tabId, payload, tracker);
    case 'extract_links': return cdpExtractLinks(tabId, payload, tracker);
    case 'extract_results': return cdpExtractResults(tabId, payload, tracker);
    case 'heartbeat':    return { status: 'ok' };
    case 'click':        return cdpClick(tabId, payload, tracker);
    case 'type':         return cdpType(tabId, payload, tracker);
    case 'scroll':       return cdpScroll(tabId, payload, tracker);
    case 'key_press':    return cdpKeyPress(tabId, payload, tracker);
    case 'go_back':      return cdpGoBack(tabId, tracker);
    case 'close_tab':    return cdpCloseTab(tabId);
    case 'switch_tab':      return cdpSwitchTab(payload);
    case 'evaluate_write':  return cdpEvaluateWrite(tabId, payload, tracker);
    case 'cookies':         return cdpCookies(tabId, payload, tracker);
    case 'form_state':      return cdpFormState(tabId, payload, tracker);
    case 'console':         return cdpConsole(tabId, payload);
    case 'upload_file':     return cdpUploadFile(tabId, payload, tracker);
    case 'wait':            return cdpWait(tabId, payload, tracker);
    case 'intercept':        return cdpIntercept(tabId, payload, tracker);
    case 'intercept_clear':  return cdpInterceptClear(tabId, tracker);
    case 'intercept_result': return cdpInterceptResult(tabId, payload);
    default:
      throw new Error(`不支持的 action：${action}`);
  }
}

// ── CDP 操作实现 ──────────────────────────────────────────────

/** 导航到 URL（tabId 为 null 时创建新标签页） */
async function cdpNavigate(tabId, url, tracker) {
  if (!url) throw new Error('缺少 url 参数');

  const chromeTabId = tabId ? parseInt(tabId, 10) : null;
  let tab;

  if (!chromeTabId) {
    // 创建新标签页
    tab = await chrome.tabs.create({ url });
  } else {
    tab = await chrome.tabs.update(chromeTabId, { url }).catch(() => null);
    if (!tab) throw new Error(`BRIDGE_002: Tab ${chromeTabId} 不存在`);
  }

  if (!Number.isFinite(tab.id)) {
    throw new Error('BRIDGE_002: 无法解析目标标签页');
  }

  beginTrackedTabOperation(tracker, tab.id);
  return { url: tab.url || url, tabId: String(tab.id) };
}

/** DOM 快照 */
async function cdpSnapshot(tabId, payload, tracker) {
  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId);
  await ensureDebuggerAttached(chromeTabId);

  const framePath = normalizeFramePath(payload);
  const selector = payload && payload.selector ? payload.selector : null;
  const snapshotOptions = {};
  if (selector) {
    snapshotOptions.selector = selector;
  }
  if (framePath) {
    snapshotOptions.framePath = framePath;
  }
  const optionsLiteral = Object.keys(snapshotOptions).length > 0
    ? JSON.stringify(snapshotOptions)
    : 'null';

  const script = `(function(){
    if (!window.__kodaclaw_snapshot) {
      throw new Error('SNAPSHOT_HELPER_UNAVAILABLE: structured snapshot helper is not ready on this page. Retry once, or use evaluate_dom for targeted extraction.');
    }
    var result = window.__kodaclaw_snapshot(${optionsLiteral});
    return JSON.stringify(result === undefined ? null : result);
  })()`;

  return cdpEvalRaw(chromeTabId, script);
}

/** 截图 */
async function cdpScreenshot(tabId, payload, tracker) {
  const { format = 'jpeg', quality = 80, uploadToken, uploadUrl } = payload || {};
  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId);
  await ensureDebuggerAttached(chromeTabId);

  const params = { format, quality };

  const result = await new Promise((resolve, reject) => {
    chrome.debugger.sendCommand({ tabId: chromeTabId }, 'Page.captureScreenshot', params, r => {
      if (chrome.runtime.lastError) reject(new Error(chrome.runtime.lastError.message));
      else resolve(r);
    });
  });

  const dataB64 = result.data;

  // 若 Gateway 提供了 uploadToken，通过 HTTP 上传
  if (uploadToken && uploadUrl && currentGatewayUrl) {
    const mimeType = format === 'png' ? 'image/png' : 'image/jpeg';
    const binaryStr = atob(dataB64);
    const bytes = new Uint8Array(binaryStr.length);
    for (let i = 0; i < binaryStr.length; i++) bytes[i] = binaryStr.charCodeAt(i);

    const uploadResp = await fetch(`${currentGatewayUrl}${uploadUrl}`, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${uploadToken}`,
        'Content-Type': mimeType,
      },
      body: bytes.buffer,
    });

    if (!uploadResp.ok) {
      throw new Error(`截图上传失败：${uploadResp.status}`);
    }

    const { fileId } = await uploadResp.json();
    return `/api/browser/screenshot/${fileId}`;
  }

  // 无上传 token 时返回 base64 data URI
  return `data:image/${format};base64,${dataB64}`;
}

/** 列出所有标签页 */
async function cdpListTabs() {
  const tabs = await chrome.tabs.query({});
  return tabs.map(t => ({
    id: String(t.id),
    url: t.url || '',
    title: t.title || '',
    active: t.active,
    windowId: t.windowId,
  }));
}

/** 获取当前 URL */
async function cdpGetUrl(tabId, tracker) {
  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId);
  const tab = await chrome.tabs.get(chromeTabId).catch(() => null);
  if (!tab) {
    throw new Error(`BRIDGE_002: Tab ${chromeTabId} 不存在`);
  }

  return tab.url || '';
}

/** 只读 JS 求值 */
async function cdpEvaluate(tabId, script, tracker) {
  if (!script) throw new Error('缺少 script 参数');
  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId);
  await ensureDebuggerAttached(chromeTabId);

  // 使用沙箱求值（若已注入）
  const sandboxScript = `(function(){return window.__kodaclaw_evaluate?window.__kodaclaw_evaluate(${JSON.stringify(script)}):eval(${JSON.stringify(script)});})()`;
  return cdpEvalRaw(chromeTabId, sandboxScript);
}

async function cdpEvaluateDom(tabId, payload, tracker) {
  const script = payload && payload.script;
  if (!script) throw new Error('缺少 script 参数');
  const framePath = normalizeFramePath(payload);
  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId);
  await ensureDebuggerAttached(chromeTabId);

  if (!framePath) {
    return new Promise((resolve, reject) => {
      chrome.debugger.sendCommand(
        { tabId: chromeTabId },
        'Runtime.evaluate',
        {
          expression: script,
          returnByValue: true,
          awaitPromise: false,
          throwOnSideEffect: true,
        },
        result => {
          if (chrome.runtime.lastError) {
            reject(new Error(chrome.runtime.lastError.message));
            return;
          }
          if (result.exceptionDetails) {
            const details = result.exceptionDetails;
            const description = details.exception && details.exception.description;
            reject(new Error(description || details.text || '只读 DOM 求值失败'));
            return;
          }
          resolve(result.result && result.result.value !== undefined ? result.result.value : null);
        }
      );
    });
  }

  const frameAwareScript = buildFrameAwareExpression(framePath, `
    return __kcWindow.eval(${JSON.stringify(script)});
  `);
  return cdpEvalRaw(chromeTabId, frameAwareScript);
}

/** 提取链接列表（高层只读动作） */
async function cdpExtractLinks(tabId, payload, tracker) {
  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId);
  await ensureDebuggerAttached(chromeTabId);

  const framePath = normalizeFramePath(payload);
  const options = Object.assign({}, payload || {});
  if (framePath) {
    options.framePath = framePath;
  } else {
    delete options.framePath;
    delete options.frameSelector;
  }

  const optionsLiteral = JSON.stringify(options);
  const script = `(function(){
    if (!window.__kodaclaw_extract_links) {
      throw new Error('EXTRACT_HELPER_UNAVAILABLE: link extraction helper is not ready on this page. Retry once after reloading the extension or page.');
    }
    return window.__kodaclaw_extract_links(${optionsLiteral});
  })()`;

  return cdpEvalRaw(chromeTabId, script);
}

/** 提取搜索/目录结果列表（高层只读动作） */
async function cdpExtractResults(tabId, payload, tracker) {
  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId);
  await ensureDebuggerAttached(chromeTabId);

  const framePath = normalizeFramePath(payload);
  const options = Object.assign({}, payload || {});
  if (framePath) {
    options.framePath = framePath;
  } else {
    delete options.framePath;
    delete options.frameSelector;
  }

  const optionsLiteral = JSON.stringify(options);
  const script = `(function(){
    if (!window.__kodaclaw_extract_results) {
      throw new Error('EXTRACT_HELPER_UNAVAILABLE: result extraction helper is not ready on this page. Retry once after reloading the extension or page.');
    }
    return window.__kodaclaw_extract_results(${optionsLiteral});
  })()`;

  return cdpEvalRaw(chromeTabId, script);
}

// ── CDP 底层辅助 ──────────────────────────────────────────────

/**
 * Runtime.evaluate 原始调用
 * @param {number} tabId
 * @param {string} script
 * @returns {Promise<any>}
 */
function cdpEvalRaw(tabId, script) {
  return new Promise((resolve, reject) => {
    chrome.debugger.sendCommand(
      { tabId },
      'Runtime.evaluate',
      { expression: script, returnByValue: true, awaitPromise: true },
      result => {
        if (chrome.runtime.lastError) {
          reject(new Error(chrome.runtime.lastError.message));
          return;
        }
        if (result.exceptionDetails) {
          const details = result.exceptionDetails;
          const description = details.exception && details.exception.description;
          reject(new Error(description || details.text || 'JS 执行异常'));
          return;
        }
        resolve(result.result && result.result.value !== undefined ? result.result.value : null);
      }
    );
  });
}

/**
 * CDP 命令 Promise 封装
 * @param {number} tabId
 * @param {string} method
 * @param {object} params
 * @returns {Promise<any>}
 */
function cdpSend(tabId, method, params = {}) {
  return new Promise((resolve, reject) => {
    chrome.debugger.sendCommand({ tabId }, method, params, result => {
      if (chrome.runtime.lastError) reject(new Error(chrome.runtime.lastError.message));
      else resolve(result);
    });
  });
}

function normalizeFramePath(payload) {
  if (payload && Array.isArray(payload.framePath)) {
    const normalized = payload.framePath
      .filter(selector => typeof selector === 'string')
      .map(selector => selector.trim())
      .filter(selector => selector.length > 0);
    return normalized.length > 0 ? normalized : null;
  }

  if (payload && typeof payload.frameSelector === 'string') {
    const selector = payload.frameSelector.trim();
    return selector ? [selector] : null;
  }

  return null;
}

function buildFrameHelpersExpression(framePath) {
  const framePathLiteral = JSON.stringify(framePath || []);
  return `
    function __kcResolveFrameContext() {
      var selectors = ${framePathLiteral};
      var currentWindow = window;
      var currentDocument = document;

      for (var i = 0; i < selectors.length; i++) {
        var selector = selectors[i];
        if (typeof selector !== 'string' || selector.trim() === '') {
          throw new Error('FRAME_PATH_INVALID: iframe selector at index ' + i + ' must be a non-empty string.');
        }

        var frameEl = currentDocument.querySelector(selector);
        if (!frameEl) {
          throw new Error('FRAME_NOT_FOUND: iframe selector "' + selector + '" did not match any iframe.');
        }

        var tagName = frameEl.tagName ? frameEl.tagName.toLowerCase() : '';
        if (tagName !== 'iframe' && tagName !== 'frame') {
          throw new Error('FRAME_NOT_IFRAME: selector "' + selector + '" matched a ' + (tagName || 'node') + ', not an iframe.');
        }

        var nextWindow;
        try {
          nextWindow = frameEl.contentWindow;
        } catch (_) {
          throw new Error('FRAME_ACCESS_DENIED: iframe selector "' + selector + '" is not same-origin.');
        }

        if (!nextWindow) {
          throw new Error('FRAME_UNAVAILABLE: iframe selector "' + selector + '" has no active window.');
        }

        var nextDocument;
        try {
          nextDocument = nextWindow.document;
        } catch (_) {
          throw new Error('FRAME_ACCESS_DENIED: iframe selector "' + selector + '" is not same-origin.');
        }

        if (!nextDocument) {
          throw new Error('FRAME_UNAVAILABLE: iframe selector "' + selector + '" has no active document.');
        }

        currentWindow = nextWindow;
        currentDocument = nextDocument;
      }

      return {
        window: currentWindow,
        document: currentDocument,
        framePath: selectors,
      };
    }

    function __kcGetAbsoluteRect(element) {
      if (!element) {
        return null;
      }

      var rect = element.getBoundingClientRect();
      var left = rect.left;
      var top = rect.top;
      var currentWindow = element.ownerDocument && element.ownerDocument.defaultView;

      while (currentWindow && currentWindow !== window.top) {
        var frameEl = currentWindow.frameElement;
        if (!frameEl) {
          break;
        }

        var frameRect = frameEl.getBoundingClientRect();
        left += frameRect.left;
        top += frameRect.top;
        currentWindow = frameEl.ownerDocument && frameEl.ownerDocument.defaultView;
      }

      return {
        left: left,
        top: top,
        width: rect.width,
        height: rect.height,
      };
    }
  `;
}

function buildFrameAwareExpression(framePath, bodySource) {
  return `(function(){
    ${buildFrameHelpersExpression(framePath)}
    var __kcContext = __kcResolveFrameContext();
    var __kcWindow = __kcContext.window;
    var __kcDocument = __kcContext.document;
    var __kcFramePath = __kcContext.framePath;
    ${bodySource}
  })()`;
}

function sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

function shouldRetryCdpAction(action, err) {
  return TRANSIENT_RETRYABLE_ACTIONS.has(action) && isTransientDocumentError(err);
}

function getActionRetryDelayMs(action) {
  switch (action) {
    case 'wait':
      return 200;
    case 'snapshot':
    case 'extract_links':
    case 'extract_results':
      return 300;
    default:
      return TRANSIENT_ACTION_RETRY_DELAY_MS;
  }
}

function isTransientDocumentError(err) {
  const message = err && err.message ? err.message : '';
  return [
    'Execution context was destroyed',
    'Cannot find context with specified id',
    'Cannot find object with given id',
    'Inspected target navigated or closed',
    'Loader has changed while resolving nodes',
    'No frame with given id',
    'Frame with the given id was not found',
  ].some(fragment => message.includes(fragment));
}

function createActionTracker() {
  return {
    touchedTabIds: new Set(),
    keepAttachedTabIds: new Set(),
    detachImmediatelyTabIds: new Set(),
  };
}

// ── CDP Phase 2 写操作 ────────────────────────────────────────

/** 点击元素（通过 data-kc-index 定位） */
async function cdpClick(tabId, payload, tracker) {
  const framePath = normalizeFramePath(payload);
  const { elementIndex, offsetX = 0, offsetY = 0 } = payload || {};
  if (elementIndex === undefined) throw new Error('缺少 elementIndex 参数');
  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId);
  await ensureDebuggerAttached(chromeTabId);

  const evalResult = await cdpSend(chromeTabId, 'Runtime.evaluate', {
    expression: buildFrameAwareExpression(framePath, `
      return __kcDocument.querySelector('[data-kc-index="${elementIndex}"]');
    `),
    returnByValue: false,
  });
  if (!evalResult.result || evalResult.result.subtype === 'null' || !evalResult.result.objectId) {
    throw new Error(`未找到 elementIndex=${elementIndex} 的元素`);
  }

  const point = await cdpEvalRaw(chromeTabId, buildFrameAwareExpression(framePath, `
    var element = __kcDocument.querySelector('[data-kc-index="${elementIndex}"]');
    if (!element) {
      throw new Error('未找到 elementIndex=${elementIndex} 的元素');
    }

    var rect = __kcGetAbsoluteRect(element);
    if (!rect || rect.width <= 0 || rect.height <= 0) {
      throw new Error('目标元素不可见，无法点击');
    }

    return {
      x: rect.left + (rect.width / 2) + ${offsetX},
      y: rect.top + (rect.height / 2) + ${offsetY},
    };
  `));
  const x = point.x;
  const y = point.y;

  await cdpSend(chromeTabId, 'Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button: 'left', clickCount: 1 });
  await cdpSend(chromeTabId, 'Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button: 'left', clickCount: 1 });

  return { clicked: true, elementIndex };
}

/** 输入文本 */
async function cdpType(tabId, payload, tracker) {
  const framePath = normalizeFramePath(payload);
  const { elementIndex, text, clearFirst = false, delayMs = 0 } = payload || {};
  if (elementIndex === undefined) throw new Error('缺少 elementIndex 参数');
  if (text === undefined) throw new Error('缺少 text 参数');
  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId);
  await ensureDebuggerAttached(chromeTabId);

  await cdpEvalRaw(chromeTabId, buildFrameAwareExpression(framePath, `
    var el = __kcDocument.querySelector('[data-kc-index="${elementIndex}"]');
    if (!el) {
      throw new Error('未找到 elementIndex=${elementIndex} 的元素');
    }

    if (__kcWindow.frameElement && typeof __kcWindow.frameElement.focus === 'function') {
      try { __kcWindow.frameElement.focus(); } catch (_) {}
    }

    el.focus();
    return true;
  `));

  if (clearFirst) {
    await cdpSend(chromeTabId, 'Input.dispatchKeyEvent', { type: 'keyDown', key: 'a', code: 'KeyA', modifiers: 2 });
    await cdpSend(chromeTabId, 'Input.dispatchKeyEvent', { type: 'keyUp',   key: 'a', code: 'KeyA', modifiers: 2 });
    await cdpSend(chromeTabId, 'Input.dispatchKeyEvent', { type: 'keyDown', key: 'Backspace', code: 'Backspace', windowsVirtualKeyCode: 8 });
    await cdpSend(chromeTabId, 'Input.dispatchKeyEvent', { type: 'keyUp',   key: 'Backspace', code: 'Backspace', windowsVirtualKeyCode: 8 });
  }


  await cdpSend(chromeTabId, 'Input.insertText', { text });

  return { typed: true, elementIndex, charCount: text.length };
}

/** 滚动页面 */
async function cdpScroll(tabId, payload, tracker) {
  const framePath = normalizeFramePath(payload);
  const { direction, amount = 300 } = payload || {};
  if (!direction) throw new Error('缺少 direction 参数');
  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId);
  await ensureDebuggerAttached(chromeTabId);

  const target = payload && payload.elementIndex !== undefined
    ? `var __kcScrollTarget = __kcDocument.querySelector('[data-kc-index="${payload.elementIndex}"]'); if (!__kcScrollTarget) { throw new Error('未找到 elementIndex=${payload.elementIndex} 的元素'); }`
    : 'var __kcScrollTarget = __kcWindow;';
  let scrollStatement;
  switch (direction) {
    case 'up':     scrollStatement = `__kcScrollTarget.scrollBy(0, -${amount})`; break;
    case 'down':   scrollStatement = `__kcScrollTarget.scrollBy(0, ${amount})`;  break;
    case 'left':   scrollStatement = `__kcScrollTarget.scrollBy(-${amount}, 0)`; break;
    case 'right':  scrollStatement = `__kcScrollTarget.scrollBy(${amount}, 0)`;  break;
    case 'top':    scrollStatement = '__kcScrollTarget.scrollTo(0, 0)'; break;
    case 'bottom': scrollStatement = `__kcScrollTarget.scrollTo(0, (__kcScrollTarget.document ? __kcScrollTarget.document.body.scrollHeight : __kcScrollTarget.scrollHeight))`; break;
    default: throw new Error(`不支持的滚动方向：${direction}`);
  }

  await cdpEvalRaw(chromeTabId, buildFrameAwareExpression(framePath, `
    ${target}
    ${scrollStatement};
    return true;
  `));
  const pos = await cdpEvalRaw(chromeTabId, buildFrameAwareExpression(framePath, `
    return {
      scrollY: __kcWindow.scrollY,
      scrollX: __kcWindow.scrollX,
    };
  `));
  return { scrolled: true, scrollY: pos.scrollY, scrollX: pos.scrollX };
}

/** 按键（支持 "Enter"、"Control+A"、"Shift+Tab" 等） */
async function cdpKeyPress(tabId, payload, tracker) {
  const { key } = payload || {};
  if (!key) throw new Error('缺少 key 参数');
  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId);
  await ensureDebuggerAttached(chromeTabId);

  const parts = key.split('+');
  const mainKey = parts[parts.length - 1];

  let modifiers = 0;
  for (const m of parts.slice(0, -1)) {
    if (m === 'Alt')                    modifiers |= 1;
    if (m === 'Control' || m === 'Ctrl') modifiers |= 2;
    if (m === 'Meta' || m === 'Command') modifiers |= 4;
    if (m === 'Shift')                  modifiers |= 8;
  }

  const keyCodeMap = {
    Enter: 13, Tab: 9, Escape: 27, Backspace: 8, Delete: 46, Space: 32,
    ArrowUp: 38, ArrowDown: 40, ArrowLeft: 37, ArrowRight: 39,
    Home: 36, End: 35, PageUp: 33, PageDown: 34,
  };
  const windowsVirtualKeyCode = keyCodeMap[mainKey] !== undefined
    ? keyCodeMap[mainKey]
    : mainKey.toUpperCase().charCodeAt(0);
  const code = keyCodeMap[mainKey] !== undefined ? mainKey : `Key${mainKey.toUpperCase()}`;

  await cdpSend(chromeTabId, 'Input.dispatchKeyEvent', { type: 'keyDown', key: mainKey, code, windowsVirtualKeyCode, modifiers });
  await cdpSend(chromeTabId, 'Input.dispatchKeyEvent', { type: 'keyUp',   key: mainKey, code, windowsVirtualKeyCode, modifiers });

  return { pressed: true, key };
}

/** 后退 */
async function cdpGoBack(tabId, tracker) {
  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId);
  await ensureDebuggerAttached(chromeTabId);

  await cdpEvalRaw(chromeTabId, 'window.history.back()');
  await new Promise(r => setTimeout(r, 500));

  const url   = await cdpEvalRaw(chromeTabId, 'location.href');
  const title = await cdpEvalRaw(chromeTabId, 'document.title');
  return { url, title };
}

/** 关闭标签页（不需要 debugger） */
async function cdpCloseTab(tabId) {
  const chromeTabId = tabId ? parseInt(tabId, 10) : null;
  if (!chromeTabId) throw new Error('缺少 tabId');
  await new Promise((resolve, reject) => {
    chrome.tabs.remove(chromeTabId, () => {
      if (chrome.runtime.lastError) reject(new Error(chrome.runtime.lastError.message));
      else resolve();
    });
  });
  return { closed: true, tabId: String(chromeTabId) };
}

/** 切换标签页（不需要 debugger） */
async function cdpSwitchTab(payload) {
  const { tabId } = payload || {};
  if (!tabId) throw new Error('缺少 tabId 参数');
  const chromeTabId = parseInt(tabId, 10);
  await new Promise((resolve, reject) => {
    chrome.tabs.update(chromeTabId, { active: true }, () => {
      if (chrome.runtime.lastError) reject(new Error(chrome.runtime.lastError.message));
      else resolve();
    });
  });
  return { switched: true, currentTabId: String(chromeTabId) };
}

// ── CDP Phase 2 高级功能 ──────────────────────────────────────

/** 写入型 JS 执行（不走沙箱，高风险） */
async function cdpEvaluateWrite(tabId, payload, tracker) {
  const framePath = normalizeFramePath(payload);
  const { script } = payload || {};
  if (!script) throw new Error('缺少 script 参数');
  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId);
  await ensureDebuggerAttached(chromeTabId);

  const expression = framePath
    ? buildFrameAwareExpression(framePath, `
        return __kcWindow.eval(${JSON.stringify(script)});
      `)
    : script;

  return new Promise(resolve => {
    chrome.debugger.sendCommand(
      { tabId: chromeTabId },
      'Runtime.evaluate',
      { expression, returnByValue: true, awaitPromise: true },
      result => {
        if (chrome.runtime.lastError) {
          resolve({ value: null, error: chrome.runtime.lastError.message });
          return;
        }
        if (result.exceptionDetails) {
          resolve({ value: null, error: result.exceptionDetails.text || 'JS 执行异常' });
          return;
        }
        resolve({
          value: result.result && result.result.value !== undefined ? result.result.value : null,
          error: null,
        });
      }
    );
  });
}

/** 获取 Cookie 列表（value 脱敏为 "***"） */
async function cdpCookies(tabId, payload, tracker) {
  const { url } = payload || {};
  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId);
  await ensureDebuggerAttached(chromeTabId);

  const params = url ? { urls: [url] } : {};
  const result = await cdpSend(chromeTabId, 'Network.getCookies', params);
  const cookies = (result.cookies || []).map(c => ({
    name: c.name,
    value: '***',
    domain: c.domain,
    path: c.path,
    httpOnly: c.httpOnly,
    secure: c.secure,
    expires: Number.isFinite(c.expires) ? Math.trunc(c.expires) : null,
  }));
  return { cookies };
}

/** 获取表单元素状态 */
async function cdpFormState(tabId, payload, tracker) {
  const framePath = normalizeFramePath(payload);
  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId);
  await ensureDebuggerAttached(chromeTabId);

  const script = buildFrameAwareExpression(framePath, `
    if (window.__kodaclaw_snapshot) {
      try { window.__kodaclaw_snapshot({ framePath: __kcFramePath }); } catch (_) {}
    }
    var isVisible = function(el) {
      if (!el) return false;
      var style = __kcWindow.getComputedStyle(el);
      if (style.display === 'none' || style.visibility === 'hidden') return false;
      var rect = el.getBoundingClientRect();
      return rect.width > 0 && rect.height > 0;
    };
    var results = [];
    var els = __kcDocument.querySelectorAll('input, textarea, select');
    for (var i = 0; i < els.length; i++) {
      var el = els[i];
      if (!isVisible(el)) continue;
      var tag = el.tagName.toLowerCase();
      var elementIndex = el.getAttribute('data-kc-index') !== null
        ? el.getAttribute('data-kc-index')
        : String(i);
      var isCheckable = el.type === 'checkbox' || el.type === 'radio';
      var entry = {
        elementIndex: elementIndex,
        tagName: tag,
        type: el.type || null,
        name: el.name || null,
        id: el.id || null,
        value: (tag === 'input' && el.type === 'password') ? '***' : (el.value || null),
        checked: isCheckable ? el.checked : null,
        placeholder: el.placeholder || null,
        selectedOptions: null,
      };
      if (tag === 'select') {
        entry.selectedOptions = Array.from(el.selectedOptions).map(function(o){
          return { value: o.value, text: o.text };
        });
      }
      results.push(entry);
    }
    return { forms: results };
  `);

  return cdpEvalRaw(chromeTabId, script);
}

/** 获取控制台消息（支持 sinceTimestamp 过滤） */
async function cdpConsole(_tabId, payload) {
  const { sinceTimestamp } = payload || {};
  const messages = sinceTimestamp
    ? consoleMessages.filter(m => m.timestamp > sinceTimestamp)
    : consoleMessages.slice();
  return { messages };
}

/** 上传文件到 file input 元素 */
async function cdpUploadFile(tabId, payload, tracker) {
  const framePath = normalizeFramePath(payload);
  const { elementIndex, filePath } = payload || {};
  if (elementIndex === undefined) throw new Error('缺少 elementIndex 参数');
  if (!filePath) throw new Error('缺少 filePath 参数');
  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId);
  await ensureDebuggerAttached(chromeTabId);

  const evalResult = await cdpSend(chromeTabId, 'Runtime.evaluate', {
    expression: buildFrameAwareExpression(framePath, `
      return __kcDocument.querySelector('[data-kc-index="${elementIndex}"]');
    `),
    returnByValue: false,
  });
  if (!evalResult.result || evalResult.result.subtype === 'null' || !evalResult.result.objectId) {
    throw new Error(`未找到 elementIndex=${elementIndex} 的元素`);
  }

  // 验证必须为 file input
  const typeResult = await cdpSend(chromeTabId, 'Runtime.callFunctionOn', {
    objectId: evalResult.result.objectId,
    functionDeclaration: 'function(){ return this.type; }',
    returnByValue: true,
  });
  if (!typeResult.result || typeResult.result.value !== 'file') {
    throw new Error(`elementIndex=${elementIndex} 不是 file input 类型`);
  }

  const nodeResult = await cdpSend(chromeTabId, 'DOM.requestNode', { objectId: evalResult.result.objectId });
  await cdpSend(chromeTabId, 'DOM.setFileInputFiles', {
    files: [filePath],
    nodeId: nodeResult.nodeId,
  });

  const fileName = filePath.split(/[\\/]/).pop();
  return { uploaded: true, elementIndex, fileName };
}

/** 等待条件满足（轮询，每 200ms 检查一次） */
async function cdpWait(tabId, payload, tracker) {
  const framePath = normalizeFramePath(payload);
  const {
    condition,
    timeoutMs = 10000,
    durationMs,
    waitForSelector,
    waitUntil,
  } = payload || {};
  const start = Date.now();

  if (typeof durationMs === 'number' && durationMs > 0) {
    await new Promise(r => setTimeout(r, durationMs));
    return { waited: true, elapsedMs: Date.now() - start };
  }

  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId);
  await ensureDebuggerAttached(chromeTabId);

  const selector = waitForSelector || (condition === 'element' ? '[data-kc-index]' : null);
  const navigationTarget = waitUntil || (condition === 'navigation' ? 'load' : null);

  if (!selector && !navigationTarget) {
    return { waited: true, elapsedMs: 0 };
  }

  while (true) {
    const elapsed = Date.now() - start;
    if (elapsed >= timeoutMs) {
      return { waited: false, elapsedMs: elapsed, error: 'timeout' };
    }

    try {
      if (selector) {
        const found = await cdpEvalRaw(chromeTabId, buildFrameAwareExpression(framePath, `
          return __kcDocument.querySelector(${JSON.stringify(selector)}) !== null;
        `));
        if (found) return { waited: true, elapsedMs: Date.now() - start };
      } else if (navigationTarget) {
        const state = await cdpEvalRaw(chromeTabId, `document.readyState`);
        if (
          navigationTarget === 'domcontentloaded'
            ? state === 'interactive' || state === 'complete'
            : state === 'complete'
        ) {
          return { waited: true, elapsedMs: Date.now() - start };
        }
      } else {
        throw new Error(`不支持的 condition：${condition || navigationTarget}`);
      }
    } catch (err) {
      if (!err.message.includes('不支持的')) {
        // 页面正在跳转中可能抛出协议错误，忽略继续等待
      } else {
        throw err;
      }
    }

    await new Promise(r => setTimeout(r, 200));
  }
}

// ── 网络拦截（Fetch domain）────────────────────────────────────

/** Map<tabId, Map<requestId, interceptedRequest>> */
const interceptedRequests = new Map();
/** Map<tabId, { urlPattern, resourceTypes, requestHeaders }> */
const interceptConfig = new Map();
const INTERCEPT_MAX_REQUESTS = 100;

/** 开始网络拦截 */
async function cdpIntercept(tabId, payload, tracker) {
  const {
    urlPattern = '*',
    resourceTypes = ['XHR', 'Fetch'],
    requestHeaders = false,
  } = payload || {};

  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId, { keepAttached: true });
  await ensureDebuggerAttached(chromeTabId);

  const fetchPatterns = resourceTypes && resourceTypes.length > 0
    ? resourceTypes.map(rt => ({ urlPattern, resourceType: rt, requestStage: 'Request' }))
    : [{ urlPattern, requestStage: 'Request' }];

  await cdpSend(chromeTabId, 'Fetch.enable', { patterns: fetchPatterns });

  interceptConfig.set(chromeTabId, { urlPattern, resourceTypes, requestHeaders });
  if (!interceptedRequests.has(chromeTabId)) {
    interceptedRequests.set(chromeTabId, new Map());
  }

  return {
    intercepting: true,
    sessionId: String(chromeTabId),
    pattern: urlPattern,
    resourceTypes,
  };
}

/** 停止网络拦截并清除记录 */
async function cdpInterceptClear(tabId, tracker) {
  const chromeTabId = await resolveTabId(tabId);
  beginTrackedTabOperation(tracker, chromeTabId, { detachImmediately: true });
  const requests = interceptedRequests.get(chromeTabId);
  const clearedCount = requests ? requests.size : 0;

  interceptedRequests.delete(chromeTabId);
  interceptConfig.delete(chromeTabId);

  if (attachedTabs.has(chromeTabId)) {
    await cdpSend(chromeTabId, 'Fetch.disable', {}).catch(() => {});
  }

  return { intercepting: false, clearedCount };
}

/** 获取拦截结果 */
async function cdpInterceptResult(tabId, payload) {
  const {
    urlPattern,
    sinceTimestamp,
    resourceTypes,
    limit = 50,
  } = payload || {};

  const chromeTabId = await resolveTabId(tabId);
  const requests = interceptedRequests.get(chromeTabId);

  if (!requests) {
    return { requests: [] };
  }

  let results = Array.from(requests.values());

  if (urlPattern && urlPattern !== '*') {
    const escaped = urlPattern.replace(/[.+^${}()|[\]\\]/g, '\\$&').replace(/\*/g, '.*');
    const regex = new RegExp(escaped);
    results = results.filter(r => regex.test(r.url));
  }
  if (sinceTimestamp) {
    results = results.filter(r => r.timestamp > sinceTimestamp);
  }
  if (resourceTypes && resourceTypes.length > 0) {
    results = results.filter(r => resourceTypes.includes(r.resourceType));
  }

  results.sort((a, b) => b.timestamp - a.timestamp);
  results = results.slice(0, limit);

  return { requests: results };
}

/** 处理 Fetch.requestPaused 事件：记录请求并立即放行 */
function handleFetchRequestPaused(tabId, params) {
  const { requestId, request, resourceType } = params;
  const config = interceptConfig.get(tabId);

  if (config) {
    let requests = interceptedRequests.get(tabId);
    if (!requests) {
      requests = new Map();
      interceptedRequests.set(tabId, requests);
    }

    if (requests.size >= INTERCEPT_MAX_REQUESTS) {
      const firstKey = requests.keys().next().value;
      requests.delete(firstKey);
    }

    const entry = {
      requestId,
      url: request.url,
      method: request.method,
      resourceType: resourceType || 'Other',
      timestamp: Date.now(),
    };

    if (config.requestHeaders) {
      entry.requestHeaders = request.headers || {};
    }

    requests.set(requestId, entry);
  }

  // 立即放行，不阻塞页面加载
  chrome.debugger.sendCommand({ tabId }, 'Fetch.continueRequest', { requestId }, () => {
    if (chrome.runtime.lastError) { /* Tab may have navigated, ignore */ }
  });
}

// ── Debugger 附加管理 ─────────────────────────────────────────

/** 已附加的 tabId 集合 */
const attachedTabs = new Set();

/** 每个 tab 当前进行中的 debugger 操作数 */
const debuggerInFlightCounts = new Map();

/** 每个 tab 的空闲自动 detach 定时器 */
const debuggerDetachTimers = new Map();

/** 需要维持附加状态的 tab（如网络拦截） */
const stickyAttachedTabs = new Set();

/** 已启用 Runtime 域的 tabId 集合（用于控制台消息监听） */
const runtimeEnabledTabs = new Set();

/** 已启用 Page 域的 tabId 集合（用于新文档脚本注入） */
const pageEnabledTabs = new Set();

/** 已为 tab 注册的新文档脚本 */
const injectedScriptIdsByTab = new Map();

/** 已为当前文档补注入过脚本的 tabId 集合 */
const liveBootstrapTabs = new Set();

/** 预加载脚本源码缓存 */
const preloadScriptSourceCache = new Map();
const preloadScriptSourcePromises = new Map();

/** 控制台消息缓冲（最多 200 条，FIFO） */
const consoleMessages = [];
const CONSOLE_MESSAGES_MAX = 200;

function clearDebuggerDetachTimer(tabId) {
  const timer = debuggerDetachTimers.get(tabId);
  if (timer) {
    clearTimeout(timer);
    debuggerDetachTimers.delete(tabId);
  }
}

function beginTrackedTabOperation(tracker, tabId, options = {}) {
  if (!tracker || !Number.isFinite(tabId)) {
    return;
  }

  if (!tracker.touchedTabIds.has(tabId)) {
    tracker.touchedTabIds.add(tabId);
    debuggerInFlightCounts.set(tabId, (debuggerInFlightCounts.get(tabId) || 0) + 1);
  }

  if (options.keepAttached) {
    tracker.keepAttachedTabIds.add(tabId);
  }

  if (options.detachImmediately) {
    tracker.detachImmediatelyTabIds.add(tabId);
  }

  clearDebuggerDetachTimer(tabId);
}

function endTrackedTabOperation(tabId) {
  const current = debuggerInFlightCounts.get(tabId) || 0;
  if (current <= 1) {
    debuggerInFlightCounts.delete(tabId);
    return 0;
  }

  const next = current - 1;
  debuggerInFlightCounts.set(tabId, next);
  return next;
}

function scheduleDebuggerAutoDetach(tabId, delayMs = DEBUGGER_IDLE_DETACH_MS) {
  clearDebuggerDetachTimer(tabId);
  if (!attachedTabs.has(tabId) || stickyAttachedTabs.has(tabId) || interceptConfig.has(tabId)) {
    return;
  }

  const timer = setTimeout(() => {
    debuggerDetachTimers.delete(tabId);
    detachDebugger(tabId).catch(() => {});
  }, Math.max(0, delayMs));

  debuggerDetachTimers.set(tabId, timer);
}

async function finalizeTrackedDebuggerUsage(tracker) {
  if (!tracker) {
    return;
  }

  for (const tabId of tracker.touchedTabIds) {
    const remaining = endTrackedTabOperation(tabId);
    if (remaining > 0) {
      continue;
    }

    if (tracker.keepAttachedTabIds.has(tabId)) {
      stickyAttachedTabs.add(tabId);
      clearDebuggerDetachTimer(tabId);
      continue;
    }

    if (tracker.detachImmediatelyTabIds.has(tabId)) {
      stickyAttachedTabs.delete(tabId);
      await detachDebugger(tabId, { force: true }).catch(() => {});
      continue;
    }

    scheduleDebuggerAutoDetach(tabId);
  }
}

function cleanupDebuggerState(tabId) {
  clearDebuggerDetachTimer(tabId);
  attachedTabs.delete(tabId);
  stickyAttachedTabs.delete(tabId);
  debuggerInFlightCounts.delete(tabId);
  runtimeEnabledTabs.delete(tabId);
  pageEnabledTabs.delete(tabId);
  injectedScriptIdsByTab.delete(tabId);
  liveBootstrapTabs.delete(tabId);
}

async function loadPreloadScriptSource(path) {
  if (preloadScriptSourceCache.has(path)) {
    return preloadScriptSourceCache.get(path);
  }

  if (preloadScriptSourcePromises.has(path)) {
    return preloadScriptSourcePromises.get(path);
  }

  const pending = (async () => {
    const response = await fetch(chrome.runtime.getURL(path));
    if (!response.ok) {
      throw new Error(`无法加载注入脚本 ${path}：${response.status}`);
    }

    const source = await response.text();
    preloadScriptSourceCache.set(path, source);
    preloadScriptSourcePromises.delete(path);
    return source;
  })().catch(error => {
    preloadScriptSourcePromises.delete(path);
    throw error;
  });

  preloadScriptSourcePromises.set(path, pending);
  return pending;
}

async function enableRuntimeDomain(tabId) {
  if (runtimeEnabledTabs.has(tabId)) {
    return;
  }

  await cdpSend(tabId, 'Runtime.enable', {});
  runtimeEnabledTabs.add(tabId);
}

async function enablePageDomain(tabId) {
  if (pageEnabledTabs.has(tabId)) {
    return;
  }

  await cdpSend(tabId, 'Page.enable', {});
  pageEnabledTabs.add(tabId);
}

async function ensurePreloadScriptsInstalled(tabId) {
  if (injectedScriptIdsByTab.has(tabId)) {
    return;
  }

  await enablePageDomain(tabId);

  const scriptIds = [];
  for (const path of PRELOAD_SCRIPT_PATHS) {
    const source = await loadPreloadScriptSource(path);
    const result = await cdpSend(tabId, 'Page.addScriptToEvaluateOnNewDocument', { source });
    scriptIds.push(result.identifier);
  }

  injectedScriptIdsByTab.set(tabId, scriptIds);
}

async function bootstrapLiveDocument(tabId) {
  if (liveBootstrapTabs.has(tabId)) {
    return;
  }

  const sources = await Promise.all(PRELOAD_SCRIPT_PATHS.map(loadPreloadScriptSource));
  const expression = sources.join('\n\n');
  await cdpSend(tabId, 'Runtime.evaluate', {
    expression,
    returnByValue: false,
    awaitPromise: true,
  });
  liveBootstrapTabs.add(tabId);
}

async function ensureDebuggerAttached(tabId) {
  clearDebuggerDetachTimer(tabId);

  if (attachedTabs.has(tabId)) {
    // Service worker 可能重启，按需补齐域启用与脚本注册。
    await enableRuntimeDomain(tabId).catch(() => {});
    await ensurePreloadScriptsInstalled(tabId).catch(() => {});
    await bootstrapLiveDocument(tabId).catch(() => {});
    return;
  }

  await new Promise((resolve, reject) => {
    chrome.debugger.attach({ tabId }, '1.3', () => {
      if (chrome.runtime.lastError) {
        const msg = chrome.runtime.lastError.message || '';
        if (msg.includes('already attached') || msg.includes('Another debugger')) {
          attachedTabs.add(tabId);
          resolve();
        } else {
          reject(new Error(msg));
        }
      } else {
        attachedTabs.add(tabId);
        resolve();
      }
    });
  });

  await enableRuntimeDomain(tabId).catch(() => {});
  await ensurePreloadScriptsInstalled(tabId).catch(() => {});
  // 已加载页面需要立即补注入，避免首次 attach 后必须刷新才能使用。
  await bootstrapLiveDocument(tabId).catch(() => {});
}

async function detachDebugger(tabId, options = {}) {
  const { force = false } = options;
  clearDebuggerDetachTimer(tabId);

  if (!force && (stickyAttachedTabs.has(tabId) || interceptConfig.has(tabId))) {
    return;
  }

  if (!attachedTabs.has(tabId)) {
    cleanupDebuggerState(tabId);
    return;
  }

  await new Promise(resolve => {
    chrome.debugger.detach({ tabId }, () => {
      cleanupDebuggerState(tabId);
      resolve();
    });
  });
}

// 标签页关闭时自动清理
chrome.tabs.onRemoved.addListener(tabId => {
  cleanupDebuggerState(tabId);
  interceptedRequests.delete(tabId);
  interceptConfig.delete(tabId);
});


chrome.debugger.onDetach.addListener(source => {
  if (typeof source.tabId !== 'number') {
    return;
  }

  cleanupDebuggerState(source.tabId);
});

// 监听 CDP 事件：捕获控制台消息 + 网络拦截
chrome.debugger.onEvent.addListener((source, method, params) => {
  if (method === 'Fetch.requestPaused') {
    handleFetchRequestPaused(source.tabId, params);
    return;
  }
  if (method === 'Runtime.consoleAPICalled') {

    const frame = params.stackTrace && params.stackTrace.callFrames && params.stackTrace.callFrames[0];
    const entry = {
      type: params.type,
      text: (params.args || []).map(a => (a.value !== undefined ? String(a.value) : (a.description || ''))).join(' '),
      timestamp: Number.isFinite(params.timestamp) ? Math.trunc(params.timestamp) : 0,
      url: frame ? (frame.url || '') : '',
      lineNumber: frame ? (frame.lineNumber || 0) : 0,
    };
    consoleMessages.push(entry);
    if (consoleMessages.length > CONSOLE_MESSAGES_MAX) {
      consoleMessages.shift();
    }
  }
});

/** 解析 tabId 字符串 → Chrome numeric tabId（null 时返回活跃标签页） */
async function resolveTabId(tabId) {
  if (tabId) return parseInt(tabId, 10);
  const tabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
  if (!tabs.length) throw new Error('BRIDGE_002: 无法找到活跃标签页');
  return tabs[0].id;
}

/** 错误码分类 */
function classifyCdpError(err) {
  const msg = err.message || '';
  if (msg.includes('BRIDGE_002') || msg.includes('Tab')) return 'BRIDGE_002';
  if (msg.includes('timeout') || msg.includes('超时')) return 'BRIDGE_004';
  if (msg.includes('Target is closed') || msg.includes('Protocol error')) return 'BRIDGE_008';
  return 'BRIDGE_008';
}

/** 超时包装 */
function withTimeout(promise, ms) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`操作超时（${ms}ms）`)), ms);
    promise.then(
      v => { clearTimeout(timer); resolve(v); },
      e => { clearTimeout(timer); reject(e); }
    );
  });
}

// ============================================================
// 模块：心跳（BridgeEvent heartbeat）
// ============================================================

function startHeartbeat() {
  stopHeartbeat();
  heartbeatTimerId = setInterval(() => {
    sendHeartbeatEvent();
    resetHeartbeatTimeout();
  }, HEARTBEAT_INTERVAL_MS);
}

function stopHeartbeat() {
  if (heartbeatTimerId !== null) { clearInterval(heartbeatTimerId); heartbeatTimerId = null; }
  clearHeartbeatTimeout();
}

function resetHeartbeatTimeout() {
  clearHeartbeatTimeout();
  heartbeatTimeoutId = setTimeout(() => {
    console.warn('[KodaClaw] 心跳超时，强制重连');
    if (ws) ws.close(4000, 'Heartbeat timeout');
  }, HEARTBEAT_TIMEOUT_MS);
}

function clearHeartbeatTimeout() {
  if (heartbeatTimeoutId !== null) { clearTimeout(heartbeatTimeoutId); heartbeatTimeoutId = null; }
}

async function sendHeartbeatEvent() {
  const tabs = await chrome.tabs.query({}).catch(() => []);
  const cookieDomains = [...new Set(
    tabs.map(t => { try { return new URL(t.url || '').hostname; } catch { return null; } })
        .filter(Boolean)
  )];

  sendWsMessage({
    version: PROTOCOL_VERSION,
    eventId: `evt-${generateUuidV4()}`,
    type: 'heartbeat',
    ts: Date.now(),
    payload: {
      openTabs: tabs.length,
      cookieDomains: cookieDomains.slice(0, 10),
    },
  });
}

// ============================================================
// 模块：指数退避重连
// ============================================================

function scheduleReconnect() {
  cancelReconnect();
  connectionState = 'reconnecting';
  broadcastConnectionState();

  const delay = Math.min(
    RECONNECT_BASE_DELAY_MS * Math.pow(2, reconnectAttempt),
    RECONNECT_MAX_DELAY_MS
  );
  console.log(`[KodaClaw] ${delay}ms 后重连（第 ${reconnectAttempt + 1} 次）`);

  reconnectTimerId = setTimeout(() => {
    reconnectAttempt++;
    connect();
  }, delay);
}

function cancelReconnect() {
  if (reconnectTimerId !== null) { clearTimeout(reconnectTimerId); reconnectTimerId = null; }
}

// ============================================================
// 模块：UI 状态广播
// ============================================================

function broadcastConnectionState() {
  chrome.runtime.sendMessage({ action: 'connectionStateChanged', status: connectionState })
    .catch(() => {});
}

function broadcastEvent(type, data) {
  chrome.runtime.sendMessage({ action: type, ...data }).catch(() => {});
}

// ============================================================
// Popup 消息路由（chrome.runtime.onMessage）
// ============================================================

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  switch (message.action) {
    case 'getStatus':
      loadCredentials().then(creds => {
        sendResponse({
          status: connectionState,
          deviceId: creds ? creds.deviceId : null,
          pairedAt: creds ? creds.pairedAt : null,
        });
      });
      return true;

    case 'pair': {
      const { token, gatewayUrl } = message;
      const gw = gatewayUrl || currentGatewayUrl || 'http://localhost:5076';
      saveConfig({ gatewayUrl: gw })
        .then(() => performPairing(gw, token))
        .then(result => {
          currentGatewayUrl = gw;
          connect();
          sendResponse({ success: true, deviceId: result.deviceId });
        })
        .catch(err => {
          console.error('[KodaClaw] 配对失败:', err);
          sendResponse({ success: false, error: err.message });
        });
      return true;
    }

    case 'disconnect':
      disconnect();
      sendResponse({ success: true });
      break;

    case 'unpair':
      disconnect();
      clearCredentials().then(() => sendResponse({ success: true }));
      return true;

    case 'updateConfig':
      saveConfig(message.config)
        .then(() => sendResponse({ success: true }))
        .catch(err => sendResponse({ success: false, error: err.message }));
      return true;

    default:
      sendResponse({ success: false, error: `未知 action：${message.action}` });
  }
});

// ============================================================
// Service Worker 生命周期
// ============================================================

chrome.runtime.onInstalled.addListener(async () => {
  console.log('[KodaClaw] 扩展已安装');
  const existing = await chrome.storage.local.get(['kodaclaw_config']);
  if (!existing.kodaclaw_config) {
    await saveConfig({ gatewayUrl: 'http://localhost:5076' });
  }
});

chrome.runtime.onStartup.addListener(() => {
  console.log('[KodaClaw] 浏览器启动，尝试恢复连接...');
  connect();
});

// Service Worker 激活时也尝试连接
loadCredentials().then(creds => {
  if (creds) {
    loadConfig().then(cfg => {
      currentGatewayUrl = cfg.gatewayUrl;
      connect();
    });
  }
}).catch(() => {});
