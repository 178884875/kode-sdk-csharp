import { useEffect, useMemo, useState } from "react";
import {
  fetchApprovals,
  fetchInbox,
  submitApprovalDecision,
  updateInboxStatus,
} from "../lib/api";
import { resolveGatewayPath } from "../lib/config";
import { useI18n, useLocaleText } from "../i18n/I18nProvider";
import { Skeleton } from "./ui/Skeleton";
import { EmptyState } from "./ui/EmptyState";
import { Inbox, Zap } from "lucide-react";
import type {
  Approval,
  ApprovalStatus,
  DeliveryMode,
  InboxItem,
  InboxItemStatus,
} from "../types/contracts";
import "./ControlPlaneDesk.css";

type InboxStatusFilter = InboxItemStatus | "all";
type ApprovalStatusFilter = ApprovalStatus | "all";
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

const APPROVAL_STATUS_OPTIONS: Array<ApprovalStatus> = [
  "Pending",
  "Approved",
  "Rejected",
  "Canceled",
];

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
      eyebrow: "控制平面",
      title: "收件 / 审批中枢",
      intro: "把待办收件、审批决策与操作员注释收拢到一个可追溯界面里，避免动作在工作流之外失焦。",
      refresh: "刷新工作台",
      refreshing: "正在刷新...",
      inboxStatusFilter: "收件状态",
      approvalStatusFilter: "审批状态",
      inboxKindTabs: { all: "全部", approvals: "审批", automations: "自动化结果" },
      inboxTitle: "收件队列",
      approvalTitle: "审批队列",
      inboxDetailTitle: "收件焦点",
      approvalDetailTitle: "审批焦点",
      emptyInbox: "当前筛选下没有收件项目。",
      emptyApproval: "当前筛选下没有审批请求。",
      errorEyebrow: "异常",
      errorTitle: "收件或审批请求加载失败",
      actionRequired: "需要动作",
      notePlaceholder: "补充审批备注（可选）",
      approve: "批准",
      reject: "拒绝",
      status: "状态",
      kind: "类型",
      source: "来源",
      route: "路由",
      session: "会话",
      approvalLink: "关联审批",
      inboxLink: "关联收件",
      correlation: "关联 ID",
      requestedAt: "请求时间",
      lastUpdated: "最近更新",
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
      selectionHint: "从左侧队列选择对象，右侧保持当前焦点细节。",
      approvalKindLabels: {
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
        loadFailed: "加载收件或审批失败。",
        decisionFailed: "提交审批决策失败。",
        updateInboxFailed: "更新收件状态失败。",
      },
    },
    en: {
      common: {
        none: "n/a",
        loading: "Loading...",
        all: "All",
        items: (count: number) => `${count} items`,
      },
      eyebrow: "Control Plane",
      title: "Inbox / Approval Console",
      intro: "Keep inbox signals, approval decisions, and operator notes on one inspectable surface so actions never fall out of view.",
      refresh: "Refresh desk",
      refreshing: "Refreshing...",
      inboxStatusFilter: "Inbox status",
      approvalStatusFilter: "Approval status",
      inboxKindTabs: { all: "All", approvals: "Approvals", automations: "Automation Results" },
      inboxTitle: "Inbox",
      approvalTitle: "Approvals",
      inboxDetailTitle: "Inbox focus",
      approvalDetailTitle: "Approval focus",
      emptyInbox: "No inbox items match the current filter.",
      emptyApproval: "No approvals match the current filter.",
      errorEyebrow: "Error",
      errorTitle: "Inbox or approval request failed",
      actionRequired: "Action required",
      notePlaceholder: "Decision note (optional)",
      approve: "Approve",
      reject: "Reject",
      status: "Status",
      kind: "Kind",
      source: "Source",
      route: "Route",
      session: "Session",
      approvalLink: "Linked approval",
      inboxLink: "Linked inbox",
      correlation: "Correlation",
      requestedAt: "Requested at",
      lastUpdated: "Last updated",
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
      selectionHint: "Pick an object from the left queues and keep the current focus visible on the right.",
      approvalKindLabels: {
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
        loadFailed: "Failed to load inbox or approvals.",
        decisionFailed: "Failed to submit approval decision.",
        updateInboxFailed: "Failed to update inbox status.",
      },
    },
  });

  const [inboxItems, setInboxItems] = useState<InboxItem[]>([]);
  const [approvals, setApprovals] = useState<Approval[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [inboxStatusFilter, setInboxStatusFilter] = useState<InboxStatusFilter>("Open");
  const [approvalStatusFilter, setApprovalStatusFilter] = useState<ApprovalStatusFilter>("Pending");
  const [approvalNotes, setApprovalNotes] = useState<Record<string, string>>({});
  const [pendingApprovalIds, setPendingApprovalIds] = useState<Record<string, boolean>>({});
  const [pendingInboxIds, setPendingInboxIds] = useState<Record<string, boolean>>({});
  const [selectedInboxId, setSelectedInboxId] = useState<string | null>(null);
  const [selectedApprovalId, setSelectedApprovalId] = useState<string | null>(null);
  const [kindFilter, setKindFilter] = useState<'all' | 'approvals' | 'automations'>('all');

  const formatTimestamp = (value?: string | null): string => {
    return formatDateTime(value, text.common.none);
  };

  const formatInboxStatus = (status: InboxItemStatus): string => {
    return text.inboxStatusLabels[status] ?? status;
  };

  const formatApprovalStatus = (status: ApprovalStatus): string => {
    return text.approvalStatusLabels[status] ?? status;
  };

  const formatApprovalKind = (kind: InboxItem["kind"] | Approval["kind"]): string => {
    return text.approvalKindLabels[kind] ?? kind;
  };

  const formatDeliveryMode = (mode: DeliveryMode): string => {
    return text.deliveryModeLabels[mode] ?? mode;
  };

  function parseChannelDeliveryPayload(payloadJson?: string | null): ChannelDeliveryPayload | null {
    if (!payloadJson) {
      return null;
    }

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

  function buildChannelThreadDetailApi(bindingId: string): string {
    return resolveGatewayPath(`/api/channels/threads/${bindingId}`);
  }

  function buildChannelThreadAuditApi(bindingId: string): string {
    return resolveGatewayPath(`/api/channels/threads/${bindingId}/audit?limit=20`);
  }

  useEffect(() => {
    setSelectedInboxId((current) => {
      if (current && inboxItems.some((item) => item.id === current)) {
        return current;
      }

      return inboxItems[0]?.id ?? null;
    });
  }, [inboxItems]);

  useEffect(() => {
    setSelectedApprovalId((current) => {
      if (current && approvals.some((approval) => approval.id === current)) {
        return current;
      }

      return approvals[0]?.id ?? null;
    });
  }, [approvals]);

  async function loadData(loadingMode: "initial" | "refresh") {
    if (loadingMode === "initial") {
      setIsLoading(true);
    } else {
      setIsRefreshing(true);
    }

    setError(null);

    try {
      const [inboxResult, approvalResult] = await Promise.all([
        fetchInbox({
          limit: 50,
          status: inboxStatusFilter === "all" ? undefined : inboxStatusFilter,
        }),
        fetchApprovals({
          limit: 50,
          status: approvalStatusFilter === "all" ? undefined : approvalStatusFilter,
        }),
      ]);

      setInboxItems(inboxResult.items);
      setApprovals(approvalResult.items);
    } catch (nextError) {
      setError(nextError instanceof Error ? nextError.message : text.errors.loadFailed);
    } finally {
      if (loadingMode === "initial") {
        setIsLoading(false);
      } else {
        setIsRefreshing(false);
      }
    }
  }

  useEffect(() => {
    void loadData("initial");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [inboxStatusFilter, approvalStatusFilter]);

  async function handleRefresh() {
    await loadData("refresh");
  }

  async function handleApprovalDecision(approvalId: string, approve: boolean) {
    setPendingApprovalIds((current) => ({ ...current, [approvalId]: true }));

    try {
      const note = approvalNotes[approvalId] ?? "";
      await submitApprovalDecision(approvalId, approve, note);
      await loadData("refresh");
    } catch (nextError) {
      setError(nextError instanceof Error ? nextError.message : text.errors.decisionFailed);
    } finally {
      setPendingApprovalIds((current) => {
        const next = { ...current };
        delete next[approvalId];
        return next;
      });
    }
  }

  async function handleInboxStatusUpdate(inboxId: string, status: InboxItemStatus) {
    setPendingInboxIds((current) => ({ ...current, [inboxId]: true }));

    try {
      await updateInboxStatus(inboxId, status);
      await loadData("refresh");
    } catch (nextError) {
      setError(nextError instanceof Error ? nextError.message : text.errors.updateInboxFailed);
    } finally {
      setPendingInboxIds((current) => {
        const next = { ...current };
        delete next[inboxId];
        return next;
      });
    }
  }

  const selectedInboxItem = useMemo(
    () => inboxItems.find((item) => item.id === selectedInboxId) ?? null,
    [inboxItems, selectedInboxId],
  );

  const selectedApproval = useMemo(
    () => approvals.find((approval) => approval.id === selectedApprovalId) ?? null,
    [approvals, selectedApprovalId],
  );

  const selectedInboxPayload = useMemo(
    () => parseChannelDeliveryPayload(selectedInboxItem?.payloadJson),
    [selectedInboxItem],
  );

  const selectedApprovalPayload = useMemo(
    () => parseChannelDeliveryPayload(selectedApproval?.payloadJson),
    [selectedApproval],
  );

  const filteredInboxItems = useMemo(() => {
    if (kindFilter === 'approvals') return inboxItems.filter(i => i.kind !== 'AutomationResult');
    if (kindFilter === 'automations') return inboxItems.filter(i => i.kind === 'AutomationResult');
    return inboxItems;
  }, [inboxItems, kindFilter]);

  return (
    <section data-testid="inbox-approval-desk" className="control-plane-stack">
      <h2 className="desk-section-title">{text.title}</h2>
      <p className="desk-section-desc">{text.intro}</p>

      <div className="control-plane-toolbar">
        <button
          type="button"
          className="secondary-button"
          data-testid="inbox-refresh"
          disabled={isLoading || isRefreshing}
          onClick={() => {
            void handleRefresh();
          }}
        >
          {isRefreshing ? text.refreshing : text.refresh}
        </button>
        <label className="metric-label" htmlFor="inbox-status-filter">
          {text.inboxStatusFilter}
        </label>
        <select
          id="inbox-status-filter"
          data-testid="inbox-status-filter"
          className="bootstrap-form__textarea control-plane-filter control-plane-select"
          value={inboxStatusFilter}
          onChange={(event) => setInboxStatusFilter(event.target.value as InboxStatusFilter)}
        >
          <option value="all">{text.common.all}</option>
          {INBOX_STATUS_OPTIONS.map((status) => (
            <option value={status} key={status}>
              {formatInboxStatus(status)}
            </option>
          ))}
        </select>
        <label className="metric-label" htmlFor="approval-status-filter">
          {text.approvalStatusFilter}
        </label>
        <select
          id="approval-status-filter"
          data-testid="approval-status-filter"
          className="bootstrap-form__textarea control-plane-filter control-plane-select"
          value={approvalStatusFilter}
          onChange={(event) => setApprovalStatusFilter(event.target.value as ApprovalStatusFilter)}
        >
          <option value="all">{text.common.all}</option>
          {APPROVAL_STATUS_OPTIONS.map((status) => (
            <option value={status} key={status}>
              {formatApprovalStatus(status)}
            </option>
          ))}
        </select>
      </div>

      {error ? (
        <section className="status-card status-card--error" data-testid="inbox-approval-error">
          <h3 className="desk-section-title">{text.errorTitle}</h3>
          <p className="desk-section-desc">{error}</p>
        </section>
      ) : null}

      <div className="control-plane-pane-shell">
        <div className="control-plane-pane-rail control-plane-stack">
        <section className="timeline" data-testid="inbox-list">
          <div className="timeline__header">
            <div>
              <h3 className="desk-section-title">{text.inboxTitle}</h3>
              <p className="desk-section-desc">{text.selectionHint}</p>
            </div>
            <span className="composer__status">
              {isLoading ? text.common.loading : text.common.items(filteredInboxItems.length)}
            </span>
          </div>
          <div className="inbox-kind-tabs" data-testid="inbox-kind-tabs">
            {(['all', 'approvals', 'automations'] as const).map(tab => (
              <button
                key={tab}
                type="button"
                className={`inbox-kind-tab${kindFilter === tab ? ' inbox-kind-tab--active' : ''}`}
                onClick={() => setKindFilter(tab)}
              >
                {tab === 'automations' && <Zap size={12} strokeWidth={1.75} />}
                {text.inboxKindTabs[tab]}
              </button>
            ))}
          </div>
          <div className="timeline__body">
            {isLoading ? <Skeleton height={52} count={3} /> : null}
            {!isLoading && filteredInboxItems.length === 0 ? (
              <EmptyState icon={<Inbox size={28} strokeWidth={1.5} />} title={text.emptyInbox} />
            ) : null}
            {filteredInboxItems.map((item) => {
              const pending = pendingInboxIds[item.id] ?? false;
              const isAutomationResult = item.kind === 'AutomationResult';
              return (
                <article
                  key={item.id}
                  className={`message message--system control-plane-stack control-plane-queue-card ${selectedInboxId === item.id ? "control-plane-list-button--selected" : ""}${isAutomationResult ? " inbox-automation-result-card" : ""}`}
                  data-testid={`inbox-item-${item.id}`}
                  onClick={() => {
                    setSelectedInboxId(item.id);
                    if (item.approvalId) {
                      setSelectedApprovalId(item.approvalId);
                    }
                  }}
                >
                  <div className="message__meta">
                    {isAutomationResult
                      ? <span className="message__role inbox-automation-result-kind"><Zap size={12} strokeWidth={1.75} />{formatApprovalKind(item.kind)}</span>
                      : <span className="message__role">{formatApprovalKind(item.kind)}</span>
                    }
                    <span>{formatTimestamp(item.updatedAt)}</span>
                  </div>
                  <strong>{item.title}</strong>
                  <p className="control-plane-compact-copy">{item.summary}</p>
                  <div className="control-plane-inline-actions">
                    {isAutomationResult ? (
                      <button
                        type="button"
                        className="secondary-button"
                        data-testid={`inbox-mark-read-${item.id}`}
                        disabled={pending || item.status === 'Acknowledged'}
                        onClick={(e) => {
                          e.stopPropagation();
                          setSelectedInboxId(item.id);
                          void handleInboxStatusUpdate(item.id, 'Acknowledged');
                        }}
                      >
                        {text.markRead}
                      </button>
                    ) : (
                      <>
                        <span className="metric-label">{text.status}</span>
                        <select
                          data-testid={`inbox-status-${item.id}`}
                          className="bootstrap-form__textarea control-plane-select"
                          value={item.status}
                          disabled={pending}
                          onChange={(event) => {
                            setSelectedInboxId(item.id);
                            void handleInboxStatusUpdate(item.id, event.target.value as InboxItemStatus);
                          }}
                        >
                          {INBOX_STATUS_OPTIONS.map((status) => (
                            <option value={status} key={status}>
                              {formatInboxStatus(status)}
                            </option>
                          ))}
                        </select>
                      </>
                    )}
                    {item.requiresAction ? (
                      <span className="stream-indicator is-live">{text.actionRequired}</span>
                    ) : null}
                  </div>
                </article>
              );
            })}
          </div>
        </section>

        <section className="timeline" data-testid="approval-list">
          <div className="timeline__header">
            <h3 className="desk-section-title">{text.approvalTitle}</h3>
            <span className="composer__status">
              {isLoading ? text.common.loading : text.common.items(approvals.length)}
            </span>
          </div>
          <div className="timeline__body">
            {isLoading ? <Skeleton height={52} count={3} /> : null}
            {!isLoading && approvals.length === 0 ? (
              <EmptyState icon={<Inbox size={28} strokeWidth={1.5} />} title={text.emptyApproval} />
            ) : null}
            {approvals.map((approval) => {
              const pending = pendingApprovalIds[approval.id] ?? false;
              const note = approvalNotes[approval.id] ?? "";
              const actionable = approval.status === "Pending";
              return (
                <article
                  key={approval.id}
                  className={`message message--assistant control-plane-stack control-plane-queue-card ${selectedApprovalId === approval.id ? "control-plane-list-button--selected" : ""}`}
                  data-testid={`approval-item-${approval.id}`}
                  onClick={() => {
                    setSelectedApprovalId(approval.id);
                    if (approval.inboxItemId) {
                      setSelectedInboxId(approval.inboxItemId);
                    }
                  }}
                >
                  <div className="message__meta">
                    <span className="message__role">{formatApprovalKind(approval.kind)}</span>
                    <span>{formatApprovalStatus(approval.status)}</span>
                  </div>
                  <strong>{approval.title}</strong>
                  <p className="control-plane-compact-copy">{approval.summary}</p>
                  <textarea
                    data-testid={`approval-note-${approval.id}`}
                    value={note}
                    rows={2}
                    className="bootstrap-form__textarea"
                    disabled={!actionable || pending}
                    onChange={(event) => {
                      setSelectedApprovalId(approval.id);
                      setApprovalNotes((current) => ({
                        ...current,
                        [approval.id]: event.target.value,
                      }));
                    }}
                    placeholder={text.notePlaceholder}
                  />
                  <div className="control-plane-inline-actions">
                    <button
                      type="button"
                      className="secondary-button"
                      data-testid="approval-approve"
                      disabled={!actionable || pending}
                    onClick={() => {
                        setSelectedApprovalId(approval.id);
                        void handleApprovalDecision(approval.id, true);
                      }}
                    >
                      {text.approve}
                    </button>
                    <button
                      type="button"
                      className="secondary-button"
                      data-testid="approval-reject"
                      disabled={!actionable || pending}
                    onClick={() => {
                        setSelectedApprovalId(approval.id);
                        void handleApprovalDecision(approval.id, false);
                      }}
                    >
                      {text.reject}
                    </button>
                  </div>
                </article>
              );
            })}
          </div>
        </section>
        </div>

        <div className="control-plane-pane-stage">
          <section className="status-card status-card--normal control-plane-stage-hero" data-testid="inbox-detail">
            {selectedInboxItem ? (
              <div className="control-plane-stack">
                <div className="control-plane-stage-hero__header">
                  <div>
                    <h3 className="desk-section-title control-plane-card-title">{selectedInboxItem.title}</h3>
                    <p className="desk-section-desc">{selectedInboxItem.summary}</p>
                  </div>
                  <div className="control-plane-chip-row">
                    <span className="control-plane-chip">{formatInboxStatus(selectedInboxItem.status)}</span>
                    {selectedInboxItem.requiresAction ? (
                      <span className="control-plane-chip control-plane-chip--warning">{text.actionRequired}</span>
                    ) : null}
                  </div>
                </div>

                <div className="control-plane-summary-grid">
                  <div className="metric-item">
                    <span className="metric-label">{text.kind}</span>
                    <span className="metric-value">{formatApprovalKind(selectedInboxItem.kind)}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.lastUpdated}</span>
                    <span className="metric-value">{formatTimestamp(selectedInboxItem.updatedAt)}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.source}</span>
                    <span className="metric-value metric-value--path">{selectedInboxItem.source}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.approvalLink}</span>
                    <span className="metric-value metric-value--path">{selectedInboxItem.approvalId ?? text.common.none}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.session}</span>
                    <span className="metric-value metric-value--path">{selectedInboxItem.sessionId ?? text.common.none}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.route}</span>
                    <span className="metric-value metric-value--path">{selectedInboxItem.route ?? text.common.none}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.correlation}</span>
                    <span className="metric-value metric-value--path">{selectedInboxItem.correlationId ?? text.common.none}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.status}</span>
                    <span className="metric-value">{formatInboxStatus(selectedInboxItem.status)}</span>
                  </div>
                </div>

                {selectedInboxPayload ? (
                  <div className="control-plane-summary-grid">
                    <div className="metric-item">
                      <span className="metric-label">{text.deliveryContext}</span>
                      <span className="metric-value">{formatDeliveryMode(selectedInboxPayload.deliveryMode)}</span>
                    </div>
                    <div className="metric-item">
                      <span className="metric-label">{text.connector}</span>
                      <span className="metric-value metric-value--path">{selectedInboxPayload.connectorKind}</span>
                    </div>
                    <div className="metric-item">
                      <span className="metric-label">{text.account}</span>
                      <span className="metric-value metric-value--path">{selectedInboxPayload.accountId}</span>
                    </div>
                    <div className="metric-item">
                      <span className="metric-label">{text.threadBinding}</span>
                      <span className="metric-value metric-value--path">{selectedInboxPayload.bindingId}</span>
                    </div>
                    <div className="metric-item">
                      <span className="metric-label">{text.thread}</span>
                      <span className="metric-value metric-value--path">{selectedInboxPayload.externalThreadId}</span>
                    </div>
                    <div className="metric-item">
                      <span className="metric-label">{text.draft}</span>
                      <span className="metric-value metric-value--path">{selectedInboxPayload.draftId}</span>
                    </div>
                    <div className="metric-item">
                      <span className="metric-label">{text.messagePreview}</span>
                      <span className="metric-value">{selectedInboxPayload.messageText}</span>
                    </div>
                    {selectedInboxPayload.mediaAttachments?.filter(a => a.contentType.startsWith("image/")).map(a => (
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
                        {buildChannelThreadDetailApi(selectedInboxPayload.bindingId)}
                      </span>
                    </div>
                    <div className="metric-item">
                      <span className="metric-label">{text.threadAuditApi}</span>
                      <span className="metric-value metric-value--path">
                        {buildChannelThreadAuditApi(selectedInboxPayload.bindingId)}
                      </span>
                    </div>
                  </div>
                ) : null}
              </div>
            ) : (
              <EmptyState icon={<Inbox size={28} strokeWidth={1.5} />} title={text.emptyInbox} />
            )}
          </section>

          <section className="status-card status-card--warning control-plane-stage-panel" data-testid="approval-detail">
            {selectedApproval ? (
              <div className="control-plane-stack">
                <div className="control-plane-stage-hero__header">
                  <div>
                    <h3 className="desk-section-title control-plane-card-title">{selectedApproval.title}</h3>
                    <p className="desk-section-desc">{selectedApproval.summary}</p>
                  </div>
                  <div className="control-plane-chip-row">
                    <span className="control-plane-chip">{formatApprovalStatus(selectedApproval.status)}</span>
                    <span className="control-plane-chip">{formatApprovalKind(selectedApproval.kind)}</span>
                  </div>
                </div>

                <div className="control-plane-summary-grid">
                  <div className="metric-item">
                    <span className="metric-label">{text.requestedAt}</span>
                    <span className="metric-value">{formatTimestamp(selectedApproval.requestedAt)}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.lastUpdated}</span>
                    <span className="metric-value">{formatTimestamp(selectedApproval.updatedAt)}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.source}</span>
                    <span className="metric-value metric-value--path">{selectedApproval.source}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.inboxLink}</span>
                    <span className="metric-value metric-value--path">{selectedApproval.inboxItemId ?? text.common.none}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.session}</span>
                    <span className="metric-value metric-value--path">{selectedApproval.sessionId ?? text.common.none}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.correlation}</span>
                    <span className="metric-value metric-value--path">{selectedApproval.correlationId ?? text.common.none}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.decisionNote}</span>
                    <span className="metric-value">{selectedApproval.decisionNote ?? text.common.none}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.status}</span>
                    <span className="metric-value">{formatApprovalStatus(selectedApproval.status)}</span>
                  </div>
                </div>

                {selectedApprovalPayload ? (
                  <div className="control-plane-summary-grid">
                    <div className="metric-item">
                      <span className="metric-label">{text.deliveryContext}</span>
                      <span className="metric-value">{formatDeliveryMode(selectedApprovalPayload.deliveryMode)}</span>
                    </div>
                    <div className="metric-item">
                      <span className="metric-label">{text.connector}</span>
                      <span className="metric-value metric-value--path">{selectedApprovalPayload.connectorKind}</span>
                    </div>
                    <div className="metric-item">
                      <span className="metric-label">{text.account}</span>
                      <span className="metric-value metric-value--path">{selectedApprovalPayload.accountId}</span>
                    </div>
                    <div className="metric-item">
                      <span className="metric-label">{text.threadBinding}</span>
                      <span className="metric-value metric-value--path">{selectedApprovalPayload.bindingId}</span>
                    </div>
                    <div className="metric-item">
                      <span className="metric-label">{text.thread}</span>
                      <span className="metric-value metric-value--path">{selectedApprovalPayload.externalThreadId}</span>
                    </div>
                    <div className="metric-item">
                      <span className="metric-label">{text.draft}</span>
                      <span className="metric-value metric-value--path">{selectedApprovalPayload.draftId}</span>
                    </div>
                    <div className="metric-item">
                      <span className="metric-label">{text.messagePreview}</span>
                      <span className="metric-value">{selectedApprovalPayload.messageText}</span>
                    </div>
                    {selectedApprovalPayload.mediaAttachments?.filter(a => a.contentType.startsWith("image/")).map(a => (
                      <div key={a.mediaId} className="metric-item metric-item--full-width">
                        <img
                          src={`/api/media/${a.mediaId}`}
                          alt={a.mediaId}
                          className="inbox-media-thumbnail"
                          data-testid={`approval-media-thumbnail-${a.mediaId}`}
                        />
                      </div>
                    ))}
                    <div className="metric-item">
                      <span className="metric-label">{text.threadDetailApi}</span>
                      <span className="metric-value metric-value--path">
                        {buildChannelThreadDetailApi(selectedApprovalPayload.bindingId)}
                      </span>
                    </div>
                    <div className="metric-item">
                      <span className="metric-label">{text.threadAuditApi}</span>
                      <span className="metric-value metric-value--path">
                        {buildChannelThreadAuditApi(selectedApprovalPayload.bindingId)}
                      </span>
                    </div>
                  </div>
                ) : null}
              </div>
            ) : (
              <EmptyState icon={<Inbox size={28} strokeWidth={1.5} />} title={text.emptyApproval} />
            )}
          </section>
        </div>
      </div>
    </section>
  );
}
