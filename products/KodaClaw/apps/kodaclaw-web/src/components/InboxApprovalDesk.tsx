import React, { useEffect, useMemo, useState } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import {
  fetchApprovals,
  fetchInbox,
  pushAutomationResultToChannel,
  submitApprovalDecision,
  updateInboxStatus,
} from "../lib/api";
import { resolveGatewayPath } from "../lib/config";
import { useI18n, useLocaleText } from "../i18n/I18nProvider";
import { Skeleton } from "./ui/Skeleton";
import { EmptyState } from "./ui/EmptyState";
import { Button } from "./ui/Button";
import { Select } from "./ui/Select";
import { Inbox, Zap } from "lucide-react";
import type {
  Approval,
  ApprovalStatus,
  ChannelPushResult,
  DeliveryMode,
  InboxItem,
  InboxItemStatus,
} from "../types/contracts";
import "./ControlPlaneDesk.css";

type InboxStatusFilter = InboxItemStatus | "all";
type MediaAttachmentRef = { mediaId: string; contentType: string };

type ChannelDeliveryPayload = {
  draftId: string;
  bindingId: string;
  connectorKind: string;
  accountId: string;
  externalThreadId: string;
  deliveryMode: DeliveryMode;
  messageText: string;
  mediaAttachments?: MediaAttachmentRef[];
};

const INBOX_STATUS_OPTIONS: Array<InboxItemStatus> = [
  "Open",
  "Acknowledged",
  "Resolved",
  "Archived",
];

function PushToChannelButton({
  inboxId,
  channels,
  onSuccess,
}: {
  inboxId: string;
  channels: string[];
  onSuccess: () => void;
}) {
  const [pushing, setPushing] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  const handlePush = async () => {
    setPushing(true);
    setError(null);
    try {
      await pushAutomationResultToChannel(inboxId, channels);
      onSuccess();
    } catch (e) {
      setError(e instanceof Error ? e.message : "推送失败");
    } finally {
      setPushing(false);
    }
  };

  return (
    <div className="inbox-push-approval">
      <Button
        variant="primary"
        onClick={() => void handlePush()}
        disabled={pushing}
      >
        {pushing ? "推送中..." : "推送到渠道"}
      </Button>
      {error ? <span className="inbox-push-approval__error">{error}</span> : null}
    </div>
  );
}

