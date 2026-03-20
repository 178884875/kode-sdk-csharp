import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AutomationsDesk } from "./components/AutomationsDesk";
import { BootstrapPanel } from "./components/BootstrapPanel";
import { CanvasDesk } from "./components/CanvasDesk";
import { ChannelsDesk } from "./components/ChannelsDesk";
import { ChatComposer } from "./components/ChatComposer";
import { InboxApprovalDesk } from "./components/InboxApprovalDesk";
import { MessageTimeline } from "./components/MessageTimeline";
import { ModelsSettingsDesk } from "./components/ModelsSettingsDesk";
import { PluginsDesk } from "./components/PluginsDesk";
import { SessionsDiagnosticsDesk } from "./components/SessionsDiagnosticsDesk";
import { SystemStatusCard } from "./components/SystemStatusCard";
import { useLocaleText } from "./i18n/I18nProvider";
import { useChatConsole } from "./hooks/useChatConsole";
import { useGatewaySnapshot } from "./hooks/useGatewaySnapshot";
import {
  getGatewayUrl,
  getInitialLaunchTarget,
  subscribeDesktopLaunchTargets,
  type DesktopDeskId,
  type DesktopLaunchTarget,
} from "./lib/config";
import { completeBootstrap, generateBootstrapDraft } from "./lib/api";
import { LegacyShell } from "./shell-v1/LegacyShell";
import { V2Shell } from "./shell-v2/V2Shell";
import { persistShellVariant, readStoredShellVariant } from "./shell-shared/shell-variant";
import { DeskMeta, MainDesk, StatusModel } from "./shell-shared/types";
import type { ChatMessage } from "./types/chat";
import type { BootstrapDraftMessage } from "./types/contracts";

const MAIN_DESK_STORAGE_KEY = "kodaclaw.mainDesk";

function buildBootstrapConversation(messages: ChatMessage[]): BootstrapDraftMessage[] {
  return messages
    .filter((message): message is ChatMessage & { role: "user" | "assistant" } =>
      (message.role === "user" || message.role === "assistant") &&
      message.status !== "streaming" &&
      message.text.trim().length > 0)
    .map((message) => ({
      role: message.role,
      text: message.text.trim(),
    }));
}

function resolveHealthTone(healthStatus: string): "healthy" | "warning" | "error" | "unknown" {
  const normalized = healthStatus.trim().toLowerCase();
  if (normalized === "healthy" || normalized === "ok") {
    return "healthy";
  }

  if (normalized === "degraded" || normalized === "warning") {
    return "warning";
  }

  if (normalized === "unhealthy" || normalized === "error" || normalized === "failed") {
    return "error";
  }

  return "unknown";
}

function isMainDesk(value: DesktopDeskId | string | null | undefined): value is MainDesk {
  return value === "chat" ||
    value === "inbox" ||
    value === "sessions" ||
    value === "models" ||
    value === "automations" ||
    value === "channels" ||
    value === "plugins" ||
    value === "canvas";
}

function resolveMainDeskFromLaunchTarget(target: DesktopLaunchTarget | null): MainDesk | null {
  return target && isMainDesk(target.desk) ? target.desk : null;
}

function readStoredMainDesk(): MainDesk {
  if (typeof window === "undefined") {
    return "chat";
  }

  const stored = window.localStorage.getItem(MAIN_DESK_STORAGE_KEY);
  return stored === "inbox" ||
      stored === "sessions" ||
      stored === "models" ||
      stored === "automations" ||
      stored === "channels" ||
      stored === "plugins" ||
      stored === "canvas" ||
      stored === "chat"
    ? stored
    : "chat";
}

