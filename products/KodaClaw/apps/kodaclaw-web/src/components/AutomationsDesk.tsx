import { type CSSProperties, useEffect, useMemo, useRef, useState } from "react";
import {
  fetchAutomationRuns,
  fetchAutomations,
  updateAutomationDefinition,
} from "../lib/api";
import { useI18n, useLocaleText } from "../i18n/I18nProvider";
import type {
  AutomationDefinition,
  AutomationDefinitionSource,
  AutomationRunRecord,
  AutomationSchedule,
} from "../types/contracts";

type EnabledFilter = "all" | "enabled";
type SourceFilter = "all" | AutomationDefinitionSource;

const SOURCE_FILTER_OPTIONS: AutomationDefinitionSource[] = ["Manual", "Heartbeat"];

const toolbarStyle: CSSProperties = {
  marginTop: 12,
  display: "flex",
  flexWrap: "wrap",
  alignItems: "center",
  gap: 10,
};

const selectStyle: CSSProperties = {
  minWidth: 160,
};

const splitLayoutStyle: CSSProperties = {
  display: "grid",
  gap: 18,
  gridTemplateColumns: "minmax(0, 1.1fr) minmax(0, 1fr)",
  marginTop: 16,
};

const listBodyStyle: CSSProperties = {
  maxHeight: "min(52vh, 680px)",
};

const selectedAutomationStyle: CSSProperties = {
  borderColor: "rgba(47, 90, 72, 0.4)",
  boxShadow: "0 12px 24px rgba(41, 26, 12, 0.12)",
};

const compactMetricStyle: CSSProperties = {
  gap: 2,
};

const detailToolbarStyle: CSSProperties = {
  display: "flex",
  alignItems: "center",
  gap: 10,
  flexWrap: "wrap",
  marginBottom: 12,
};

const detailMetricStyle: CSSProperties = {
  marginBottom: 10,
};

const detailErrorStyle: CSSProperties = {
  marginBottom: 14,
};

const listWithBulletsStyle: CSSProperties = {
  margin: 0,
  paddingLeft: 18,
  display: "grid",
  gap: 4,
};

const runsListStyle: CSSProperties = {
  listStyle: "none",
  margin: 0,
  padding: 0,
  display: "grid",
  gap: 10,
};

function summarizePrompt(prompt: string, maxLength = 180): string {
  const normalized = prompt.replace(/\s+/g, " ").trim();
  if (normalized.length <= maxLength) {
    return normalized;
  }

  return `${normalized.slice(0, maxLength - 3)}...`;
}

