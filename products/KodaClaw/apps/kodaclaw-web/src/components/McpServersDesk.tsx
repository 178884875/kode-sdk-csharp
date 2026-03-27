import { useEffect, useState } from "react";
import {
  CheckCircle2, XCircle, Loader2, Trash2, Plus, RefreshCw,
  Terminal, Globe, Network, Pencil, ChevronDown, ChevronUp,
} from "lucide-react";
import { fetchMcpServers, saveMcpServers, testMcpServerConnection } from "../lib/api";
import { getRuntimeConfig } from "../lib/config";
import type { WorkspaceMcpConfig, WorkspaceMcpServerEntry, McpConnectionTestResult } from "../types/contracts";
import { Modal } from "./ui/Modal";
import { ConfirmModal } from "./ui/ConfirmModal";
import { Button } from "./ui/Button";
import { Select } from "./ui/Select";
import "./ui/Modal.css";
import "./McpServersDesk.css";

/* ── Types ─────────────────────────────────────────── */

type TestState = "idle" | "testing" | "ok" | "fail";

interface ServerTestStatus {
  state: TestState;
  result?: McpConnectionTestResult;
}

interface ServerFormState {
  name: string;
  transport: "stdio" | "streamableHttp" | "sse";
  command: string;
  args: string;
  url: string;
  headers: string;
}

const EMPTY_FORM: ServerFormState = {
  name: "",
  transport: "stdio",
  command: "",
  args: "",
  url: "",
  headers: "",
};

/* ── Helpers ─────────────────────────────────────────── */

function parseHeadersText(text: string): Record<string, string> | undefined {
  if (!text.trim()) return undefined;
  try {
    return JSON.parse(text) as Record<string, string>;
  } catch {
    return undefined;
  }
}

function isHttpTransport(t: string) {
  return t === "streamableHttp" || t === "sse";
}

function entryTarget(entry: WorkspaceMcpServerEntry): string {
  return entry.url ?? entry.command ?? "";
}

function entryTransportLabel(entry: WorkspaceMcpServerEntry): string {
  if (entry.transport) return entry.transport;
  return entry.url ? "http" : "stdio";
}

function entryIcon(entry: WorkspaceMcpServerEntry) {
  return entry.url
    ? <Globe size={13} className="mcp-entry__icon mcp-entry__icon--http" />
    : <Terminal size={13} className="mcp-entry__icon mcp-entry__icon--stdio" />;
}

/** Convert an existing entry back into form fields */
function entryToForm(name: string, entry: WorkspaceMcpServerEntry): ServerFormState {
  const isHttp = !!entry.url || isHttpTransport(entry.transport ?? "");
  return {
    name,
    transport: isHttp
      ? ((entry.transport ?? "streamableHttp") as ServerFormState["transport"])
      : "stdio",
    command: entry.command ?? "",
    args: (entry.args ?? []).join(" "),
    url: entry.url ?? "",
    headers: entry.headers ? JSON.stringify(entry.headers, null, 2) : "",
  };
}

/* ── Main component ─────────────────────────────────── */