export default function App() {
  const bootstrapTemplates = useLocaleText({
    zh: {
      identityMarkdown: `# Koda 身份设定

- 名称：Koda
- 角色：本地优先的 AI 协作搭档
- 默认语气：冷静、务实、直接
- 工作方式：可审视、可回退、必要时先审批后执行
`,
      soulMarkdown: `# Koda 灵魂准则

- 第一原则：优先保护用户信任与本地数据边界
- 决策风格：先澄清，再行动；能回退的优先可回退
- 行为约束：涉及外部影响或高风险动作时，默认先走审批
`,
      userMarkdown: `# 用户画像

- 偏好的工作节奏：
- 沟通风格：
- 边界与禁区：
- 成功信号：
`,
    },
    en: {
      identityMarkdown: `# Koda Identity

- Name: Koda
- Role: local-first AI collaborator
- Default tone: calm, practical, direct
- Operating style: inspectable, reversible, approval-first when needed
`,
      soulMarkdown: `# Koda Soul

- First principle: protect user trust and local data boundaries
- Decision style: clarify before acting and prefer reversible paths
- Behavior rule: use approvals by default for risky or outward-facing actions
`,
      userMarkdown: `# User Profile

- Preferred working style:
- Communication style:
- Boundaries:
- Success signals:
`,
    },
  });

  const text = useLocaleText({
    zh: {
      documentTitle: "KodaClaw 现场中枢",
      documentDescription: "KodaClaw 现场中枢：面向本地优先代理的引导、对话与控制台工作台。",
      notCreatedYet: "尚未创建",
      gatewayFallback: "代理 / 同源",
      railEyebrow: "作业导引",
      railTitle: "控制航道",
      railCopy: "把对话、审批、诊断、渠道与扩展都收进一个带上下文的操作界面。",
      railModeLabel: "运行模式",
      railModeBootstrap: "引导模式",
      railModeMain: "主控模式",
      railSessionLabel: "主会话",
      railVersionLabel: "工作区版本",
      bootstrapNavTitle: "引导阶段",
      bootstrapNavBody: "先完成身份与边界沉淀，再切入主控工作台。",
      commandEyebrow: "工作台视角",
      commandBody: (eyebrow: string, sessionId: string) => `${eyebrow} · 当前主会话 ${sessionId}`,
      contextStatusEyebrow: "态势摘要",
      healthLabels: {
        healthy: "健康",
        warning: "降级",
        error: "异常",
        unknown: "未知",
      },
      activeBootstrap: {
        label: "引导工作台",
        eyebrow: "引导阶段",
        summary: "把第一轮对话沉淀成可复用的身份设定与协作边界，然后再进入主控模式。",
      },
      desks: [
        {
          id: "chat",
          label: "对话航道",
          eyebrow: "协同对话",
          summary: "在持续可见的消息时间线上发起任务、接收流式回复，并保留系统注记。",
        },
        {
          id: "inbox",
          label: "收件 / 审批",
          eyebrow: "行动队列",
          summary: "把待处理事项、审批请求与人工决策集中在一个操作面里。",
        },
        {
          id: "sessions",
          label: "会话 / 诊断",
          eyebrow: "审计追踪",
          summary: "检查会话生命周期、关键事件与诊断证据，再决定下一步动作。",
        },
        {
          id: "models",
          label: "模型 / 设置",
          eyebrow: "运行控制",
          summary: "调整模型端点、更新信号与运行偏好，同时保持风险边界清晰可见。",
        },
        {
          id: "automations",
          label: "自动化",
          eyebrow: "周期任务",
          summary: "浏览计划、近期运行结果与启停状态，让重复工作保持可控。",
        },
        {
          id: "channels",
          label: "渠道枢纽",
          eyebrow: "外部线程",
          summary: "检查账号、线程绑定与待发审批，确保对外回复在正确的边界内执行。",
        },
        {
          id: "plugins",
          label: "插件舰桥",
          eyebrow: "扩展能力",
          summary: "从信任、权限、运行态与日志四个维度掌控插件暴露给新会话的方式。",
        },
        {
          id: "canvas",
          label: "画布档案",
          eyebrow: "发布界面",
          summary: "维护默认入口、查看产物元数据，并随时验证可发布的展示面。",
        },
      ] as DeskMeta[],
      status: {
        unavailableTitle: "Gateway 快照不可用",
        loadingTitle: "正在同步工作区状态",
        loadingBody: "先确认健康状态与 bootstrap-state，再让工作台进入可操作状态。",
        bootstrapTitle: "引导契约仍在进行中",
        bootstrapBody: "先通过对话补足 onboarding 信息，再把整理好的 Markdown 提交到引导面板。",
        inboxTitle: "审批与待办已经就位",
        inboxBody: "集中处理待确认动作、清理收件队列，并保持人工决策留痕。",
        sessionsTitle: "诊断航道已激活",
        sessionsBody: "先阅读会话轨迹、断点与时间线证据，再决定是否继续操作运行态。",
        modelsTitle: "运行控制已解锁",
        modelsBody: "在不离开主工作台的前提下调节模型端点、风险信号与工作区偏好。",
        automationsTitle: "周期任务台已展开",
        automationsBody: "从同一个界面审视排程、近期执行与启停状态，避免自动化失控。",
        pluginsTitle: "插件舰桥已激活",
        pluginsBody: "先看信任与健康，再决定是否把插件工具暴露给新的会话。",
        channelsTitle: "渠道枢纽已激活",
        channelsBody: "在释放对外回复之前，先检查连接器健康、线程绑定与待审草稿。",
        canvasTitle: "画布档案台已激活",
        canvasBody: "持续验证默认入口、元数据与展示面，让发布内容保持可审视。",
        mainTitle: "主控对话已激活",
        mainBody: "工作区已脱离引导阶段，现在可以接收常规 Koda 请求并返回流式结果。",
      },
      notes: {
        gatewaySnapshot: (status: string, version: number, rootPath: string) =>
          `Gateway ${status}。工作区版本 ${version}，路径 ${rootPath}。`,
        gatewayError: (detail: string) => `Gateway 快照错误：${detail}`,
      },
      bootstrap: {
        required: "在完成引导前，身份设定、灵魂准则与用户画像都不能为空。",
        draftInputRequired: "请先通过聊天梳理引导信息，或先写下一版初稿，再生成结构化草稿。",
        generating: "正在根据引导对话生成草稿…",
        generate: "根据对话生成草稿",
        generated: "已根据引导对话刷新草稿，请检查后再提交。",
        summaryLabel: "草稿摘要",
        archived: "引导结果已保存，`BOOTSTRAP.md` 已归档。",
        removed: "引导结果已保存，`BOOTSTRAP.md` 已在提交后移除。",
        completed: (identityPath: string, soulPath: string, userPath: string) =>
          `引导完成。身份设定写入 ${identityPath}；灵魂准则写入 ${soulPath}；用户画像写入 ${userPath}。`,
        draftApplied: (summary: string) => `已生成引导草稿：${summary}`,
        failed: (detail: string) => `引导提交失败：${detail}`,
        unknown: "未知错误",
        detail: (sessionId: string) => `当前主会话：${sessionId}`,
        waitingDetail: "正在等待 bootstrap-state 响应。",
      },
      chat: {
        initialSystemNote: "现场中枢已就绪。Gateway 快照会决定当前处于引导阶段还是主控对话阶段。",
        placeholderBootstrap: "询问身份、边界和 onboarding 相关问题…",
        placeholderMain: "描述你希望 Koda 处理的任务…",
        emptyCompletion: "[空响应]",
        unknownStreamError: "未知流式错误。",
        streamClosed: "[流式通道已关闭]",
        failedToReachStream: "无法连接到聊天流。",
      },
    },
    en: {
      documentTitle: "KodaClaw Field Console",
      documentDescription: "KodaClaw field console for local-first agent bootstrap, conversation, and control-plane operations.",
      notCreatedYet: "not created yet",
      gatewayFallback: "proxy / same-origin",
      railEyebrow: "Navigation",
      railTitle: "Control Lanes",
      railCopy: "Bring conversations, approvals, diagnostics, channels, and extensions into one contextual operator surface.",
      railModeLabel: "Mode",
      railModeBootstrap: "Bootstrap",
      railModeMain: "Main",
      railSessionLabel: "Main Session",
      railVersionLabel: "Workspace Version",
      bootstrapNavTitle: "Bootstrap Stage",
      bootstrapNavBody: "Finish identity and boundary capture before moving into the main operating desk.",
      commandEyebrow: "Workbench",
      commandBody: (eyebrow: string, sessionId: string) => `${eyebrow} · active main session ${sessionId}`,
      contextStatusEyebrow: "Status Summary",
      healthLabels: {
        healthy: "Healthy",
        warning: "Degraded",
        error: "Unhealthy",
        unknown: "Unknown",
      },
      activeBootstrap: {
        label: "Bootstrap Desk",
        eyebrow: "Onboarding",
        summary: "Turn the first conversation into durable identity and collaboration boundaries before entering main mode.",
      },
      desks: [
        {
          id: "chat",
          label: "Chat Lane",
          eyebrow: "Conversation",
          summary: "Dispatch tasks, receive streaming responses, and keep system notes visible on the same conversational surface.",
        },
        {
          id: "inbox",
          label: "Inbox / Approval",
          eyebrow: "Action queue",
          summary: "Keep inbound follow-up items and approval decisions in one operator-facing command queue.",
        },
        {
          id: "sessions",
          label: "Sessions / Diagnostics",
          eyebrow: "Audit trail",
          summary: "Inspect lifecycle traces, key events, and diagnostic evidence before acting on runtime state.",
        },
        {
          id: "models",
          label: "Models / Settings",
          eyebrow: "Runtime control",
          summary: "Tune model endpoints, risk signals, and workspace preferences without leaving the main surface.",
        },
        {
          id: "automations",
          label: "Automations",
          eyebrow: "Recurring jobs",
          summary: "Review schedules, recent runs, and enablement so recurring work stays understandable and reversible.",
        },
        {
          id: "channels",
          label: "Channels",
          eyebrow: "External threads",
          summary: "Inspect accounts, thread bindings, and outbound approvals before releasing responses beyond the workspace.",
        },
        {
          id: "plugins",
          label: "Plugins",
          eyebrow: "Extensibility",
          summary: "Control trust, permissions, runtime state, and logs before exposing plugin tools to fresh sessions.",
        },
        {
          id: "canvas",
          label: "Canvas",
          eyebrow: "Published surfaces",
          summary: "Keep default entry points, metadata, and the inspectable publishing surface aligned from one desk.",
        },
      ] as DeskMeta[],
      status: {
        unavailableTitle: "Gateway snapshot unavailable",
        loadingTitle: "Synchronizing workspace state",
        loadingBody: "Checking health and bootstrap-state before the desk goes live.",
        bootstrapTitle: "Bootstrap contract is still active",
        bootstrapBody: "Use the chat lane to interrogate onboarding details, then commit the curated markdown from the onboarding panel.",
        inboxTitle: "Approval queue is on the desk",
        inboxBody: "Review pending approvals, sweep inbox status, and keep operator follow-up visible.",
        sessionsTitle: "Trace lane is active",
        sessionsBody: "Inspect session lifecycles, breakpoints, and timeline evidence before acting on runtime state.",
        modelsTitle: "Runtime controls are unlocked",
        modelsBody: "Tune model endpoints and workspace preferences without leaving the observatory surface.",
        automationsTitle: "Recurring control is on the desk",
        automationsBody: "Inspect schedules, review recent runs, and enable or pause recurring jobs from one editorial surface.",
        pluginsTitle: "Plugin command deck is active",
        pluginsBody: "Review trust, enablement, runtime state, and logs before exposing plugin tools to new sessions.",
        channelsTitle: "Channel operations desk is active",
        channelsBody: "Inspect connector health, thread bindings, and pending delivery approvals before you release outbound replies.",
        canvasTitle: "Canvas atelier is active",
        canvasBody: "Review published artifacts, verify the default entry, and keep the render surface inspectable.",
        mainTitle: "Main chat mode is active",
        mainBody: "The workspace has left bootstrap and can accept regular Koda requests with SSE streaming.",
      },
      notes: {
        gatewaySnapshot: (status: string, version: number, rootPath: string) =>
          `Gateway ${status}. Workspace v${version} at ${rootPath}.`,
        gatewayError: (detail: string) => `Gateway snapshot error: ${detail}`,
      },
      bootstrap: {
        required: "Identity, soul, and user markdown are all required before bootstrap can complete.",
        draftInputRequired: "Add onboarding conversation detail in chat or seed the draft fields before generating a structured draft.",
        generating: "Generating a draft from the onboarding conversation...",
        generate: "Generate draft from chat",
        generated: "Draft refreshed from the onboarding conversation. Review it before committing.",
        summaryLabel: "Draft summary",
        archived: "Bootstrap completion stored. BOOTSTRAP.md has been archived.",
        removed: "Bootstrap completion stored. BOOTSTRAP.md was removed after commit.",
        completed: (identityPath: string, soulPath: string, userPath: string) =>
          `Bootstrap completed. Identity saved to ${identityPath}; soul saved to ${soulPath}; user profile saved to ${userPath}.`,
        draftApplied: (summary: string) => `Bootstrap draft generated: ${summary}`,
        failed: (detail: string) => `Bootstrap completion failed: ${detail}`,
        unknown: "unknown error",
        detail: (sessionId: string) => `Active main session: ${sessionId}`,
        waitingDetail: "Waiting for bootstrap-state response.",
      },
      chat: {
        initialSystemNote: "Field console ready. Gateway snapshot will determine bootstrap or main chat mode.",
        placeholderBootstrap: "Ask identity, boundary, and onboarding prompts...",
        placeholderMain: "Describe the task you want Koda to handle...",
        emptyCompletion: "[empty completion]",
        unknownStreamError: "Unknown stream error.",
        streamClosed: "[stream closed]",
        failedToReachStream: "Failed to reach chat stream.",
      },
    },
  });

  const { health, snapshot, isLoading, error, mode, refresh } = useGatewaySnapshot();
  const { draft, setDraft, isStreaming, messages, placeholder, sendMessage, appendSystemNote } =
    useChatConsole(mode, text.chat);
  const [mainDesk, setMainDesk] = useState<MainDesk>(() => readStoredMainDesk());
  const [shellVariant] = useState(() => readStoredShellVariant());
  const identityTemplateRef = useRef(bootstrapTemplates.identityMarkdown);
  const soulTemplateRef = useRef(bootstrapTemplates.soulMarkdown);
  const userTemplateRef = useRef(bootstrapTemplates.userMarkdown);
  const [identityMarkdown, setIdentityMarkdown] = useState(() => bootstrapTemplates.identityMarkdown);
  const [soulMarkdown, setSoulMarkdown] = useState(() => bootstrapTemplates.soulMarkdown);
  const [userMarkdown, setUserMarkdown] = useState(() => bootstrapTemplates.userMarkdown);
  const [archiveBootstrapFile, setArchiveBootstrapFile] = useState(true);
  const [isGeneratingBootstrapDraft, setIsGeneratingBootstrapDraft] = useState(false);
  const [isCompletingBootstrap, setIsCompletingBootstrap] = useState(false);
  const [bootstrapDraftSummary, setBootstrapDraftSummary] = useState<string | null>(null);
  const [bootstrapError, setBootstrapError] = useState<string | null>(null);
  const [bootstrapSuccess, setBootstrapSuccess] = useState<string | null>(null);
  const [sessionsFocusRequest, setSessionsFocusRequest] = useState<{
    sessionId: string;
    requestId: number;
  } | null>(null);
  const lastSnapshotSignature = useRef<string | null>(null);
  const lastErrorSignature = useRef<string | null>(null);
  const modeRef = useRef(mode);
  const pendingLaunchTarget = useRef<DesktopLaunchTarget | null>(getInitialLaunchTarget());
  const sessionsFocusRequestId = useRef(0);

  useEffect(() => {
    if (identityMarkdown === identityTemplateRef.current) {
      setIdentityMarkdown(bootstrapTemplates.identityMarkdown);
    }

    identityTemplateRef.current = bootstrapTemplates.identityMarkdown;
  }, [bootstrapTemplates.identityMarkdown, identityMarkdown]);

  useEffect(() => {
    if (soulMarkdown === soulTemplateRef.current) {
      setSoulMarkdown(bootstrapTemplates.soulMarkdown);
    }

    soulTemplateRef.current = bootstrapTemplates.soulMarkdown;
  }, [bootstrapTemplates.soulMarkdown, soulMarkdown]);

  useEffect(() => {
    if (userMarkdown === userTemplateRef.current) {
      setUserMarkdown(bootstrapTemplates.userMarkdown);
    }

    userTemplateRef.current = bootstrapTemplates.userMarkdown;
  }, [bootstrapTemplates.userMarkdown, userMarkdown]);

  useEffect(() => {
    modeRef.current = mode;
  }, [mode]);

  useEffect(() => {
    document.title = text.documentTitle;
    const description = document.querySelector('meta[name="description"]');
    if (description) {
      description.setAttribute("content", text.documentDescription);
    }
  }, [text.documentDescription, text.documentTitle]);

  useEffect(() => {
    return subscribeDesktopLaunchTargets((target) => {
      pendingLaunchTarget.current = target;

      if (modeRef.current !== "main") {
        return;
      }

      const nextDesk = resolveMainDeskFromLaunchTarget(target);
      if (!nextDesk) {
        pendingLaunchTarget.current = null;
        return;
      }

      setMainDesk(nextDesk);
      pendingLaunchTarget.current = null;
    });
  }, []);

  useEffect(() => {
    if (mode !== "main") {
      return;
    }

    const nextDesk = resolveMainDeskFromLaunchTarget(pendingLaunchTarget.current);
    if (!nextDesk) {
      return;
    }

    setMainDesk(nextDesk);
    pendingLaunchTarget.current = null;
  }, [mode]);

  useEffect(() => {
    if (!snapshot) {
      return;
    }

    const signature = `${health?.status ?? "unknown"}|${snapshot.workspaceVersion}|${snapshot.workspaceRootPath}`;
    if (lastSnapshotSignature.current === signature) {
      return;
    }

    appendSystemNote(
      text.notes.gatewaySnapshot(
        text.healthLabels[resolveHealthTone(health?.status ?? "unknown")],
        snapshot.workspaceVersion,
        snapshot.workspaceRootPath,
      ),
    );
    lastSnapshotSignature.current = signature;
  }, [appendSystemNote, health?.status, snapshot, text.notes]);

  useEffect(() => {
    if (!error) {
      return;
    }

    if (lastErrorSignature.current === error) {
      return;
    }

    appendSystemNote(text.notes.gatewayError(error));
    lastErrorSignature.current = error;
  }, [appendSystemNote, error, text.notes]);

  useEffect(() => {
    if (mode !== "main") {
      return;
    }

    window.localStorage.setItem(MAIN_DESK_STORAGE_KEY, mainDesk);
  }, [mainDesk, mode]);

  useEffect(() => {
    persistShellVariant(shellVariant);
  }, [shellVariant]);

  const clearBootstrapFeedback = useCallback(() => {
    setBootstrapDraftSummary(null);
    setBootstrapError(null);
    setBootstrapSuccess(null);
  }, []);

  const handleOpenSessionDetail = useCallback((sessionId: string) => {
    sessionsFocusRequestId.current += 1;
    setSessionsFocusRequest({
      sessionId,
      requestId: sessionsFocusRequestId.current,
    });
    setMainDesk("sessions");
  }, []);

  const handleBootstrapSubmit = useCallback(async () => {
    const nextIdentityMarkdown = identityMarkdown.trim();
    const nextSoulMarkdown = soulMarkdown.trim();
    const nextUserMarkdown = userMarkdown.trim();

    if (!nextIdentityMarkdown || !nextSoulMarkdown || !nextUserMarkdown) {
      setBootstrapError(text.bootstrap.required);
      setBootstrapSuccess(null);
      return;
    }

    setIsCompletingBootstrap(true);
    setBootstrapError(null);
    setBootstrapSuccess(null);

    try {
      const result = await completeBootstrap({
        identityMarkdown: nextIdentityMarkdown,
        soulMarkdown: nextSoulMarkdown,
        userMarkdown: nextUserMarkdown,
        archiveBootstrapFile,
      });

      setBootstrapSuccess(result.bootstrapFileArchived ? text.bootstrap.archived : text.bootstrap.removed);
      setMainDesk("chat");
      appendSystemNote(text.bootstrap.completed(result.identityFilePath, result.soulFilePath, result.userFilePath));
      await refresh();
    } catch (nextError) {
      const detail = nextError instanceof Error ? nextError.message : text.bootstrap.unknown;
      setBootstrapError(detail);
      appendSystemNote(text.bootstrap.failed(detail));
    } finally {
      setIsCompletingBootstrap(false);
    }
  }, [appendSystemNote, archiveBootstrapFile, identityMarkdown, refresh, soulMarkdown, text.bootstrap, userMarkdown]);

  const handleBootstrapDraftGenerate = useCallback(async () => {
    const conversation = buildBootstrapConversation(messages);
    const hasSeedDraft =
      identityMarkdown.trim().length > 0 ||
      soulMarkdown.trim().length > 0 ||
      userMarkdown.trim().length > 0;
    if (conversation.length === 0 && !hasSeedDraft) {
      setBootstrapError(text.bootstrap.draftInputRequired);
      setBootstrapSuccess(null);
      setBootstrapDraftSummary(null);
      return;
    }

    setIsGeneratingBootstrapDraft(true);
    setBootstrapError(null);
    setBootstrapSuccess(null);
    setBootstrapDraftSummary(null);

    try {
      const result = await generateBootstrapDraft({
        conversation,
        identityMarkdown: identityMarkdown.trim() || null,
        soulMarkdown: soulMarkdown.trim() || null,
        userMarkdown: userMarkdown.trim() || null,
      });

      setIdentityMarkdown(result.identityMarkdown);
      setSoulMarkdown(result.soulMarkdown);
      setUserMarkdown(result.userMarkdown);
      setBootstrapDraftSummary(result.summary);
      setBootstrapSuccess(text.bootstrap.generated);
      appendSystemNote(text.bootstrap.draftApplied(result.summary));
    } catch (nextError) {
      const detail = nextError instanceof Error ? nextError.message : text.bootstrap.unknown;
      setBootstrapError(detail);
      appendSystemNote(text.bootstrap.failed(detail));
    } finally {
      setIsGeneratingBootstrapDraft(false);
    }
  }, [appendSystemNote, identityMarkdown, messages, soulMarkdown, text.bootstrap, userMarkdown]);

  const activeDeskMeta = useMemo(() => {
    if (mode !== "main") {
      return text.activeBootstrap;
    }

    return text.desks.find((item) => item.id === mainDesk) ?? text.desks[0];
  }, [mainDesk, mode, text.activeBootstrap, text.desks]);

  const statusModel = useMemo<StatusModel>(() => {
    if (error) {
      return {
        title: text.status.unavailableTitle,
        body: error,
        level: "error" as const,
      };
    }

    if (isLoading) {
      return {
        title: text.status.loadingTitle,
        body: text.status.loadingBody,
        level: "warning" as const,
      };
    }

    if (mode === "bootstrap") {
      return {
        title: text.status.bootstrapTitle,
        body: text.status.bootstrapBody,
        level: "warning" as const,
      };
    }

    switch (mainDesk) {
      case "inbox":
        return { title: text.status.inboxTitle, body: text.status.inboxBody, level: "normal" as const };
      case "sessions":
        return { title: text.status.sessionsTitle, body: text.status.sessionsBody, level: "normal" as const };
      case "models":
        return { title: text.status.modelsTitle, body: text.status.modelsBody, level: "normal" as const };
      case "automations":
        return { title: text.status.automationsTitle, body: text.status.automationsBody, level: "normal" as const };
      case "plugins":
        return { title: text.status.pluginsTitle, body: text.status.pluginsBody, level: "normal" as const };
      case "channels":
        return { title: text.status.channelsTitle, body: text.status.channelsBody, level: "normal" as const };
      case "canvas":
        return { title: text.status.canvasTitle, body: text.status.canvasBody, level: "normal" as const };
      case "chat":
      default:
        return { title: text.status.mainTitle, body: text.status.mainBody, level: "normal" as const };
    }
  }, [error, isLoading, mainDesk, mode, text.status]);

  const gatewayUrl = getGatewayUrl() || text.gatewayFallback;
  const activeSessionLabel = snapshot?.activeMainSessionId ?? text.notCreatedYet;
  const workspaceVersionLabel = snapshot ? String(snapshot.workspaceVersion) : "--";
  const modeLabel = mode === "bootstrap" ? text.railModeBootstrap : text.railModeMain;

  const workbench = useMemo(() => {
    if (mode !== "main") {
      return (
        <section className="shell-workbench desk-column desk-column--chat" data-testid="chat-view" data-kc-mode="chat">
          <MessageTimeline messages={messages} isStreaming={isStreaming} />
          <ChatComposer
            value={draft}
            placeholder={placeholder}
            disabled={isLoading || isCompletingBootstrap}
            isStreaming={isStreaming}
            onChange={setDraft}
            onSubmit={sendMessage}
          />
        </section>
      );
    }

    if (mainDesk === "inbox") {
      return (
        <section className="shell-workbench desk-column desk-column--chat" data-testid="control-plane-view" data-kc-view="inbox">
          <InboxApprovalDesk />
        </section>
      );
    }

    if (mainDesk === "sessions") {
      return (
        <section className="shell-workbench desk-column desk-column--chat" data-testid="control-plane-view" data-kc-view="sessions">
          <SessionsDiagnosticsDesk
            focusRequest={sessionsFocusRequest}
            onFocusRequestConsumed={() => setSessionsFocusRequest(null)}
          />
        </section>
      );
    }

    if (mainDesk === "models") {
      return (
        <section className="shell-workbench desk-column desk-column--chat" data-testid="control-plane-view" data-kc-view="models">
          <ModelsSettingsDesk />
        </section>
      );
    }

    if (mainDesk === "automations") {
      return (
        <section className="shell-workbench desk-column desk-column--chat" data-testid="control-plane-view" data-kc-view="automations">
          <AutomationsDesk />
        </section>
      );
    }

    if (mainDesk === "canvas") {
      return (
        <section className="shell-workbench desk-column desk-column--chat" data-testid="control-plane-view" data-kc-view="canvas">
          <CanvasDesk />
        </section>
      );
    }

    if (mainDesk === "plugins") {
      return (
        <section className="shell-workbench desk-column desk-column--chat" data-testid="control-plane-view" data-kc-view="plugins">
          <PluginsDesk />
        </section>
      );
    }

    if (mainDesk === "channels") {
      return (
        <section className="shell-workbench desk-column desk-column--chat" data-testid="control-plane-view" data-kc-view="channels">
          <ChannelsDesk />
        </section>
      );
    }

    return (
      <section className="shell-workbench desk-column desk-column--chat" data-testid="chat-view" data-kc-mode="chat">
        <MessageTimeline messages={messages} isStreaming={isStreaming} />
        <ChatComposer
          value={draft}
          placeholder={placeholder}
          disabled={isLoading || isCompletingBootstrap}
          isStreaming={isStreaming}
          onChange={setDraft}
          onSubmit={sendMessage}
        />
      </section>
    );
  }, [
    draft,
    isCompletingBootstrap,
    isLoading,
    isStreaming,
    mainDesk,
    messages,
    mode,
    placeholder,
    sendMessage,
    sessionsFocusRequest,
    setDraft,
  ]);

  const contextPanel = useMemo(() => {
    return (
      <>
        <SystemStatusCard
          statusTitle={statusModel.title}
          statusBody={statusModel.body}
          level={statusModel.level}
          eyebrow={text.contextStatusEyebrow}
        />
        <BootstrapPanel
          needsBootstrap={mode === "bootstrap"}
          detail={
            snapshot
              ? text.bootstrap.detail(snapshot.activeMainSessionId ?? text.notCreatedYet)
              : text.bootstrap.waitingDetail
          }
          identityMarkdown={identityMarkdown}
          soulMarkdown={soulMarkdown}
          userMarkdown={userMarkdown}
          archiveBootstrapFile={archiveBootstrapFile}
          isGeneratingDraft={isGeneratingBootstrapDraft}
          isSubmitting={isCompletingBootstrap}
          draftSummary={bootstrapDraftSummary}
          error={bootstrapError}
          success={bootstrapSuccess}
          onIdentityChange={(next) => {
            clearBootstrapFeedback();
            setIdentityMarkdown(next);
          }}
          onSoulChange={(next) => {
            clearBootstrapFeedback();
            setSoulMarkdown(next);
          }}
          onUserChange={(next) => {
            clearBootstrapFeedback();
            setUserMarkdown(next);
          }}
          onArchiveBootstrapChange={(next) => {
            clearBootstrapFeedback();
            setArchiveBootstrapFile(next);
          }}
          onGenerateDraft={handleBootstrapDraftGenerate}
          onSubmit={handleBootstrapSubmit}
        />
      </>
    );
  }, [
    archiveBootstrapFile,
    bootstrapDraftSummary,
    bootstrapError,
    bootstrapSuccess,
    clearBootstrapFeedback,
    handleBootstrapDraftGenerate,
    handleBootstrapSubmit,
    identityMarkdown,
    isGeneratingBootstrapDraft,
    isCompletingBootstrap,
    mode,
    snapshot,
    soulMarkdown,
    statusModel.body,
    statusModel.level,
    statusModel.title,
    text.bootstrap,
    text.contextStatusEyebrow,
    text.notCreatedYet,
    userMarkdown,
  ]);

  const shellLayoutProps = {
    mode,
    activeDeskMeta,
    mainDesk,
    desks: text.desks,
    onMainDeskChange: setMainDesk,
    onOpenSessionDetail: handleOpenSessionDetail,
    railEyebrow: text.railEyebrow,
    railTitle: text.railTitle,
    railCopy: text.railCopy,
    railModeLabel: text.railModeLabel,
    railModeValue: modeLabel,
    railSessionLabel: text.railSessionLabel,
    activeSessionId: snapshot?.activeMainSessionId,
    activeSessionValue: activeSessionLabel,
    railVersionLabel: text.railVersionLabel,
    workspaceVersionValue: workspaceVersionLabel,
    bootstrapNavTitle: text.bootstrapNavTitle,
    bootstrapNavBody: text.bootstrapNavBody,
    commandEyebrow: text.commandEyebrow,
    commandBody: text.commandBody(activeDeskMeta.eyebrow, activeSessionLabel),
    gatewayUrl,
    healthStatus: health?.status ?? "unknown",
    workspaceRootPath: snapshot?.workspaceRootPath,
    workbench,
    contextPanel,
  };

  return shellVariant === "v2" ? <V2Shell {...shellLayoutProps} /> : <LegacyShell {...shellLayoutProps} />;
}