export function AutomationsDesk() {
  const { formatDateTime } = useI18n();
  const text = useLocaleText({
    zh: {
      eyebrow: "控制平面",
      title: "自动化编务台",
      copy:
        "统筹周期任务、核对即将到来的调度，并在切换开关前把最近一次运行叙事保留在视野里。",
      refresh: "刷新工作台",
      refreshing: "刷新中...",
      filters: {
        scope: "范围",
        source: "来源",
      },
      enabledFilter: {
        all: "全部自动化",
        enabled: "仅看已启用",
      },
      sourceFilterAll: "全部来源",
      indexTitle: "自动化索引",
      loaded: (count: number) => `已加载 ${count} 条`,
      loadingList: "正在加载自动化...",
      emptyList: "当前筛选条件下暂无自动化。",
      detailEyebrow: "自动化详情",
      detailSubtitle: (schedule: string, source: string) => `${schedule} · 来源 ${source}`,
      inspectDetail: "查看详情",
      inspecting: "查看中",
      states: {
        enabled: "已启用",
        disabled: "已停用",
      },
      nextRun: "下次运行",
      lastRunStatus: "上次运行状态",
      toggleEnable: "启用自动化",
      toggleDisable: "停用自动化",
      saving: "保存中...",
      nextRunHint: (value: string) => `下次运行 ${value}`,
      promptSummary: "提示词摘要",
      inputPaths: "输入路径",
      noInputPaths: "未配置显式输入路径。",
      lastError: "最近错误",
      noRecentError: "最近没有自动化错误。",
      recentRuns: "最近运行",
      loadingRuns: "正在加载最近运行...",
      emptyRuns: "这个自动化还没有最近运行记录。",
      emptyDetail: "选择一个自动化以查看提示词摘要、运行记录与开关状态。",
      sourceLabels: {
        Manual: "手动",
        Heartbeat: "心跳",
      },
      runStatus: {
        Succeeded: "成功",
        Failed: "失败",
        Running: "运行中",
        Queued: "排队中",
        Pending: "待处理",
        Canceled: "已取消",
      },
      triggerLabels: {
        schedule: "计划触发",
        manual: "手动触发",
        heartbeat: "心跳触发",
      },
      schedule: {
        hourly: "每小时",
        everyHours: (interval: number) => `每 ${interval} 小时`,
        daily: "每天",
        dailyAt: (localTime: string) => `每天 ${localTime}`,
        weekly: (days: string, localTime?: string | null) =>
          `每周 ${days}${localTime ? ` ${localTime}` : ""}`,
        selectedDays: "指定日期",
      },
      dayNames: {
        Monday: "周一",
        Tuesday: "周二",
        Wednesday: "周三",
        Thursday: "周四",
        Friday: "周五",
        Saturday: "周六",
        Sunday: "周日",
      },
      runAttempt: (attempt: number) => `第 ${attempt} 次尝试`,
      unavailable: "暂无",
      loadRunsError: "加载自动化运行记录失败。",
      loadAutomationsError: "加载自动化列表失败。",
      updateAutomationError: "更新自动化状态失败。",
    },
    en: {
      eyebrow: "Control Plane",
      title: "Automations Editorial Desk",
      copy:
        "Curate recurring jobs, inspect upcoming schedules, and keep the last run narrative visible before you flip a switch.",
      refresh: "Refresh desk",
      refreshing: "Refreshing...",
      filters: {
        scope: "Scope",
        source: "Source",
      },
      enabledFilter: {
        all: "All automations",
        enabled: "Enabled only",
      },
      sourceFilterAll: "All sources",
      indexTitle: "Automation Index",
      loaded: (count: number) => `${count} loaded`,
      loadingList: "Loading automations...",
      emptyList: "No automations match the current filters.",
      detailEyebrow: "Automation Detail",
      detailSubtitle: (schedule: string, source: string) => `${schedule} · source ${source}`,
      inspectDetail: "Inspect detail",
      inspecting: "Inspecting",
      states: {
        enabled: "Enabled",
        disabled: "Disabled",
      },
      nextRun: "Next run",
      lastRunStatus: "Last run status",
      toggleEnable: "Enable automation",
      toggleDisable: "Disable automation",
      saving: "Saving...",
      nextRunHint: (value: string) => `Next run ${value}`,
      promptSummary: "Prompt summary",
      inputPaths: "Input paths",
      noInputPaths: "No explicit input paths configured.",
      lastError: "Last error",
      noRecentError: "No recent automation error.",
      recentRuns: "Recent runs",
      loadingRuns: "Loading recent runs...",
      emptyRuns: "No recent runs found for this automation.",
      emptyDetail: "Select an automation to inspect prompt summary, runs, and toggle state.",
      sourceLabels: {
        Manual: "Manual",
        Heartbeat: "Heartbeat",
      },
      runStatus: {
        Succeeded: "Succeeded",
        Failed: "Failed",
        Running: "Running",
        Queued: "Queued",
        Pending: "Pending",
        Canceled: "Canceled",
      },
      triggerLabels: {
        schedule: "Scheduled",
        manual: "Manual trigger",
        heartbeat: "Heartbeat",
      },
      schedule: {
        hourly: "Every hour",
        everyHours: (interval: number) => `Every ${interval} hours`,
        daily: "Daily",
        dailyAt: (localTime: string) => `Daily at ${localTime}`,
        weekly: (days: string, localTime?: string | null) =>
          `Weekly on ${days}${localTime ? ` at ${localTime}` : ""}`,
        selectedDays: "selected days",
      },
      dayNames: {
        Monday: "Monday",
        Tuesday: "Tuesday",
        Wednesday: "Wednesday",
        Thursday: "Thursday",
        Friday: "Friday",
        Saturday: "Saturday",
        Sunday: "Sunday",
      },
      runAttempt: (attempt: number) => `attempt ${attempt}`,
      unavailable: "n/a",
      loadRunsError: "Failed to load automation runs.",
      loadAutomationsError: "Failed to load automations.",
      updateAutomationError: "Failed to update automation state.",
    },
  });

  const [automations, setAutomations] = useState<AutomationDefinition[]>([]);
  const [selectedAutomationId, setSelectedAutomationId] = useState<string | null>(null);
  const [recentRuns, setRecentRuns] = useState<AutomationRunRecord[]>([]);
  const [enabledFilter, setEnabledFilter] = useState<EnabledFilter>("all");
  const [sourceFilter, setSourceFilter] = useState<SourceFilter>("all");
  const [isLoadingList, setIsLoadingList] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [isLoadingRuns, setIsLoadingRuns] = useState(false);
  const [pendingToggleIds, setPendingToggleIds] = useState<Record<string, boolean>>({});
  const [error, setError] = useState<string | null>(null);

  const listRequestIdRef = useRef(0);
  const runsRequestIdRef = useRef(0);

  const selectedAutomation = useMemo(
    () => automations.find((item) => item.id === selectedAutomationId) ?? null,
    [automations, selectedAutomationId],
  );

  const displayedLastError = useMemo(() => {
    if (!selectedAutomation) {
      return null;
    }

    if (selectedAutomation.lastError?.trim()) {
      return selectedAutomation.lastError;
    }

    const failedRun = recentRuns.find((run) => run.errorMessage?.trim());
    return failedRun?.errorMessage ?? null;
  }, [recentRuns, selectedAutomation]);

  function resolveSourceLabel(source: AutomationDefinitionSource): string {
    return text.sourceLabels[source] ?? source;
  }

  function resolveRunStatusLabel(status?: string | null): string {
    if (!status) {
      return text.unavailable;
    }

    return text.runStatus[status as keyof typeof text.runStatus] ?? status;
  }

  function resolveTriggerLabel(trigger?: string | null): string {
    if (!trigger) {
      return text.unavailable;
    }

    return text.triggerLabels[trigger as keyof typeof text.triggerLabels] ?? trigger;
  }

  function resolveDayLabel(day: string): string {
    return text.dayNames[day as keyof typeof text.dayNames] ?? day;
  }

  function formatSchedule(schedule: AutomationSchedule): string {
    if (schedule.kind === "Hourly") {
      const interval = schedule.interval ?? 1;
      return interval === 1 ? text.schedule.hourly : text.schedule.everyHours(interval);
    }

    if (schedule.kind === "Daily") {
      return schedule.localTime ? text.schedule.dailyAt(schedule.localTime) : text.schedule.daily;
    }

    const days = schedule.daysOfWeek?.map(resolveDayLabel).join(", ") ?? text.schedule.selectedDays;
    return text.schedule.weekly(days, schedule.localTime);
  }

  function formatRunWindow(run: AutomationRunRecord): string {
    const started = formatDateTime(run.startedAt, text.unavailable);
    const completed = formatDateTime(run.completedAt, text.unavailable);
    return `${started} -> ${completed}`;
  }

  async function loadAutomationRuns(automationId: string | null) {
    const requestId = ++runsRequestIdRef.current;

    if (!automationId) {
      setRecentRuns([]);
      setIsLoadingRuns(false);
      return;
    }

    setIsLoadingRuns(true);

    try {
      const payload = await fetchAutomationRuns(automationId, { limit: 12 });
      if (runsRequestIdRef.current !== requestId) {
        return;
      }

      setRecentRuns(payload.items);
    } catch (nextError) {
      if (runsRequestIdRef.current !== requestId) {
        return;
      }

      setRecentRuns([]);
      setError(nextError instanceof Error ? nextError.message : text.loadRunsError);
    } finally {
      if (runsRequestIdRef.current === requestId) {
        setIsLoadingRuns(false);
      }
    }
  }

  async function loadAutomations(mode: "initial" | "refresh") {
    const requestId = ++listRequestIdRef.current;

    if (mode === "initial") {
      setIsLoadingList(true);
    } else {
      setIsRefreshing(true);
    }

    setError(null);

    try {
      const payload = await fetchAutomations({
        limit: 60,
        enabled: enabledFilter === "enabled" ? true : undefined,
        source: sourceFilter === "all" ? undefined : sourceFilter,
      });

      if (listRequestIdRef.current !== requestId) {
        return;
      }

      setAutomations(payload.items);
      const hasCurrent = payload.items.some((item) => item.id === selectedAutomationId);
      const nextSelectedAutomationId = hasCurrent
        ? selectedAutomationId
        : (payload.items[0]?.id ?? null);
      setSelectedAutomationId(nextSelectedAutomationId);
      await loadAutomationRuns(nextSelectedAutomationId);
    } catch (nextError) {
      if (listRequestIdRef.current !== requestId) {
        return;
      }

      setAutomations([]);
      setRecentRuns([]);
      setSelectedAutomationId(null);
      setError(nextError instanceof Error ? nextError.message : text.loadAutomationsError);
    } finally {
      if (listRequestIdRef.current === requestId) {
        if (mode === "initial") {
          setIsLoadingList(false);
        } else {
          setIsRefreshing(false);
        }
      }
    }
  }

  useEffect(() => {
    void loadAutomations("initial");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabledFilter, sourceFilter]);

  async function handleRefresh() {
    await loadAutomations("refresh");
  }

  function handleSelectAutomation(automationId: string) {
    setSelectedAutomationId(automationId);
    setError(null);
    void loadAutomationRuns(automationId);
  }

  async function handleToggleAutomation(automation: AutomationDefinition) {
    setPendingToggleIds((current) => ({ ...current, [automation.id]: true }));
    setError(null);

    try {
      await updateAutomationDefinition(automation.id, !automation.enabled);
      await loadAutomations("refresh");
    } catch (nextError) {
      setError(nextError instanceof Error ? nextError.message : text.updateAutomationError);
    } finally {
      setPendingToggleIds((current) => {
        const next = { ...current };
        delete next[automation.id];
        return next;
      });
    }
  }

  return (
    <section className="bootstrap-panel" data-testid="automations-desk">
      <div className="section-eyebrow">{text.eyebrow}</div>
      <h2 className="section-title">{text.title}</h2>
      <p className="section-copy">{text.copy}</p>

      <div className="automations-desk__toolbar" style={toolbarStyle}>
        <button
          type="button"
          className="secondary-button"
          data-testid="automations-refresh"
          disabled={isLoadingList || isRefreshing}
          onClick={() => {
            void handleRefresh();
          }}
        >
          {isRefreshing ? text.refreshing : text.refresh}
        </button>

        <label className="metric-label" htmlFor="automations-enabled-filter">
          {text.filters.scope}
        </label>
        <select
          id="automations-enabled-filter"
          data-testid="automations-enabled-filter"
          className="bootstrap-form__textarea"
          value={enabledFilter}
          onChange={(event) => setEnabledFilter(event.target.value as EnabledFilter)}
          style={selectStyle}
        >
          <option value="all">{text.enabledFilter.all}</option>
          <option value="enabled">{text.enabledFilter.enabled}</option>
        </select>

        <label className="metric-label" htmlFor="automations-source-filter">
          {text.filters.source}
        </label>
        <select
          id="automations-source-filter"
          data-testid="automations-source-filter"
          className="bootstrap-form__textarea"
          value={sourceFilter}
          onChange={(event) => setSourceFilter(event.target.value as SourceFilter)}
          style={selectStyle}
        >
          <option value="all">{text.sourceFilterAll}</option>
          {SOURCE_FILTER_OPTIONS.map((source) => (
            <option value={source} key={source}>
              {resolveSourceLabel(source)}
            </option>
          ))}
        </select>
      </div>

      {error ? (
        <p
          className="bootstrap-panel__feedback bootstrap-panel__feedback--error"
          data-testid="automations-error"
        >
          {error}
        </p>
      ) : null}

      <div className="automations-desk__layout" style={splitLayoutStyle}>
        <section className="timeline" data-testid="automations-list">
          <div className="timeline__header">
            <h3 className="section-title">{text.indexTitle}</h3>
            <span className="composer__status">
              {isLoadingList ? text.loadingList : text.loaded(automations.length)}
            </span>
          </div>

          <div className="timeline__body automations-desk__list-body" style={listBodyStyle}>
            {isLoadingList ? <p className="timeline__empty">{text.loadingList}</p> : null}
            {!isLoadingList && automations.length === 0 ? (
              <p className="timeline__empty">{text.emptyList}</p>
            ) : null}

            {automations.map((automation) => {
              const isSelected = automation.id === selectedAutomationId;

              return (
                <article
                  key={automation.id}
                  className={
                    automation.enabled ? "message message--assistant" : "message message--system"
                  }
                  data-testid={`automation-item-${automation.id}`}
                  style={isSelected ? selectedAutomationStyle : undefined}
                >
                  <div className="message__meta">
                    <span className="message__role">{resolveSourceLabel(automation.source)}</span>
                    <span>
                      {automation.enabled ? text.states.enabled : text.states.disabled}
                    </span>
                  </div>
                  <strong>{automation.title}</strong>
                  <span>{formatSchedule(automation.schedule)}</span>
                  <div className="metric-item" style={compactMetricStyle}>
                    <span className="metric-label">{text.nextRun}</span>
                    <span className="metric-value">
                      {formatDateTime(automation.nextRunAt, text.unavailable)}
                    </span>
                    <span className="metric-label">{text.lastRunStatus}</span>
                    <span className="metric-value">
                      {resolveRunStatusLabel(automation.lastRunStatus)}
                    </span>
                  </div>
                  <button
                    type="button"
                    className="secondary-button"
                    data-testid={`automation-select-${automation.id}`}
                    aria-pressed={isSelected}
                    onClick={() => handleSelectAutomation(automation.id)}
                    disabled={isLoadingList || isRefreshing}
                  >
                    {isSelected ? text.inspecting : text.inspectDetail}
                  </button>
                </article>
              );
            })}
          </div>
        </section>

        <section className="status-card status-card--normal" data-testid="automation-detail">
          <p className="section-eyebrow">{text.detailEyebrow}</p>
          {selectedAutomation ? (
            <>
              <h3 className="section-title">{selectedAutomation.title}</h3>
              <p className="section-copy">
                {text.detailSubtitle(
                  formatSchedule(selectedAutomation.schedule),
                  resolveSourceLabel(selectedAutomation.source),
                )}
              </p>

              <div className="automations-desk__detail-toolbar" style={detailToolbarStyle}>
                <span
                  className={selectedAutomation.enabled ? "mode-badge mode-badge--main" : "mode-badge"}
                  data-testid="automation-enabled-chip"
                >
                  {selectedAutomation.enabled ? text.states.enabled : text.states.disabled}
                </span>
                <button
                  type="button"
                  className="secondary-button"
                  data-testid={`automation-toggle-${selectedAutomation.id}`}
                  onClick={() => {
                    void handleToggleAutomation(selectedAutomation);
                  }}
                  disabled={pendingToggleIds[selectedAutomation.id] ?? false}
                >
                  {pendingToggleIds[selectedAutomation.id]
                    ? text.saving
                    : selectedAutomation.enabled
                      ? text.toggleDisable
                      : text.toggleEnable}
                </button>
                <span className="metric-label">
                  {text.nextRunHint(formatDateTime(selectedAutomation.nextRunAt, text.unavailable))}
                </span>
              </div>

              <div className="metric-item" data-testid="automation-detail-prompt" style={detailMetricStyle}>
                <span className="metric-label">{text.promptSummary}</span>
                <span className="metric-value">{summarizePrompt(selectedAutomation.prompt)}</span>
              </div>

              <div
                className="metric-item"
                data-testid="automation-detail-input-paths"
                style={detailMetricStyle}
              >
                <span className="metric-label">{text.inputPaths}</span>
                {selectedAutomation.inputPaths?.length ? (
                  <ul style={listWithBulletsStyle}>
                    {selectedAutomation.inputPaths.map((path) => (
                      <li key={path} className="metric-value metric-value--path">
                        {path}
                      </li>
                    ))}
                  </ul>
                ) : (
                  <span className="metric-value">{text.noInputPaths}</span>
                )}
              </div>

              <div
                className="metric-item"
                data-testid="automation-detail-last-error"
                style={detailErrorStyle}
              >
                <span className="metric-label">{text.lastError}</span>
                <span className="metric-value">
                  {displayedLastError ?? text.noRecentError}
                </span>
              </div>

              <div data-testid="automation-runs">
                <p className="metric-label">{text.recentRuns}</p>
                {isLoadingRuns ? <p className="section-copy">{text.loadingRuns}</p> : null}
                {!isLoadingRuns && recentRuns.length === 0 ? (
                  <p className="section-copy">{text.emptyRuns}</p>
                ) : null}
                {!isLoadingRuns && recentRuns.length > 0 ? (
                  <ul style={runsListStyle}>
                    {recentRuns.map((run) => (
                      <li key={run.runId} className="metric-item" data-testid={`automation-run-${run.runId}`}>
                        <span className="metric-label">
                          {resolveRunStatusLabel(run.status)} · {text.runAttempt(run.attempt)}
                        </span>
                        <span className="metric-value">
                          {run.summary ?? resolveTriggerLabel(run.trigger)}
                        </span>
                        <span className="metric-label">{formatRunWindow(run)}</span>
                      </li>
                    ))}
                  </ul>
                ) : null}
              </div>
            </>
          ) : (
            <p className="section-copy">{text.emptyDetail}</p>
          )}
        </section>
      </div>
    </section>
  );
}
