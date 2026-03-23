import { useEffect, useMemo, useRef, useState } from "react";
import {
  fetchAutomationRuns,
  fetchAutomations,
  fetchSessionDetail,
  fetchSettings,
  setAutomationsEnabled,
  triggerAutomation,
  updateAutomationDefinition,
} from "../lib/api";
import { useI18n, useLocaleText } from "../i18n/I18nProvider";
import { Skeleton } from "./ui/Skeleton";
import { EmptyState } from "./ui/EmptyState";
import { Zap } from "lucide-react";
import type {
  AutomationDefinition,
  AutomationDefinitionSource,
  AutomationRunRecord,
  AutomationSchedule,
  SessionDetail,
} from "../types/contracts";

type EnabledFilter = "all" | "enabled";
type SourceFilter = "all" | AutomationDefinitionSource;

const SOURCE_FILTER_OPTIONS: AutomationDefinitionSource[] = ["Manual", "Heartbeat"];



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
      promptDiagnostics: "最近运行提示诊断",
      loadingPromptDiagnostics: "正在加载最近一次运行的提示诊断...",
      emptyPromptDiagnostics: "最近运行没有可用的提示诊断。",
      loadPromptDiagnosticsError: "加载最近运行的提示诊断失败。",
      latestRunSession: "最近运行会话",
      promptProfile: "提示词档案",
      promptSize: "提示词大小",
      promptBudget: "提示词预算",
      promptGeneratedAt: "生成时间",
      loadedContextFiles: "已加载上下文",
      noLoadedContextFiles: "最近运行未记录已加载上下文。",
      truncationState: "截断状态",
      truncationOn: "已截断",
      truncationOff: "未截断",
      truncatedContextFiles: "被截断的文件",
      truncationNotes: "截断说明",
      memoryBoundary: "记忆边界",
      memoryBoundaryDefault:
        "本轮默认不读取长期记忆；只有基线协议文件和显式输入路径会进入自动化提示词。",
      memoryBoundaryWithMemory:
        "本轮提示词显式加载了 MEMORY.md；这属于主动纳入的上下文，而不是默认记忆回忆。",
      promptSizeValue: (count: number) => `${count.toLocaleString()} 字符`,
      promptBudgetValue: (count: number, budget: number, remaining: number | null | undefined) =>
        `${count.toLocaleString()} / ${budget.toLocaleString()}${remaining === null || remaining === undefined ? "" : `（剩余 ${remaining.toLocaleString()}）`}`,
      promptBudgetUnavailable: "未配置字符预算",
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
      triggerNow: "立即执行",
      triggering: "执行中...",
      triggerError: "触发自动化失败。",
      engineDisabledBanner: "自动化引擎当前已关闭。前往设置页启用「启用自动化引擎」后，所有已开启的自动化才会按计划执行。",
      engineEnabled: "已启用",
      engineDisabled: "已禁用",
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
      promptDiagnostics: "Latest run prompt diagnostics",
      loadingPromptDiagnostics: "Loading prompt diagnostics for the latest run...",
      emptyPromptDiagnostics: "No prompt diagnostics are available for the latest run.",
      loadPromptDiagnosticsError: "Failed to load latest-run prompt diagnostics.",
      latestRunSession: "Latest run session",
      promptProfile: "Prompt profile",
      promptSize: "Prompt size",
      promptBudget: "Prompt budget",
      promptGeneratedAt: "Generated at",
      loadedContextFiles: "Loaded context files",
      noLoadedContextFiles: "No loaded context files were recorded for the latest run.",
      truncationState: "Truncation",
      truncationOn: "Truncated",
      truncationOff: "Not truncated",
      truncatedContextFiles: "Truncated files",
      truncationNotes: "Truncation notes",
      memoryBoundary: "Memory boundary",
      memoryBoundaryDefault:
        "Long-term memory stays out by default; only baseline operating files and explicit input paths enter automation prompts.",
      memoryBoundaryWithMemory:
        "This run explicitly loaded MEMORY.md, so long-term memory was included intentionally rather than recalled by default.",
      promptSizeValue: (count: number) => `${count.toLocaleString()} chars`,
      promptBudgetValue: (count: number, budget: number, remaining: number | null | undefined) =>
        `${count.toLocaleString()} / ${budget.toLocaleString()}${remaining === null || remaining === undefined ? "" : ` (${remaining.toLocaleString()} remaining)`}`,
      promptBudgetUnavailable: "No character budget configured",
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
      triggerNow: "Run now",
      triggering: "Running...",
      triggerError: "Failed to trigger automation.",
      engineDisabledBanner: "The automations engine is currently disabled. Go to Settings and enable \"Automations engine enabled\" so scheduled automations can run.",
      engineEnabled: "Enabled",
      engineDisabled: "Disabled",
    },
  });

  const [automations, setAutomations] = useState<AutomationDefinition[]>([]);
  const [selectedAutomationId, setSelectedAutomationId] = useState<string | null>(null);
  const [recentRuns, setRecentRuns] = useState<AutomationRunRecord[]>([]);
  const [latestRunSessionDetail, setLatestRunSessionDetail] = useState<SessionDetail | null>(null);
  const [enabledFilter, setEnabledFilter] = useState<EnabledFilter>("all");
  const [sourceFilter, setSourceFilter] = useState<SourceFilter>("all");
  const [isLoadingList, setIsLoadingList] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [isLoadingRuns, setIsLoadingRuns] = useState(false);
  const [isLoadingPromptDiagnostics, setIsLoadingPromptDiagnostics] = useState(false);
  const [pendingToggleIds, setPendingToggleIds] = useState<Record<string, boolean>>({});
  const [pendingTriggerIds, setPendingTriggerIds] = useState<Record<string, boolean>>({});
  const [error, setError] = useState<string | null>(null);
  const [promptDiagnosticsError, setPromptDiagnosticsError] = useState<string | null>(null);
  const [automationsEngineEnabled, setAutomationsEngineEnabled] = useState<boolean | null>(null);
  const [isTogglingEngine, setIsTogglingEngine] = useState(false);

  const listRequestIdRef = useRef(0);
  const runsRequestIdRef = useRef(0);
  const promptDiagnosticsRequestIdRef = useRef(0);

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

  function formatPromptSize(characterCount: number): string {
    return text.promptSizeValue(characterCount);
  }

  function formatPromptBudget(detail: SessionDetail): string {
    const report = detail.promptReport;
    if (!report) {
      return text.promptBudgetUnavailable;
    }

    if (report.characterBudget === null || report.characterBudget === undefined) {
      return text.promptBudgetUnavailable;
    }

    return text.promptBudgetValue(
      report.characterCount,
      report.characterBudget,
      report.remainingCharacterBudget,
    );
  }

  async function loadPromptDiagnostics(sessionId: string | null) {
    const requestId = ++promptDiagnosticsRequestIdRef.current;

    if (!sessionId) {
      setLatestRunSessionDetail(null);
      setPromptDiagnosticsError(null);
      setIsLoadingPromptDiagnostics(false);
      return;
    }

    setIsLoadingPromptDiagnostics(true);
    setPromptDiagnosticsError(null);

    try {
      const detail = await fetchSessionDetail(sessionId);
      if (promptDiagnosticsRequestIdRef.current !== requestId) {
        return;
      }

      setLatestRunSessionDetail(detail);
    } catch (nextError) {
      if (promptDiagnosticsRequestIdRef.current !== requestId) {
        return;
      }

      setLatestRunSessionDetail(null);
      setPromptDiagnosticsError(
        nextError instanceof Error ? nextError.message : text.loadPromptDiagnosticsError,
      );
    } finally {
      if (promptDiagnosticsRequestIdRef.current === requestId) {
        setIsLoadingPromptDiagnostics(false);
      }
    }
  }

  async function loadAutomationRuns(automationId: string | null) {
    const requestId = ++runsRequestIdRef.current;

    if (!automationId) {
      setRecentRuns([]);
      setLatestRunSessionDetail(null);
      setPromptDiagnosticsError(null);
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
      const latestRunSessionId = payload.items.find((run) => run.sessionId?.trim())?.sessionId ?? null;
      await loadPromptDiagnostics(latestRunSessionId);
    } catch (nextError) {
      if (runsRequestIdRef.current !== requestId) {
        return;
      }

      setRecentRuns([]);
      setLatestRunSessionDetail(null);
      setPromptDiagnosticsError(null);
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
    fetchSettings()
      .then((s) => setAutomationsEngineEnabled(s.automationsEnabled))
      .catch(() => setAutomationsEngineEnabled(null));
  }, []);

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

  async function handleTriggerAutomation(automationId: string) {
    setPendingTriggerIds((current) => ({ ...current, [automationId]: true }));
    setError(null);

    try {
      await triggerAutomation(automationId);
      // Refresh runs list after a short delay to allow the scheduler to pick up the queued run.
      setTimeout(() => {
        void loadAutomationRuns(automationId);
      }, 1500);
    } catch (nextError) {
      setError(nextError instanceof Error ? nextError.message : text.triggerError);
    } finally {
      setPendingTriggerIds((current) => {
        const next = { ...current };
        delete next[automationId];
        return next;
      });
    }
  }

  const latestPromptReport = latestRunSessionDetail?.promptReport ?? null;
  const latestPromptHasMemory = latestPromptReport?.loadedContextFiles.some((path) =>
    path.endsWith("/MEMORY.md") || path === "workspace/MEMORY.md",
  ) ?? false;

  async function handleToggleEngine() {
    if (automationsEngineEnabled === null || isTogglingEngine) return;
    setIsTogglingEngine(true);
    try {
      const next = !automationsEngineEnabled;
      await setAutomationsEnabled(next);
      setAutomationsEngineEnabled(next);
    } catch {
      // Keep current state on failure
    } finally {
      setIsTogglingEngine(false);
    }
  }

  return (
    <section className="" data-testid="automations-desk">
      <h2 className="desk-section-title">{text.title}</h2>
      <p className="desk-section-desc">{text.copy}</p>

      {automationsEngineEnabled === false && (
        <div
          className="automations-engine-banner automations-engine-banner--warning"
          data-testid="automations-engine-banner"
          role="alert"
        >
          {text.engineDisabledBanner}
        </div>
      )}

      <div className="automations-desk__toolbar">
        <button
          type="button"
          className="btn btn--secondary"
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
          className="kc-select control-plane-filter"
          value={enabledFilter}
          onChange={(event) => setEnabledFilter(event.target.value as EnabledFilter)}
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
          className="kc-select control-plane-filter"
          value={sourceFilter}
          onChange={(event) => setSourceFilter(event.target.value as SourceFilter)}
        >
          <option value="all">{text.sourceFilterAll}</option>
          {SOURCE_FILTER_OPTIONS.map((source) => (
            <option value={source} key={source}>
              {resolveSourceLabel(source)}
            </option>
          ))}
        </select>

        {automationsEngineEnabled !== null && (
          <label className="automations-engine-toggle" data-testid="automations-engine-toggle">
            <input
              type="checkbox"
              checked={automationsEngineEnabled}
              disabled={isTogglingEngine}
              onChange={() => { void handleToggleEngine(); }}
              aria-label={automationsEngineEnabled ? text.engineEnabled : text.engineDisabled}
            />
            <span className="automations-engine-toggle__label">
              {automationsEngineEnabled ? text.engineEnabled : text.engineDisabled}
            </span>
          </label>
        )}
      </div>

      {error ? (
        <p
          className="__feedback __feedback--error"
          data-testid="automations-error"
        >
          {error}
        </p>
      ) : null}

      <div className="automations-desk__layout">
        <section className="timeline" data-testid="automations-list">
          <div className="timeline__header">
            <h3 className="desk-section-title">{text.indexTitle}</h3>
            <span className="composer__status">
              {isLoadingList ? text.loadingList : text.loaded(automations.length)}
            </span>
          </div>

          <div className="timeline__body automations-desk__list-body">
            {isLoadingList ? <Skeleton height={52} count={3} /> : null}
            {!isLoadingList && automations.length === 0 ? (
              <EmptyState icon={<Zap size={28} strokeWidth={1.5} />} title={text.emptyList} />
            ) : null}

            {automations.map((automation) => {
              const isSelected = automation.id === selectedAutomationId;

              return (
                <article
                  key={automation.id}
                  className={`${automation.enabled ? "message message--assistant" : "message message--system"}${isSelected ? " automation-item--selected" : ""}`}
                  data-testid={`automation-item-${automation.id}`}
                >
                  <div className="message__meta">
                    <span className="message__role">{resolveSourceLabel(automation.source)}</span>
                    <span>
                      {automation.enabled ? text.states.enabled : text.states.disabled}
                    </span>
                  </div>
                  <strong>{automation.title}</strong>
                  <span>{formatSchedule(automation.schedule)}</span>
                  <div className="metric-item metric-item--compact">
                    <span className="metric-label">{text.nextRun}</span>
                    <span className="metric-value">
                      {formatDateTime(automation.nextRunAt, text.unavailable)}
                    </span>
                    <span className="metric-label">{text.lastRunStatus}</span>
                    <span className="metric-value">
                      {resolveRunStatusLabel(automation.lastRunStatus)}
                    </span>
                  </div>
                  <div className="automation-item-actions">
                    <button
                      type="button"
                      className="btn btn--secondary"
                      data-testid={`automation-select-${automation.id}`}
                      aria-pressed={isSelected}
                      onClick={() => handleSelectAutomation(automation.id)}
                      disabled={isLoadingList || isRefreshing}
                    >
                      {isSelected ? text.inspecting : text.inspectDetail}
                    </button>
                    {automation.enabled ? (
                      <button
                        type="button"
                        className="btn btn--secondary"
                        data-testid={`automation-trigger-${automation.id}`}
                        onClick={() => {
                          void handleTriggerAutomation(automation.id);
                        }}
                        disabled={pendingTriggerIds[automation.id] ?? false}
                      >
                        {pendingTriggerIds[automation.id] ? text.triggering : text.triggerNow}
                      </button>
                    ) : null}
                  </div>
                </article>
              );
            })}
          </div>
        </section>

        <section className="status-card status-card--normal" data-testid="automation-detail">
          {selectedAutomation ? (
            <>
              <h3 className="desk-section-title">{selectedAutomation.title}</h3>
              <p className="desk-section-desc">
                {text.detailSubtitle(
                  formatSchedule(selectedAutomation.schedule),
                  resolveSourceLabel(selectedAutomation.source),
                )}
              </p>

              <div className="automations-desk__detail-toolbar">
                <span
                  className={selectedAutomation.enabled ? "mode-badge mode-badge--main" : "mode-badge"}
                  data-testid="automation-enabled-chip"
                >
                  {selectedAutomation.enabled ? text.states.enabled : text.states.disabled}
                </span>
                <button
                  type="button"
                  className="btn btn--secondary"
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

              <div className="metric-item automation-detail-metric" data-testid="automation-detail-prompt">
                <span className="metric-label">{text.promptSummary}</span>
                <span className="metric-value">{summarizePrompt(selectedAutomation.prompt)}</span>
              </div>

              <div
                className="metric-item automation-detail-metric"
                data-testid="automation-detail-input-paths"
              >
                <span className="metric-label">{text.inputPaths}</span>
                {selectedAutomation.inputPaths?.length ? (
                  <ul className="automation-bullets-list">
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
                className="metric-item automation-detail-error"
                data-testid="automation-detail-last-error"
              >
                <span className="metric-label">{text.lastError}</span>
                <span className="metric-value">
                  {displayedLastError ?? text.noRecentError}
                </span>
              </div>

              <div data-testid="automation-runs">
                <p className="metric-label">{text.recentRuns}</p>
                {isLoadingRuns ? <p className="desk-section-desc">{text.loadingRuns}</p> : null}
                {!isLoadingRuns && recentRuns.length === 0 ? (
                  <p className="desk-section-desc">{text.emptyRuns}</p>
                ) : null}
                {!isLoadingRuns && recentRuns.length > 0 ? (
                  <ul className="automation-runs-list">
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

              <div
                className="metric-item automation-detail-metric"
                data-testid="automation-prompt-diagnostics"
              >
                <span className="metric-label">{text.promptDiagnostics}</span>
                {isLoadingPromptDiagnostics ? (
                  <p className="desk-section-desc">{text.loadingPromptDiagnostics}</p>
                ) : promptDiagnosticsError ? (
                  <span className="metric-value">{promptDiagnosticsError}</span>
                ) : latestPromptReport ? (
                  <>
                    <span className="metric-label">{text.latestRunSession}</span>
                    <span className="metric-value metric-value--path" data-testid="automation-prompt-session-id">
                      {latestRunSessionDetail?.sessionId ?? text.unavailable}
                    </span>
                    <span className="metric-label">{text.promptProfile}</span>
                    <span className="metric-value">{latestPromptReport.profileId}</span>
                    <span className="metric-label">{text.promptSize}</span>
                    <span className="metric-value">{formatPromptSize(latestPromptReport.characterCount)}</span>
                    <span className="metric-label">{text.promptBudget}</span>
                    <span className="metric-value">{formatPromptBudget(latestRunSessionDetail!)}</span>
                    <span className="metric-label">{text.promptGeneratedAt}</span>
                    <span className="metric-value">
                      {formatDateTime(latestPromptReport.generatedAt, text.unavailable)}
                    </span>
                    <span className="metric-label">{text.memoryBoundary}</span>
                    <span className="metric-value">
                      {latestPromptHasMemory
                        ? text.memoryBoundaryWithMemory
                        : text.memoryBoundaryDefault}
                    </span>
                    <span className="metric-label">{text.truncationState}</span>
                    <span className="metric-value">
                      {latestPromptReport.wasTruncated ? text.truncationOn : text.truncationOff}
                    </span>
                    <span className="metric-label">{text.loadedContextFiles}</span>
                    {latestPromptReport.loadedContextFiles.length > 0 ? (
                      <ul className="automation-bullets-list">
                        {latestPromptReport.loadedContextFiles.map((path) => (
                          <li key={path} className="metric-value metric-value--path">
                            {path}
                          </li>
                        ))}
                      </ul>
                    ) : (
                      <span className="metric-value">{text.noLoadedContextFiles}</span>
                    )}
                    {latestPromptReport.wasTruncated ? (
                      <>
                        <span className="metric-label">{text.truncatedContextFiles}</span>
                        {(latestPromptReport.truncatedContextFiles?.length ?? 0) > 0 ? (
                          <ul className="automation-bullets-list">
                            {latestPromptReport.truncatedContextFiles!.map((path) => (
                              <li key={path} className="metric-value metric-value--path">
                                {path}
                              </li>
                            ))}
                          </ul>
                        ) : (
                          <span className="metric-value">{text.emptyPromptDiagnostics}</span>
                        )}
                        <span className="metric-label">{text.truncationNotes}</span>
                        {(latestPromptReport.truncationNotes?.length ?? 0) > 0 ? (
                          <ul className="automation-bullets-list">
                            {latestPromptReport.truncationNotes!.map((note) => (
                              <li key={note} className="metric-value">
                                {note}
                              </li>
                            ))}
                          </ul>
                        ) : (
                          <span className="metric-value">{text.emptyPromptDiagnostics}</span>
                        )}
                      </>
                    ) : null}
                  </>
                ) : (
                  <span className="metric-value">{text.emptyPromptDiagnostics}</span>
                )}
              </div>
            </>
          ) : (
            <EmptyState icon={<Zap size={28} strokeWidth={1.5} />} title={text.emptyDetail} />
          )}
        </section>
      </div>
    </section>
  );
}