export function McpServersDesk() {
  const [config, setConfig] = useState<WorkspaceMcpConfig | null>(null);
  const [loading, setLoading] = useState(true);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [testStatus, setTestStatus] = useState<Record<string, ServerTestStatus>>({});
  const [expandedPanels, setExpandedPanels] = useState<Set<string>>(new Set());
  // null = closed, "" = adding new, "<name>" = editing existing
  const [modalTarget, setModalTarget] = useState<string | null>(null);
  const [deleteConfirm, setDeleteConfirm] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setSaveError(null);
    try {
      setConfig(await fetchMcpServers());
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : "加载失败");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void load(); }, []);

  async function saveConfig(next: WorkspaceMcpConfig) {
    setSaving(true);
    setSaveError(null);
    try {
      setConfig(await saveMcpServers(next));
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : "保存失败");
    } finally {
      setSaving(false);
    }
  }

  async function handleToggleEnabled(name: string, entry: WorkspaceMcpServerEntry) {
    if (!config) return;
    await saveConfig({
      mcpServers: {
        ...config.mcpServers,
        [name]: { ...entry, enabled: entry.enabled !== false ? false : true },
      },
    });
  }

  async function handleDelete(name: string) {
    if (!config) return;
    const { [name]: _removed, ...rest } = config.mcpServers;
    await saveConfig({ mcpServers: rest });
    setTestStatus(prev => { const next = { ...prev }; delete next[name]; return next; });
    setDeleteConfirm(null);
  }

  async function handleTestConnection(name: string) {
    // Auto-collapse panel when retesting
    setExpandedPanels(prev => { const next = new Set(prev); next.delete(name); return next; });
    setTestStatus(prev => ({ ...prev, [name]: { state: "testing" } }));
    try {
      const result = await testMcpServerConnection(name);
      setTestStatus(prev => ({
        ...prev,
        [name]: { state: result.success ? "ok" : "fail", result },
      }));
    } catch (e) {
      setTestStatus(prev => ({
        ...prev,
        [name]: {
          state: "fail",
          result: {
            success: false, toolCount: 0,
            errorMessage: e instanceof Error ? e.message : "连接失败",
          },
        },
      }));
    }
  }

  function togglePanel(name: string) {
    setExpandedPanels(prev => {
      const next = new Set(prev);
      if (next.has(name)) next.delete(name); else next.add(name);
      return next;
    });
  }

  function handleSaved(updated: WorkspaceMcpConfig, oldName?: string) {
    setConfig(updated);
    setModalTarget(null);
    // If name changed, migrate test status
    if (oldName) {
      setTestStatus(prev => {
        const next = { ...prev };
        delete next[oldName];
        return next;
      });
    }
  }

  const servers = config ? Object.entries(config.mcpServers) : [];
  const editingEntry = modalTarget && config?.mcpServers[modalTarget]
    ? config.mcpServers[modalTarget]
    : null;

  return (
    <div className="mcp-desk">
      {/* ── Toolbar ── */}
      <div className="mcp-desk__toolbar">
        <div className="mcp-desk__toolbar-left">
          <span className="mcp-desk__count">
            {loading ? "加载中…" : `${servers.length} 个服务器`}
          </span>
          {saveError && <span className="mcp-desk__inline-error">{saveError}</span>}
        </div>
        <div className="mcp-desk__toolbar-right">
          <Button
            variant="ghost"
            size="control"
            onClick={() => void load()}
            disabled={loading}
            aria-label="刷新"
          >
            <RefreshCw size={14} className={loading ? "spin" : ""} />
            刷新
          </Button>
          <Button
            variant="primary"
            size="control"
            onClick={() => setModalTarget("")}
          >
            <Plus size={14} />
            添加服务器
          </Button>
        </div>
      </div>

      {/* ── Server list ── */}
      {!loading && servers.length === 0 ? (
        <div className="mcp-desk__empty">
          <Network size={36} className="mcp-desk__empty-icon" />
          <p className="mcp-desk__empty-title">暂无 MCP 服务器</p>
          <p className="mcp-desk__empty-sub">添加 MCP 服务器以在对话中扩展工具能力</p>
          <Button
            variant="primary"
            onClick={() => setModalTarget("")}
          >
            <Plus size={14} />
            添加第一个服务器
          </Button>
        </div>
      ) : (
        <div className="mcp-desk__list">
          {servers.map(([name, entry]) => {
            const enabled = entry.enabled !== false;
            const ts = testStatus[name];
            const target = entryTarget(entry);
            const panelExpanded = expandedPanels.has(name);
            const hasError = ts?.state === "fail" && !!ts.result?.errorMessage;
            const hasTools = ts?.state === "ok" && !!ts.result?.toolNames?.length;

            return (
              <div
                key={name}
                className={`mcp-entry ${enabled ? "" : "mcp-entry--disabled"}`}
              >
                {/* ── Main row ── */}
                <div className="mcp-entry__row">
                  <div className="mcp-entry__icon-wrap">{entryIcon(entry)}</div>

                  <div className="mcp-entry__meta">
                    <div className="mcp-entry__name">
                      {name}
                      <span className="mcp-entry__transport">
                        {entryTransportLabel(entry)}
                      </span>
                      {!enabled && (
                        <span className="mcp-entry__badge mcp-entry__badge--disabled">已禁用</span>
                      )}
                    </div>
                    <div className="mcp-entry__target" title={target}>{target}</div>
                  </div>

                  {/* Status + expand toggle */}
                  <div className="mcp-entry__status">
                    <TestStatusBadge ts={ts} />
                    {(hasError || hasTools) && (
                      <Button
                        variant="ghost"
                        size="sm"
                        className="mcp-entry__log-toggle"
                        onClick={() => togglePanel(name)}
                        title={panelExpanded ? "收起" : hasTools ? "查看工具列表" : "查看错误日志"}
                      >
                        {panelExpanded
                          ? <ChevronUp size={12} />
                          : <ChevronDown size={12} />}
                        {panelExpanded ? "收起" : hasTools ? "工具" : "日志"}
                      </Button>
                    )}
                  </div>

                  <div className="mcp-entry__actions">
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => void handleTestConnection(name)}
                      disabled={ts?.state === "testing" || saving}
                      title="测试连接"
                    >
                      {ts?.state === "testing"
                        ? <Loader2 size={13} className="spin" />
                        : null}
                      测试
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => setModalTarget(name)}
                      disabled={saving}
                      title="编辑"
                      aria-label={`编辑 ${name}`}
                    >
                      <Pencil size={13} />
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => void handleToggleEnabled(name, entry)}
                      disabled={saving}
                    >
                      {enabled ? "禁用" : "启用"}
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      className="mcp-entry__delete"
                      onClick={() => setDeleteConfirm(name)}
                      disabled={saving}
                      title="删除"
                      aria-label={`删除 ${name}`}
                    >
                      <Trash2 size={13} />
                    </Button>
                  </div>
                </div>

                {/* ── Tools panel (success) ── */}
                {hasTools && panelExpanded && (
                  <div className="mcp-entry__tools-panel">
                    <div className="mcp-entry__tools-panel-label">
                      可用工具（{ts!.result!.toolNames!.length}）
                    </div>
                    <div className="mcp-entry__tools-list">
                      {ts!.result!.toolNames!.map(n => (
                        <span key={n} className="mcp-tool-chip">{n}</span>
                      ))}
                    </div>
                  </div>
                )}

                {/* ── Error log panel (fail) ── */}
                {hasError && panelExpanded && (
                  <div className="mcp-entry__error-log">
                    <div className="mcp-entry__error-log-label">错误详情</div>
                    <pre className="mcp-entry__error-log-body">
                      {ts!.result!.errorMessage}
                    </pre>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      {/* ── Add / Edit Modal ── */}
      <ServerModal
        open={modalTarget !== null}
        config={config}
        editName={modalTarget ?? undefined}
        initialEntry={editingEntry ?? undefined}
        onClose={() => setModalTarget(null)}
        onSaved={handleSaved}
      />

      {/* ── Delete Confirm Modal ── */}
      <ConfirmModal
        open={deleteConfirm !== null}
        title="删除 MCP Server"
        description={`确认删除 "${deleteConfirm}" 吗？此操作不可撤销。`}
        confirmLabel="删除"
        variant="danger"
        busy={saving}
        onConfirm={() => { if (deleteConfirm) void handleDelete(deleteConfirm); }}
        onCancel={() => setDeleteConfirm(null)}
      />
    </div>
  );
}

/* ── Test status badge ──────────────────────────────── */

function TestStatusBadge({ ts }: { ts: ServerTestStatus | undefined }) {
  if (!ts || ts.state === "idle") return null;

  if (ts.state === "testing") {
    return (
      <span className="mcp-badge mcp-badge--testing">
        <Loader2 size={11} className="spin" />
        测试中
      </span>
    );
  }
  if (ts.state === "ok") {
    return (
      <span className="mcp-badge mcp-badge--ok">
        <CheckCircle2 size={11} />
        {ts.result?.toolCount ?? 0} 工具
      </span>
    );
  }
  return (
    <span className="mcp-badge mcp-badge--fail">
      <XCircle size={11} />
      连接失败
    </span>
  );
}

/* ── Add / Edit Server Modal ────────────────────────── */

interface ServerModalProps {
  open: boolean;
  config: WorkspaceMcpConfig | null;
  /** Undefined or "" = adding new; a name string = editing that entry */
  editName?: string;
  initialEntry?: WorkspaceMcpServerEntry;
  onClose: () => void;
  onSaved: (config: WorkspaceMcpConfig, oldName?: string) => void;
}

function ServerModal({ open, config, editName, initialEntry, onClose, onSaved }: ServerModalProps) {
  const isEdit = !!editName;
  const [form, setForm] = useState<ServerFormState>(EMPTY_FORM);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  // Populate on open
  useEffect(() => {
    if (!open) return;
    setError(null);
    if (isEdit && editName && initialEntry) {
      setForm(entryToForm(editName, initialEntry));
    } else {
      setForm(EMPTY_FORM);
    }
  }, [open, isEdit, editName, initialEntry]);

  const isHttp = isHttpTransport(form.transport);
  const isWindows = getRuntimeConfig().platform === "win32";
  const showWindowsNodeHint = isWindows && !isHttp &&
    /^(npx|npm|yarn|pnpm|uvx|uv)$/i.test(form.command.trim());

  function setField<K extends keyof ServerFormState>(key: K, value: ServerFormState[K]) {
    setForm(f => ({ ...f, [key]: value }));
    setError(null);
  }

  async function handleSubmit() {
    setError(null);
    const name = form.name.trim();
    if (!name) { setError("服务器名称不能为空"); return; }
    // Allow same name when editing; block collision otherwise
    if (config?.mcpServers[name] && name !== editName) {
      setError(`名称 "${name}" 已存在`);
      return;
    }
    if (!isHttp && !form.command.trim()) { setError("stdio 类型必须填写启动命令"); return; }
    if (isHttp && !form.url.trim()) { setError("HTTP 类型必须填写服务地址 URL"); return; }

    const entry: WorkspaceMcpServerEntry = isHttp
      ? {
          transport: form.transport,
          url: form.url.trim(),
          headers: parseHeadersText(form.headers),
          // Preserve enabled flag when editing
          ...(isEdit && initialEntry?.enabled !== undefined
            ? { enabled: initialEntry.enabled }
            : {}),
        }
      : {
          command: form.command.trim(),
          args: form.args.trim() ? form.args.trim().split(/\s+/) : undefined,
          ...(isEdit && initialEntry?.enabled !== undefined
            ? { enabled: initialEntry.enabled }
            : {}),
        };

    // Build updated map: if name changed, remove old key
    const base = { ...(config?.mcpServers ?? {}) };
    if (isEdit && editName && editName !== name) {
      delete base[editName];
    }
    base[name] = entry;

    setSaving(true);
    try {
      const saved = await saveMcpServers({ mcpServers: base });
      onSaved(saved, isEdit && editName !== name ? editName : undefined);
    } catch (e) {
      setError(e instanceof Error ? e.message : "保存失败");
    } finally {
      setSaving(false);
    }
  }

  const footer = (
    <>
      <Button variant="secondary" onClick={onClose} disabled={saving}>
        取消
      </Button>
      <Button
        variant="primary"
        onClick={() => void handleSubmit()}
        disabled={saving}
      >
        {saving ? <Loader2 size={14} className="spin" /> : null}
        {saving ? "保存中…" : "保存"}
      </Button>
    </>
  );

  return (
    <Modal
      open={open}
      title={isEdit ? `编辑 "${editName}"` : "添加 MCP 服务器"}
      onClose={onClose}
      footer={footer}
      width={520}
    >
      <div className="kc-modal-form">
        {/* 名称 + 传输类型 */}
        <div className="kc-modal-form__row">
          <div className="kc-field">
            <label className="kc-field__label" htmlFor="mcp-name">名称</label>
            <input
              id="mcp-name"
              className="kc-input"
              placeholder="e.g. chrome-devtools"
              value={form.name}
              onChange={e => setField("name", e.target.value)}
              autoFocus
            />
          </div>
          <div className="kc-field">
            <label className="kc-field__label" htmlFor="mcp-transport">传输类型</label>
            <Select
              id="mcp-transport"
              value={form.transport}
              onChange={e => setField("transport", e.target.value as ServerFormState["transport"])}
            >
              <option value="stdio">stdio（本地进程）</option>
              <option value="streamableHttp">streamableHttp（远程 HTTP）</option>
              <option value="sse">sse（Server-Sent Events）</option>
            </Select>
          </div>
        </div>

        {/* 条件字段：stdio vs HTTP */}
        {!isHttp ? (
          <div className="kc-modal-form__row">
            <div className="kc-field">
              <label className="kc-field__label" htmlFor="mcp-command">启动命令</label>
              <input
                id="mcp-command"
                className="kc-input"
                placeholder="npx"
                value={form.command}
                onChange={e => setField("command", e.target.value)}
              />
              {showWindowsNodeHint && (
                <span className="kc-field__hint kc-field__hint--warn">
                  Windows 提示：<code>{form.command.trim()}</code> 是 .cmd 脚本，
                  无法直接启动。请改为 <code>cmd.exe</code>，
                  并将 <code>/c {form.command.trim()}</code> 填入参数栏。
                </span>
              )}
            </div>
            <div className="kc-field">
              <label className="kc-field__label" htmlFor="mcp-args">参数</label>
              <input
                id="mcp-args"
                className="kc-input"
                placeholder="-y my-server@latest"
                value={form.args}
                onChange={e => setField("args", e.target.value)}
              />
              <span className="kc-field__hint">多个参数以空格分隔</span>
            </div>
          </div>
        ) : (
          <>
            <div className="kc-field">
              <label className="kc-field__label" htmlFor="mcp-url">服务地址</label>
              <input
                id="mcp-url"
                className="kc-input"
                placeholder="https://api.example.com/mcp"
                value={form.url}
                onChange={e => setField("url", e.target.value)}
              />
            </div>
            <div className="kc-field">
              <label className="kc-field__label" htmlFor="mcp-headers">请求头（可选）</label>
              <input
                id="mcp-headers"
                className="kc-input"
                placeholder='{"Authorization": "Bearer sk-..."}'
                value={form.headers}
                onChange={e => setField("headers", e.target.value)}
              />
              <span className="kc-field__hint">JSON 格式，留空跳过</span>
            </div>
          </>
        )}

        {error && <p className="kc-field__error">{error}</p>}
      </div>
    </Modal>
  );
}