export function InboxApprovalDesk() {
  const { formatDateTime } = useI18n();
  const text = useLocaleText({
    zh: {
      common: {
        none: "暂无",
        loading: "加载中...",
        all: "全部",
        items: (count: number) => `${count} 项`,
      },
      title: "收件箱",
      intro: "查看 Agent 发来的通知、审批请求和自动化结果。",
      refresh: "刷新",
      refreshing: "正在刷新...",
      statusFilter: "状态",
      kindTabs: { all: "全部", approvals: "审批", automations: "自动化结果" },
      emptyInbox: "当前筛选下没有收件。",
      emptyDetail: "从左侧选择一条收件记录查看详情。",
      errorTitle: "收件加载失败",
      actionRequired: "需要动作",
      notePlaceholder: "补充审批备注（可选）",
      approve: "批准",
      reject: "拒绝",
      status: "状态",
      kind: "类型",
      source: "来源",
      route: "路由",
      session: "会话",
      correlation: "关联 ID",
      lastUpdated: "最近更新",
      linkedApproval: "关联审批",
      decisionNote: "决策备注",
      deliveryContext: "渠道投递上下文",
      connector: "连接器",
      account: "账号",
      threadBinding: "线程绑定",
      thread: "线程",
      draft: "草稿",
      messagePreview: "消息预览",
      threadDetailApi: "线程详情 API",
      threadAuditApi: "线程审计 API",
      deliveryModeLabels: {
        AutoSend: "自动发送",
        DraftApproval: "草稿审批",
        RequireApproval: "需审批后发送",
      },
      kindLabels: {
        Approval: "审批",
        AutomationResult: "自动化结果",
        PluginRequest: "插件请求",
        ChannelUpdate: "渠道更新",
        Alert: "告警",
        TaskResult: "任务结果",
        Information: "信息",
        OutboundMessage: "对外消息",
        OutboundEmail: "对外邮件",
        PluginAuthorization: "插件授权",
        ExternalAction: "外部动作",
        ChannelDelivery: "渠道投递",
        AutomationAction: "自动化动作",
      },
      inboxStatusLabels: {
        Open: "待处理",
        Acknowledged: "已知悉",
        Resolved: "已解决",
        Archived: "已归档",
      },
      approvalStatusLabels: {
        Pending: "待审批",
        Approved: "已批准",
        Rejected: "已拒绝",
        Canceled: "已取消",
      },
      markRead: "标为已读",
      errors: {
        loadFailed: "加载收件失败。",
        decisionFailed: "提交审批决策失败。",
        updateFailed: "更新收件状态失败。",
      },
    },
    en: {
      common: {
        none: "n/a",
        loading: "Loading...",
        all: "All",
        items: (count: number) => `${count} items`,
      },
      title: "Inbox",
      intro: "View notifications, approval requests, and automation results from the Agent.",
      refresh: "Refresh",
      refreshing: "Refreshing...",
      statusFilter: "Status",
      kindTabs: { all: "All", approvals: "Approvals", automations: "Automation Results" },
      emptyInbox: "No inbox items match the current filter.",
      emptyDetail: "Select an inbox item to view details.",
      errorTitle: "Inbox failed to load",
      actionRequired: "Action required",
      notePlaceholder: "Decision note (optional)",
      approve: "Approve",
      reject: "Reject",
      status: "Status",
      kind: "Kind",
      source: "Source",
      route: "Route",
      session: "Session",
      correlation: "Correlation",
      lastUpdated: "Last updated",
      linkedApproval: "Linked approval",
      decisionNote: "Decision note",
      deliveryContext: "Channel delivery context",
      connector: "Connector",
      account: "Account",
      threadBinding: "Thread binding",
      thread: "Thread",
      draft: "Draft",
      messagePreview: "Message preview",
      threadDetailApi: "Thread detail API",
      threadAuditApi: "Thread audit API",
      deliveryModeLabels: {
        AutoSend: "Auto send",
        DraftApproval: "Draft approval",
        RequireApproval: "Require approval",
      },
      kindLabels: {
        Approval: "Approval",
        AutomationResult: "Automation result",
        PluginRequest: "Plugin request",
        ChannelUpdate: "Channel update",
        Alert: "Alert",
        TaskResult: "Task result",
        Information: "Information",
        OutboundMessage: "Outbound message",
        OutboundEmail: "Outbound email",
        PluginAuthorization: "Plugin authorization",
        ExternalAction: "External action",
        ChannelDelivery: "Channel delivery",
        AutomationAction: "Automation action",
      },
      inboxStatusLabels: {
        Open: "Open",
        Acknowledged: "Acknowledged",
        Resolved: "Resolved",
        Archived: "Archived",
      },
      approvalStatusLabels: {
        Pending: "Pending",
        Approved: "Approved",
        Rejected: "Rejected",
        Canceled: "Canceled",
      },
      markRead: "Mark as read",
      errors: {
        loadFailed: "Failed to load inbox.",
        decisionFailed: "Failed to submit approval decision.",
        updateFailed: "Failed to update inbox status.",
      },
    },
  });

  const [inboxItems, setInboxItems] = useState<InboxItem[]>([]);
  const [approvals, setApprovals] = useState<Approval[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<InboxStatusFilter>("Open");
  const [approvalNotes, setApprovalNotes] = useState<Record<string, string>>({});
  const [pendingApprovalIds, setPendingApprovalIds] = useState<Record<string, boolean>>({});
  const [pendingInboxIds, setPendingInboxIds] = useState<Record<string, boolean>>({});
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [kindFilter, setKindFilter] = useState<"all" | "approvals" | "automations">("all");

  const formatTimestamp = (value?: string | null) => formatDateTime(value, text.common.none);
  const formatKind = (kind: InboxItem["kind"] | Approval["kind"]) => text.kindLabels[kind] ?? kind;
  const formatInboxStatus = (status: InboxItemStatus) => text.inboxStatusLabels[status] ?? status;
  const formatApprovalStatus = (status: ApprovalStatus) => text.approvalStatusLabels[status] ?? status;
  const formatDeliveryMode = (mode: DeliveryMode) => text.deliveryModeLabels[mode] ?? mode;

  function parseChannelDeliveryPayload(payloadJson?: string | null): ChannelDeliveryPayload | null {
    if (!payloadJson) return null;
    try {
      const parsed = JSON.parse(payloadJson) as Partial<ChannelDeliveryPayload>;
      if (
        typeof parsed.draftId !== "string" ||
        typeof parsed.bindingId !== "string" ||
        typeof parsed.connectorKind !== "string" ||
        typeof parsed.accountId !== "string" ||
        typeof parsed.externalThreadId !== "string" ||
        typeof parsed.deliveryMode !== "string" ||
        typeof parsed.messageText !== "string"
      ) {
        return null;
      }
      return {
        draftId: parsed.draftId,
        bindingId: parsed.bindingId,
        connectorKind: parsed.connectorKind,
        accountId: parsed.accountId,
        externalThreadId: parsed.externalThreadId,
        deliveryMode: parsed.deliveryMode,
        messageText: parsed.messageText,
        mediaAttachments: Array.isArray(parsed.mediaAttachments) ? parsed.mediaAttachments : undefined,
      };
    } catch {
      return null;
    }
  }

  const buildThreadDetailApi = (bindingId: string) =>
    resolveGatewayPath(`/api/channels/threads/${bindingId}`);
  const buildThreadAuditApi = (bindingId: string) =>
    resolveGatewayPath(`/api/channels/threads/${bindingId}/audit?limit=20`);

  useEffect(() => {
    setSelectedId((current) => {
      if (current && inboxItems.some((item) => item.id === current)) return current;
      return inboxItems[0]?.id ?? null;
    });
  }, [inboxItems]);

  async function loadData(mode: "initial" | "refresh") {
    if (mode === "initial") setIsLoading(true);
    else setIsRefreshing(true);
    setError(null);

    try {
      const [inboxResult, approvalResult] = await Promise.all([
        fetchInbox({ limit: 50, status: statusFilter === "all" ? undefined : statusFilter }),
        fetchApprovals({ limit: 50 }),
      ]);
      setInboxItems([...inboxResult.items]);
      setApprovals([...approvalResult.items]);
    } catch (err) {
      setError(err instanceof Error ? err.message : text.errors.loadFailed);
    } finally {
      if (mode === "initial") setIsLoading(false);
      else setIsRefreshing(false);
    }
  }

  useEffect(() => {
    void loadData("initial");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [statusFilter]);

  async function handleRefresh() {
    await loadData("refresh");
  }

  async function handleApprovalDecision(approvalId: string, approve: boolean) {
    setPendingApprovalIds((cur) => ({ ...cur, [approvalId]: true }));
    try {
      const note = approvalNotes[approvalId] ?? "";
      await submitApprovalDecision(approvalId, approve, note);
      await loadData("refresh");
    } catch (err) {
      setError(err instanceof Error ? err.message : text.errors.decisionFailed);
    } finally {
      setPendingApprovalIds((cur) => {
        const next = { ...cur };
        delete next[approvalId];
        return next;
      });
    }
  }

  async function handleStatusUpdate(inboxId: string, status: InboxItemStatus) {
    setPendingInboxIds((cur) => ({ ...cur, [inboxId]: true }));
    try {
      await updateInboxStatus(inboxId, status);
      await loadData("refresh");
    } catch (err) {
      setError(err instanceof Error ? err.message : text.errors.updateFailed);
    } finally {
      setPendingInboxIds((cur) => {
        const next = { ...cur };
        delete next[inboxId];
        return next;
      });
    }
  }

  const approvalsMap = useMemo(() => {
    const map: Record<string, Approval> = {};
    for (const a of approvals) map[a.id] = a;
    return map;
  }, [approvals]);

  const filteredItems = useMemo(() => {
    if (kindFilter === "approvals") return inboxItems.filter((i) => i.kind !== "AutomationResult");
    if (kindFilter === "automations") return inboxItems.filter((i) => i.kind === "AutomationResult");
    return inboxItems;
  }, [inboxItems, kindFilter]);

  const selectedItem = useMemo(
    () => inboxItems.find((i) => i.id === selectedId) ?? null,
    [inboxItems, selectedId],
  );

  const linkedApproval = useMemo(() => {
    if (!selectedItem?.approvalId) return null;
    return approvalsMap[selectedItem.approvalId] ?? null;
  }, [selectedItem, approvalsMap]);

  const selectedPayload = useMemo(
    () => parseChannelDeliveryPayload(selectedItem?.payloadJson ?? linkedApproval?.payloadJson),
    [selectedItem, linkedApproval],
  );

  function renderDeliveryContext(payload: ChannelDeliveryPayload) {
    return (
      <div className="inbox-delivery-context">
        <p className="metric-label">{text.deliveryContext}</p>
        <div className="control-plane-summary-grid">
          <div className="metric-item">
            <span className="metric-label">{text.connector}</span>
            <span className="metric-value metric-value--path">{payload.connectorKind}</span>
          </div>
          <div className="metric-item">
            <span className="metric-label">{text.account}</span>
            <span className="metric-value metric-value--path">{payload.accountId}</span>
          </div>
          <div className="metric-item">
            <span className="metric-label">{text.threadBinding}</span>
            <span className="metric-value metric-value--path">{payload.bindingId}</span>
          </div>
          <div className="metric-item">
            <span className="metric-label">{text.thread}</span>
            <span className="metric-value metric-value--path">{payload.externalThreadId}</span>
          </div>
          <div className="metric-item">
            <span className="metric-label">{text.draft}</span>
            <span className="metric-value metric-value--path">{payload.draftId}</span>
          </div>
          <div className="metric-item">
            <span className="metric-label">{text.status}</span>
            <span className="metric-value">{formatDeliveryMode(payload.deliveryMode)}</span>
          </div>
          <div className="metric-item metric-item--full-width">
            <span className="metric-label">{text.messagePreview}</span>
            <span className="metric-value">{payload.messageText}</span>
          </div>
          {payload.mediaAttachments
            ?.filter((a) => a.contentType.startsWith("image/"))
            .map((a) => (
              <div key={a.mediaId} className="metric-item metric-item--full-width">
                <img
                  src={`/api/media/${a.mediaId}`}
                  alt={a.mediaId}
                  className="inbox-media-thumbnail"
                  data-testid={`inbox-media-thumbnail-${a.mediaId}`}
                />
              </div>
            ))}
          <div className="metric-item">
            <span className="metric-label">{text.threadDetailApi}</span>
            <span className="metric-value metric-value--path">
              {buildThreadDetailApi(payload.bindingId)}
            </span>
          </div>
          <div className="metric-item">
            <span className="metric-label">{text.threadAuditApi}</span>
            <span className="metric-value metric-value--path">
              {buildThreadAuditApi(payload.bindingId)}
            </span>
          </div>
        </div>
      </div>
    );
  }

  return (
    <section data-testid="inbox-approval-desk" className="control-plane-stack" style={{ gap: 0 }}>
      <div>
        <h2 className="desk-section-title">{text.title}</h2>
        <p className="desk-section-desc">{text.intro}</p>

        <div className="control-plane-toolbar">
          <Button
            variant="secondary"
            size="control"
            data-testid="inbox-refresh"
            disabled={isLoading || isRefreshing}
            onClick={() => { void handleRefresh(); }}
          >
            {isRefreshing ? text.refreshing : text.refresh}
          </Button>
          <label className="metric-label" htmlFor="inbox-status-filter">
            {text.statusFilter}
          </label>
          <Select
            id="inbox-status-filter"
            data-testid="inbox-status-filter"
            className="control-plane-filter"
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value as InboxStatusFilter)}
          >
            <option value="all">{text.common.all}</option>
            {INBOX_STATUS_OPTIONS.map((s) => (
              <option key={s} value={s}>{formatInboxStatus(s)}</option>
            ))}
          </Select>
        </div>
      </div>

      {error ? (
        <section className="status-card status-card--error" data-testid="inbox-approval-error">
          <h3 className="desk-section-title">{text.errorTitle}</h3>
          <p className="desk-section-desc">{error}</p>
        </section>
      ) : null}

      <div className="control-plane-pane-shell inbox-desk__layout">
        {/* LEFT: Inbox list */}
        <div className="control-plane-pane-rail inbox-desk__rail">
          <section className="timeline" data-testid="inbox-list">
            <div className="timeline__header">
              <span className="composer__status">
                {isLoading ? text.common.loading : text.common.items(filteredItems.length)}
              </span>
            </div>

            <div className="inbox-kind-tabs" data-testid="inbox-kind-tabs">
              {(["all", "approvals", "automations"] as const).map((tab) => (
                <button
                  key={tab}
                  type="button"
                  className={`inbox-kind-tab${kindFilter === tab ? " inbox-kind-tab--active" : ""}`}
                  onClick={() => setKindFilter(tab)}
                >
                  {tab === "automations" && <Zap size={12} strokeWidth={1.75} />}
                  {text.kindTabs[tab]}
                </button>
              ))}
            </div>

            <div className="timeline__body inbox-desk__list-body">
              {isLoading ? <Skeleton height={52} count={3} /> : null}
              {!isLoading && filteredItems.length === 0 ? (
                <EmptyState icon={<Inbox size={28} strokeWidth={1.5} />} title={text.emptyInbox} />
              ) : null}
              {filteredItems.map((item) => {
                const isAutomation = item.kind === "AutomationResult";
                return (
                  <article
                    key={item.id}
                    className={`message message--system control-plane-stack control-plane-queue-card${selectedId === item.id ? " control-plane-list-button--selected" : ""}${isAutomation ? " inbox-automation-result-card" : ""}`}
                    data-testid={`inbox-item-${item.id}`}
                    onClick={() => setSelectedId(item.id)}
                  >
                    <div className="message__meta">
                      {isAutomation ? (
                        <span className="message__role inbox-automation-result-kind">
                          <Zap size={12} strokeWidth={1.75} />
                          {formatKind(item.kind)}
                        </span>
                      ) : (
                        <span className="message__role">{formatKind(item.kind)}</span>
                      )}
                      <span>{formatTimestamp(item.updatedAt)}</span>
                    </div>
                    <strong>{item.title}</strong>
                    <p className="control-plane-compact-copy inbox-card-summary">{item.summary}</p>
                    {item.requiresAction ? (
                      <span className="stream-indicator is-live">{text.actionRequired}</span>
                    ) : null}
                  </article>
                );
              })}
            </div>
          </section>
        </div>

        {/* RIGHT: Detail */}
        <div className="control-plane-pane-stage inbox-desk__stage" data-testid="inbox-detail">
          {!selectedItem ? (
            <div className="control-plane-stage-hero">
              <EmptyState icon={<Inbox size={28} strokeWidth={1.5} />} title={text.emptyDetail} />
            </div>
          ) : (
            <div className="status-card status-card--normal control-plane-stage-hero control-plane-stack">
              {/* Hero header */}
              <div className="inbox-detail-hero">
                <div className="control-plane-chip-row">
                  <span className="control-plane-chip">{formatKind(selectedItem.kind)}</span>
                  <span className="control-plane-chip">{formatInboxStatus(selectedItem.status)}</span>
                  {selectedItem.requiresAction ? (
                    <span className="control-plane-chip control-plane-chip--warning">
                      {text.actionRequired}
                    </span>
                  ) : null}
                </div>
                <h3 className="desk-section-title control-plane-card-title">{selectedItem.title}</h3>
                <div className={`inbox-detail-markdown${selectedItem.kind === "AutomationResult" ? " inbox-detail-markdown--automation" : ""}`}>
                  <ReactMarkdown
                    remarkPlugins={[remarkGfm]}
                    components={{
                      a: ({ href, children }) => (
                        <a href={href} target="_blank" rel="noopener noreferrer">
                          {children}
                        </a>
                      ),
                    }}
                  >
                    {selectedItem.summary ?? ""}
                  </ReactMarkdown>
                </div>
              </div>

              {/* AutomationResult channel push status */}
              {(() => {
                if (selectedItem.kind !== "AutomationResult") return null;
                let channelPushResults: ChannelPushResult[] | undefined;
                let notifyMode: string | undefined;
                let notificationChannels: string[] | undefined;
                try {
                  const p = JSON.parse(selectedItem.payloadJson ?? "{}") as Record<string, unknown>;
                  channelPushResults = p.channelPushResults as ChannelPushResult[] | undefined;
                  notifyMode = p.notifyMode as string | undefined;
                  notificationChannels = p.notificationChannels as string[] | undefined;
                } catch {
                  // ignore parse errors
                }

                if (!notificationChannels || notificationChannels.length === 0) return null;

                return (
                  <div className="inbox-detail-push-status">
                    {channelPushResults && channelPushResults.length > 0 ? (
                      <div className="inbox-push-results">
                        {channelPushResults.map((r) => (
                          <div
                            key={r.bindingId}
                            className={`inbox-push-result ${r.ok ? "inbox-push-result--ok" : "inbox-push-result--fail"}`}
                          >
                            <span className="inbox-push-result__binding">{r.bindingId}</span>
                            <span className="inbox-push-result__status">{r.ok ? "✓" : "✗"}</span>
                            {!r.ok && r.errorMessage ? (
                              <span className="inbox-push-result__error">{r.errorMessage}</span>
                            ) : null}
                          </div>
                        ))}
                      </div>
                    ) : notifyMode === "Approval" ? (
                      <PushToChannelButton
                        inboxId={selectedItem.id}
                        channels={notificationChannels}
                        onSuccess={() => { void loadData("refresh"); }}
                      />
                    ) : null}
                  </div>
                );
              })()}

              {/* Metadata grid */}
              <div className="control-plane-summary-grid">
                <div className="metric-item">
                  <span className="metric-label">{text.source}</span>
                  <span className="metric-value metric-value--path">{selectedItem.source}</span>
                </div>
                <div className="metric-item">
                  <span className="metric-label">{text.lastUpdated}</span>
                  <span className="metric-value">{formatTimestamp(selectedItem.updatedAt)}</span>
                </div>
                {selectedItem.sessionId ? (
                  <div className="metric-item">
                    <span className="metric-label">{text.session}</span>
                    <span className="metric-value metric-value--path">{selectedItem.sessionId}</span>
                  </div>
                ) : null}
                {selectedItem.correlationId ? (
                  <div className="metric-item">
                    <span className="metric-label">{text.correlation}</span>
                    <span className="metric-value metric-value--path">{selectedItem.correlationId}</span>
                  </div>
                ) : null}
                {selectedItem.route ? (
                  <div className="metric-item metric-item--full-width">
                    <span className="metric-label">{text.route}</span>
                    <span className="metric-value metric-value--path">{selectedItem.route}</span>
                  </div>
                ) : null}
              </div>

              {/* Channel delivery context */}
              {selectedPayload ? renderDeliveryContext(selectedPayload) : null}

              {/* Linked approval section */}
              {linkedApproval ? (
                <div className="inbox-approval-section">
                  <p className="metric-label">{text.linkedApproval}</p>
                  <div className="control-plane-stage-hero__header">
                    <div>
                      <strong>{linkedApproval.title}</strong>
                      <p className="control-plane-compact-copy">{linkedApproval.summary}</p>
                    </div>
                    <div className="control-plane-chip-row">
                      <span className="control-plane-chip">{formatKind(linkedApproval.kind)}</span>
                      <span className="control-plane-chip">{formatApprovalStatus(linkedApproval.status)}</span>
                    </div>
                  </div>
                  {linkedApproval.sessionId ? (
                    <div className="metric-item">
                      <span className="metric-label">{text.session}</span>
                      <span className="metric-value metric-value--path">{linkedApproval.sessionId}</span>
                    </div>
                  ) : null}
                  {linkedApproval.status === "Pending" ? (
                    <>
                      <textarea
                        className="kc-textarea"
                        data-testid={`approval-note-${linkedApproval.id}`}
                        placeholder={text.notePlaceholder}
                        rows={2}
                        value={approvalNotes[linkedApproval.id] ?? ""}
                        disabled={pendingApprovalIds[linkedApproval.id] ?? false}
                        onChange={(e) =>
                          setApprovalNotes((cur) => ({
                            ...cur,
                            [linkedApproval.id]: e.target.value,
                          }))
                        }
                      />
                      <div className="control-plane-inline-actions">
                        <Button
                          variant="primary"
                          data-testid="approval-approve"
                          disabled={pendingApprovalIds[linkedApproval.id] ?? false}
                          onClick={() => void handleApprovalDecision(linkedApproval.id, true)}
                        >
                          {text.approve}
                        </Button>
                        <Button
                          variant="secondary"
                          data-testid="approval-reject"
                          disabled={pendingApprovalIds[linkedApproval.id] ?? false}
                          onClick={() => void handleApprovalDecision(linkedApproval.id, false)}
                        >
                          {text.reject}
                        </Button>
                      </div>
                    </>
                  ) : null}
                  {linkedApproval.decisionNote ? (
                    <div className="metric-item">
                      <span className="metric-label">{text.decisionNote}</span>
                      <span className="metric-value">{linkedApproval.decisionNote}</span>
                    </div>
                  ) : null}
                </div>
              ) : null}

              {/* Item actions */}
              <div className="control-plane-detail-actions">
                {selectedItem.kind === "AutomationResult" ? (
                  <Button
                    variant="secondary"
                    data-testid={`inbox-mark-read-${selectedItem.id}`}
                    disabled={
                      (pendingInboxIds[selectedItem.id] ?? false) ||
                      selectedItem.status === "Acknowledged"
                    }
                    onClick={() => void handleStatusUpdate(selectedItem.id, "Acknowledged")}
                  >
                    {text.markRead}
                  </Button>
                ) : (
                  <>
                    <label className="metric-label" htmlFor="inbox-detail-status">
                      {text.status}
                    </label>
                    <Select
                      id="inbox-detail-status"
                      data-testid={`inbox-status-${selectedItem.id}`}
                      value={selectedItem.status}
                      disabled={pendingInboxIds[selectedItem.id] ?? false}
                      onChange={(e) =>
                        void handleStatusUpdate(selectedItem.id, e.target.value as InboxItemStatus)
                      }
                    >
                      {INBOX_STATUS_OPTIONS.map((s) => (
                        <option key={s} value={s}>{formatInboxStatus(s)}</option>
                      ))}
                    </Select>
                  </>
                )}
              </div>
            </div>
          )}
        </div>
      </div>
    </section>
  );
}
