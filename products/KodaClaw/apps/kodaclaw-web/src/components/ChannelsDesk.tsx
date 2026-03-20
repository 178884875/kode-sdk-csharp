import { type CSSProperties, useEffect, useMemo, useRef, useState } from "react";
import {
  fetchChannelAccounts,
  fetchChannelConnectors,
  fetchChannelThreadAudit,
  fetchChannelThreadDetail,
  fetchChannelThreads,
} from "../lib/api";
import { useI18n, useLocaleText } from "../i18n/I18nProvider";
import type {
  ChannelAccount,
  ChannelAuditEntry,
  ChannelConnectorDescriptor,
  ChannelConnectorKind,
  ChannelThreadDetail,
  ChannelThreadSummary,
  DeliveryMode,
  SessionKind,
} from "../types/contracts";

type ConnectorFilter = ChannelConnectorKind | "all";
type AccountFilter = "all" | string;

const THREAD_LIMIT = 80;
const AUDIT_LIMIT = 20;

const toolbarStyle: CSSProperties = {
  marginTop: 12,
  display: "flex",
  gap: 10,
  flexWrap: "wrap",
  alignItems: "center",
};

const connectorSelectStyle: CSSProperties = {
  minWidth: 180,
};

const accountSelectStyle: CSSProperties = {
  minWidth: 240,
};

const splitLayoutStyle: CSSProperties = {
  display: "grid",
  gap: 18,
  gridTemplateColumns: "minmax(0, 1fr) minmax(0, 1.15fr)",
  marginTop: 16,
};

const connectorsBodyStyle: CSSProperties = {
  maxHeight: "min(50vh, 620px)",
};

const threadListStyle: CSSProperties = {
  marginTop: 12,
  display: "grid",
  gap: 10,
  maxHeight: "min(42vh, 420px)",
  overflow: "auto",
};

const threadButtonStyle: CSSProperties = {
  textAlign: "left",
  borderRadius: 18,
  padding: "12px 14px",
};

const detailPanelStyle: CSSProperties = {
  marginTop: 16,
  display: "grid",
  gap: 10,
};

