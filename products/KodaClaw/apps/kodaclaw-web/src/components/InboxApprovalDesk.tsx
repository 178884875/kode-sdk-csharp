import { useEffect, useState } from "react";
import {
  fetchApprovals,
  fetchInbox,
  submitApprovalDecision,
  updateInboxStatus,
} from "../lib/api";
import { useI18n, useLocaleText } from "../i18n/I18nProvider";
import type {
  Approval,
  ApprovalStatus,
  InboxItem,
  InboxItemStatus,
} from "../types/contracts";
import "./ControlPlaneDesk.css";

type InboxStatusFilter = InboxItemStatus | "all";
type ApprovalStatusFilter = ApprovalStatus | "all";

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
      inboxTitle: "收件队列",
      approvalTitle: "审批队列",
      emptyInbox: "当前筛选下没有收件项目。",
      emptyApproval: "当前筛选下没有审批请求。",
      errorEyebrow: "异常",
      errorTitle: "收件或审批请求加载失败",
      actionRequired: "需要动作",
      notePlaceholder: "补充审批备注（可选）",
      approve: "批准",
      reject: "拒绝",
      status: "状态",
      lastUpdated: "最近更新",
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
      inboxTitle: "Inbox",
      approvalTitle: "Approvals",
      emptyInbox: "No inbox items match the current filter.",
      emptyApproval: "No approvals match the current filter.",
      errorEyebrow: "Error",
      errorTitle: "Inbox or approval request failed",
      actionRequired: "Action required",
      notePlaceholder: "Decision note (optional)",
      approve: "Approve",
      reject: "Reject",
      status: "Status",
      lastUpdated: "Last updated",
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

  return (
    <section data-testid="inbox-approval-desk" className="bootstrap-panel control-plane-stack">
      <div className="section-eyebrow">{text.eyebrow}</div>
      <h2 className="section-title">{text.title}</h2>
      <p className="section-copy">{text.intro}</p>

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
          <p className="section-eyebrow">{text.errorEyebrow}</p>
          <h3 className="section-title">{text.errorTitle}</h3>
          <p className="section-copy">{error}</p>
        </section>
      ) : null}

      <div className="control-plane-two-pane">
        <section className="timeline" data-testid="inbox-list">
          <div className="timeline__header">
            <h3 className="section-title">{text.inboxTitle}</h3>
            <span className="composer__status">
              {isLoading ? text.common.loading : text.common.items(inboxItems.length)}
            </span>
          </div>
          <div className="timeline__body">
            {!isLoading && inboxItems.length === 0 ? (
              <p className="timeline__empty">{text.emptyInbox}</p>
            ) : null}
            {inboxItems.map((item) => {
              const pending = pendingInboxIds[item.id] ?? false;
              return (
                <article
                  key={item.id}
                  className="message message--system control-plane-stack"
                  data-testid={`inbox-item-${item.id}`}
                >
                  <div className="message__meta">
                    <span className="message__role">{formatApprovalKind(item.kind)}</span>
                    <span>{formatTimestamp(item.updatedAt)}</span>
                  </div>
                  <strong>{item.title}</strong>
                  <p className="control-plane-compact-copy">{item.summary}</p>
                  <div className="control-plane-inline-actions">
                    <span className="metric-label">{text.status}</span>
                    <select
                      data-testid={`inbox-status-${item.id}`}
                      className="bootstrap-form__textarea control-plane-select"
                      value={item.status}
                      disabled={pending}
                      onChange={(event) => {
                        void handleInboxStatusUpdate(item.id, event.target.value as InboxItemStatus);
                      }}
                    >
                      {INBOX_STATUS_OPTIONS.map((status) => (
                        <option value={status} key={status}>
                          {formatInboxStatus(status)}
                        </option>
                      ))}
                    </select>
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
            <h3 className="section-title">{text.approvalTitle}</h3>
            <span className="composer__status">
              {isLoading ? text.common.loading : text.common.items(approvals.length)}
            </span>
          </div>
          <div className="timeline__body">
            {!isLoading && approvals.length === 0 ? (
              <p className="timeline__empty">{text.emptyApproval}</p>
            ) : null}
            {approvals.map((approval) => {
              const pending = pendingApprovalIds[approval.id] ?? false;
              const note = approvalNotes[approval.id] ?? "";
              const actionable = approval.status === "Pending";
              return (
                <article
                  key={approval.id}
                  className="message message--assistant control-plane-stack"
                  data-testid={`approval-item-${approval.id}`}
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
    </section>
  );
}