export function ChannelsDesk() {
  const { formatDateTime } = useI18n();
  const text = useLocaleText({
    zh: {
      eyebrow: "渠道枢纽",
      title: "渠道运营台",
      copy:
        "统一查看连接器账号、线程绑定，并在一个界面上核对策略、投递规则与待审批状态。",
      refresh: "刷新渠道面板",
      refreshing: "刷新中...",
      filters: {
        connector: "连接器",
        account: "账号",
      },
      allConnectors: "全部连接器",
      allAccounts: "全部账号",
      loading: "加载中...",
      summary: (accounts: number, threads: number) => `${accounts} 个账号 · ${threads} 条线程`,
      connectorsTitle: "连接器与账号",
      connectorsCount: (count: number) => `${count} 个连接器`,
      connectorSummary: {
        implemented: "已实现",
        planned: "规划中",
        inbound: "支持入站",
        noInbound: "无入站",
        outbound: "支持出站",
        noOutbound: "无出站",
      },
      accountState: {
        Connected: "已连接",
        Connecting: "连接中",
        Disconnected: "未连接",
        Degraded: "受限",
      },
      updatedAt: (value: string) => `更新时间 ${value}`,
      emptyConnectors: "Gateway 尚未返回渠道连接器或账号。",
      threadEyebrow: "线程绑定",
      threadTitle: "线程索引",
      noPreview: "暂无预览文本。",
      pendingApproval: (approvalId: string) => `待审批 ${approvalId}`,
      pendingDraft: "草稿待处理",
      noPendingDraft: "暂无待处理草稿",
      loadingDetail: "正在加载线程详情...",
      emptyThreads: "当前筛选下没有匹配的线程绑定。",
      emptyDetail: "选择一个线程以查看策略、投递规则与最近审计轨迹。",
      detail: {
        binding: "绑定",
        sessionKind: "会话类型",
        deliveryMode: "投递模式",
        policy: "策略",
        replyGuard: "回复保护",
        pending: "待处理",
        recentAudit: "最近审计",
        noAudit: "暂无审计记录。",
      },
      policySummary: (profile: string, memory: string) => `用户画像 ${profile} · 长期记忆 ${memory}`,
      replyGuardSummary: (directReply: string, mentionRequired: string) =>
        `直接回复 ${directReply} · 显式提及 ${mentionRequired}`,
      on: "开",
      off: "关",
      yes: "是",
      no: "否",
      detailPendingApproval: (approvalId: string) => `审批 ${approvalId}`,
      detailPendingDraft: "草稿待审批",
      detailNoPending: "暂无待审批或待发送草稿",
      unavailable: "暂无",
      loadDetailError: "加载线程详情失败。",
      loadDeskError: "加载渠道工作台失败。",
      deliveryMode: {
        AutoSend: "自动发送",
        DraftApproval: "草稿审批",
        RequireApproval: "需审批后发送",
      },
      sessionKind: {
        Main: "主会话",
        ChannelDirectMessage: "渠道私信",
        ChannelGroup: "渠道群组",
        Automation: "自动化",
        Plugin: "插件",
      },
    },
    en: {
      eyebrow: "Channel Hub",
      title: "Channels Operations Desk",
      copy:
        "Monitor connector accounts, inspect thread bindings, and verify policy, delivery rule, and pending approval state on one surface.",
      refresh: "Refresh channels",
      refreshing: "Refreshing...",
      filters: {
        connector: "Connector",
        account: "Account",
      },
      allConnectors: "All connectors",
      allAccounts: "All accounts",
      loading: "Loading...",
      summary: (accounts: number, threads: number) => `${accounts} accounts · ${threads} threads`,
      connectorsTitle: "Connectors & Accounts",
      connectorsCount: (count: number) => `${count} connectors`,
      connectorSummary: {
        implemented: "Implemented",
        planned: "Planned",
        inbound: "Inbound",
        noInbound: "No inbound",
        outbound: "Outbound",
        noOutbound: "No outbound",
      },
      accountState: {
        Connected: "Connected",
        Connecting: "Connecting",
        Disconnected: "Disconnected",
        Degraded: "Degraded",
      },
      updatedAt: (value: string) => `Updated ${value}`,
      emptyConnectors: "No channel connectors or accounts returned by Gateway.",
      threadEyebrow: "Thread Bindings",
      threadTitle: "Thread Index",
      noPreview: "No preview text.",
      pendingApproval: (approvalId: string) => `Pending approval ${approvalId}`,
      pendingDraft: "Pending draft",
      noPendingDraft: "No pending draft",
      loadingDetail: "Loading thread detail...",
      emptyThreads: "No thread bindings matched this filter.",
      emptyDetail: "Select a thread to inspect policy, delivery rule, and recent audit trail.",
      detail: {
        binding: "Binding",
        sessionKind: "Session kind",
        deliveryMode: "Delivery mode",
        policy: "Policy",
        replyGuard: "Reply guard",
        pending: "Pending",
        recentAudit: "Recent audit",
        noAudit: "No audit entries.",
      },
      policySummary: (profile: string, memory: string) => `user-profile ${profile} · memory ${memory}`,
      replyGuardSummary: (directReply: string, mentionRequired: string) =>
        `direct-reply ${directReply} · mention-required ${mentionRequired}`,
      on: "on",
      off: "off",
      yes: "yes",
      no: "no",
      detailPendingApproval: (approvalId: string) => `Approval ${approvalId}`,
      detailPendingDraft: "Draft pending",
      detailNoPending: "No pending approval/draft",
      unavailable: "n/a",
      loadDetailError: "Failed to load thread detail.",
      loadDeskError: "Failed to load channels desk.",
      deliveryMode: {
        AutoSend: "Auto send",
        DraftApproval: "Draft approval",
        RequireApproval: "Require approval",
      },
      sessionKind: {
        Main: "Main",
        ChannelDirectMessage: "Channel direct message",
        ChannelGroup: "Channel group",
        Automation: "Automation",
        Plugin: "Plugin",
      },
    },
  });

  const [connectors, setConnectors] = useState<ChannelConnectorDescriptor[]>([]);
  const [accounts, setAccounts] = useState<ChannelAccount[]>([]);
  const [threads, setThreads] = useState<ChannelThreadSummary[]>([]);
  const [selectedBindingId, setSelectedBindingId] = useState<string | null>(null);
  const [threadDetail, setThreadDetail] = useState<ChannelThreadDetail | null>(null);
  const [threadAudit, setThreadAudit] = useState<ChannelAuditEntry[]>([]);
  const [connectorFilter, setConnectorFilter] = useState<ConnectorFilter>("all");
  const [accountFilter, setAccountFilter] = useState<AccountFilter>("all");
  const [isLoadingList, setIsLoadingList] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [isLoadingDetail, setIsLoadingDetail] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const listRequestRef = useRef(0);
  const detailRequestRef = useRef(0);

  const selectedThread = useMemo(
    () => threads.find((item) => item.bindingId === selectedBindingId) ?? null,
    [threads, selectedBindingId],
  );

  const visibleAccounts = useMemo(() => {
    if (connectorFilter === "all") {
      return accounts;
    }

    return accounts.filter((item) => item.connectorKind === connectorFilter);
  }, [accounts, connectorFilter]);

  function resolveAccountStateLabel(state: ChannelAccount["state"]): string {
    return text.accountState[state] ?? state;
  }

  function resolveDeliveryModeLabel(mode: DeliveryMode): string {
    return text.deliveryMode[mode] ?? mode;
  }

  function resolveSessionKindLabel(kind: SessionKind): string {
    return text.sessionKind[kind] ?? kind;
  }

  function trimText(value?: string | null, maxLength = 92): string {
    const normalized = (value ?? "").replace(/\s+/g, " ").trim();
    if (!normalized) {
      return text.noPreview;
    }

    if (normalized.length <= maxLength) {
      return normalized;
    }

    return `${normalized.slice(0, maxLength - 3)}...`;
  }

  function summarizeConnector(connector: ChannelConnectorDescriptor): string {
    const capabilityLabel = `${connector.supportsInbound ? text.connectorSummary.inbound : text.connectorSummary.noInbound} · ${connector.supportsOutbound ? text.connectorSummary.outbound : text.connectorSummary.noOutbound}`;
    return `${connector.implemented ? text.connectorSummary.implemented : text.connectorSummary.planned} · ${capabilityLabel}`;
  }

  function resolveAccountLabel(account: ChannelAccount): string {
    return `${account.displayName} · ${resolveAccountStateLabel(account.state)}`;
  }

  function resolveThreadState(thread: ChannelThreadSummary): string {
    if (thread.pendingApprovalId) {
      return text.pendingApproval(thread.pendingApprovalId);
    }

    if (thread.hasPendingDraft) {
      return text.pendingDraft;
    }

    return text.noPendingDraft;
  }

  async function loadDetail(bindingId: string | null) {
    const requestId = ++detailRequestRef.current;

    if (!bindingId) {
      setThreadDetail(null);
      setThreadAudit([]);
      setIsLoadingDetail(false);
      return;
    }

    setIsLoadingDetail(true);

    try {
      const [detail, audit] = await Promise.all([
        fetchChannelThreadDetail(bindingId),
        fetchChannelThreadAudit(bindingId, { limit: AUDIT_LIMIT }),
      ]);

      if (detailRequestRef.current !== requestId) {
        return;
      }

      setThreadDetail(detail);
      setThreadAudit(audit);
    } catch (nextError) {
      if (detailRequestRef.current !== requestId) {
        return;
      }

      setThreadDetail(null);
      setThreadAudit([]);
      setError(nextError instanceof Error ? nextError.message : text.loadDetailError);
    } finally {
      if (detailRequestRef.current === requestId) {
        setIsLoadingDetail(false);
      }
    }
  }

  async function loadDesk(mode: "initial" | "refresh") {
    const requestId = ++listRequestRef.current;

    if (mode === "initial") {
      setIsLoadingList(true);
    } else {
      setIsRefreshing(true);
    }

    setError(null);

    try {
      const [connectorsPayload, accountsPayload, threadsPayload] = await Promise.all([
        fetchChannelConnectors(),
        fetchChannelAccounts({
          connectorKind: connectorFilter === "all" ? undefined : connectorFilter,
          limit: THREAD_LIMIT,
        }),
        fetchChannelThreads({
          connectorKind: connectorFilter === "all" ? undefined : connectorFilter,
          accountId: accountFilter === "all" ? undefined : accountFilter,
          limit: THREAD_LIMIT,
        }),
      ]);

      if (listRequestRef.current !== requestId) {
        return;
      }

      setConnectors(connectorsPayload);
      setAccounts(accountsPayload);
      setThreads(threadsPayload.items);

      const preferredBindingId = threadsPayload.items.some((item) => item.bindingId === selectedBindingId)
        ? selectedBindingId
        : threadsPayload.items[0]?.bindingId ?? null;
      setSelectedBindingId(preferredBindingId);
      await loadDetail(preferredBindingId);
    } catch (nextError) {
      if (listRequestRef.current !== requestId) {
        return;
      }

      setConnectors([]);
      setAccounts([]);
      setThreads([]);
      setSelectedBindingId(null);
      setThreadDetail(null);
      setThreadAudit([]);
      setError(nextError instanceof Error ? nextError.message : text.loadDeskError);
    } finally {
      if (listRequestRef.current === requestId) {
        if (mode === "initial") {
          setIsLoadingList(false);
        } else {
          setIsRefreshing(false);
        }
      }
    }
  }

  useEffect(() => {
    void loadDesk("initial");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [connectorFilter, accountFilter]);

  async function handleRefresh() {
    await loadDesk("refresh");
  }

  async function handleSelectThread(bindingId: string) {
    setSelectedBindingId(bindingId);
    setError(null);
    await loadDetail(bindingId);
  }

  return (
    <section className="bootstrap-panel" data-testid="channels-desk">
      <div className="section-eyebrow">{text.eyebrow}</div>
      <h2 className="section-title">{text.title}</h2>
      <p className="section-copy">{text.copy}</p>

      <div className="channels-desk__toolbar" style={toolbarStyle}>
        <button
          type="button"
          className="secondary-button"
          data-testid="channels-refresh"
          disabled={isLoadingList || isRefreshing}
          onClick={() => {
            void handleRefresh();
          }}
        >
          {isRefreshing ? text.refreshing : text.refresh}
        </button>

        <label className="metric-label" htmlFor="channels-connector-filter">
          {text.filters.connector}
        </label>
        <select
          id="channels-connector-filter"
          className="bootstrap-form__textarea"
          data-testid="channels-connector-filter"
          value={connectorFilter}
          onChange={(event) => {
            setConnectorFilter(event.target.value as ConnectorFilter);
            setAccountFilter("all");
          }}
          style={connectorSelectStyle}
        >
          <option value="all">{text.allConnectors}</option>
          {connectors.map((connector) => (
            <option value={connector.kind} key={connector.kind}>
              {connector.displayName}
            </option>
          ))}
        </select>

        <label className="metric-label" htmlFor="channels-account-filter">
          {text.filters.account}
        </label>
        <select
          id="channels-account-filter"
          className="bootstrap-form__textarea"
          data-testid="channels-account-filter"
          value={accountFilter}
          onChange={(event) => setAccountFilter(event.target.value as AccountFilter)}
          style={accountSelectStyle}
        >
          <option value="all">{text.allAccounts}</option>
          {visibleAccounts.map((account) => (
            <option value={account.id} key={account.id}>
              {resolveAccountLabel(account)}
            </option>
          ))}
        </select>

        <span className="composer__status" data-testid="channels-summary">
          {isLoadingList ? text.loading : text.summary(accounts.length, threads.length)}
        </span>
      </div>

      {error ? (
        <p className="bootstrap-panel__feedback bootstrap-panel__feedback--error" data-testid="channels-error">
          {error}
        </p>
      ) : null}

      <div className="channels-desk__layout" style={splitLayoutStyle}>
        <section className="timeline" data-testid="channels-connectors-accounts">
          <div className="timeline__header">
            <h3 className="section-title">{text.connectorsTitle}</h3>
            <span className="composer__status">{text.connectorsCount(connectors.length)}</span>
          </div>
          <div className="timeline__body" style={connectorsBodyStyle}>
            {connectors.map((connector) => (
              <article className="message message--assistant" key={connector.kind}>
                <div className="message__meta">
                  <span className="message__role">{connector.displayName}</span>
                  <span>{connector.kind}</span>
                </div>
                <span>{summarizeConnector(connector)}</span>
              </article>
            ))}

            {accounts.map((account) => (
              <article className="message message--system" key={account.id}>
                <div className="message__meta">
                  <span className="message__role">{account.connectorKind}</span>
                  <span>{resolveAccountStateLabel(account.state)}</span>
                </div>
                <strong>{account.displayName}</strong>
                <span className="metric-value metric-value--path">{account.id}</span>
                <span className="metric-label">
                  {text.updatedAt(formatDateTime(account.updatedAt, text.unavailable))}
                </span>
              </article>
            ))}

            {!isLoadingList && connectors.length === 0 && accounts.length === 0 ? (
              <p className="timeline__empty">{text.emptyConnectors}</p>
            ) : null}
          </div>
        </section>

        <section className="status-card status-card--normal" data-testid="channels-threads">
          <p className="section-eyebrow">{text.threadEyebrow}</p>
          <h3 className="section-title">{text.threadTitle}</h3>

          <div className="channels-desk__thread-list" style={threadListStyle}>
            {threads.map((thread) => {
              const selected = selectedBindingId === thread.bindingId;
              return (
                <button
                  key={thread.bindingId}
                  type="button"
                  className="secondary-button"
                  data-testid={`channel-thread-select-${thread.bindingId}`}
                  aria-pressed={selected}
                  onClick={() => {
                    void handleSelectThread(thread.bindingId);
                  }}
                  style={threadButtonStyle}
                >
                  <div className="message__meta">
                    <span className="message__role">{thread.connectorKind}</span>
                    <span>{resolveDeliveryModeLabel(thread.deliveryMode)}</span>
                  </div>
                  <strong>{thread.displayTitle}</strong>
                  <div>{trimText(thread.lastMessagePreview)}</div>
                  <div className="metric-label">{resolveThreadState(thread)}</div>
                </button>
              );
            })}

            {!isLoadingList && threads.length === 0 ? (
              <p className="section-copy" data-testid="channels-empty">
                {text.emptyThreads}
              </p>
            ) : null}
          </div>

          <div className="channels-desk__detail-panel" style={detailPanelStyle} data-testid="channel-thread-detail">
            {isLoadingDetail ? (
              <p className="section-copy">{text.loadingDetail}</p>
            ) : threadDetail && selectedThread ? (
              <>
                <div className="metric-item">
                  <span className="metric-label">{text.detail.binding}</span>
                  <span className="metric-value metric-value--path">{threadDetail.binding.id}</span>
                  <span className="metric-label">{text.detail.sessionKind}</span>
                  <span className="metric-value">
                    {resolveSessionKindLabel(threadDetail.binding.sessionKind)}
                  </span>
                  <span className="metric-label">{text.detail.deliveryMode}</span>
                  <span className="metric-value">
                    {resolveDeliveryModeLabel(threadDetail.deliveryRule.mode)}
                  </span>
                </div>
                <div className="metric-item">
                  <span className="metric-label">{text.detail.policy}</span>
                  <span className="metric-value">
                    {text.policySummary(
                      threadDetail.policy.loadUserProfile ? text.on : text.off,
                      threadDetail.policy.loadLongTermMemory ? text.on : text.off,
                    )}
                  </span>
                  <span className="metric-label">{text.detail.replyGuard}</span>
                  <span className="metric-value">
                    {text.replyGuardSummary(
                      threadDetail.policy.allowDirectReply ? text.on : text.off,
                      threadDetail.policy.requireExplicitMention ? text.yes : text.no,
                    )}
                  </span>
                  <span className="metric-label">{text.detail.pending}</span>
                  <span className="metric-value">
                    {threadDetail.pendingApprovalId
                      ? text.detailPendingApproval(threadDetail.pendingApprovalId)
                      : threadDetail.hasPendingDraft
                        ? text.detailPendingDraft
                        : text.detailNoPending}
                  </span>
                </div>

                <div className="metric-item" data-testid="channel-thread-audit">
                  <span className="metric-label">{text.detail.recentAudit}</span>
                  {threadAudit.length > 0 ? (
                    threadAudit.map((entry) => (
                      <span key={entry.id} className="metric-value">
                        {entry.eventType} · {entry.summary ?? text.unavailable} · {formatDateTime(entry.createdAt, text.unavailable)}
                      </span>
                    ))
                  ) : (
                    <span className="metric-value">{text.detail.noAudit}</span>
                  )}
                </div>
              </>
            ) : (
              <p className="section-copy">{text.emptyDetail}</p>
            )}
          </div>
        </section>
      </div>
    </section>
  );
}
