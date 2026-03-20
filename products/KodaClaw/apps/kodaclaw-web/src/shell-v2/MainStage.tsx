import { useLocaleText } from "../i18n/I18nProvider";
import { LocaleToggle } from "../components/LocaleToggle";
import { ModeBadge } from "../components/ModeBadge";
import { ShellLayoutProps } from "../shell-shared/types";

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

type MainStageProps = Pick<
  ShellLayoutProps,
  | "mode"
  | "activeDeskMeta"
  | "mainDesk"
  | "activeSessionValue"
  | "gatewayUrl"
  | "healthStatus"
  | "workspaceRootPath"
  | "workbench"
>;

export function MainStage({
  mode,
  activeDeskMeta,
  mainDesk,
  activeSessionValue,
  gatewayUrl,
  healthStatus,
  workspaceRootPath,
  workbench,
}: MainStageProps) {
  const text = useLocaleText({
    zh: {
      eyebrow: "会话优先工作区",
      chatEyebrow: "主线程工作区",
      stageTitle: "主舞台",
      threadLabel: "当前主会话",
      gateway: "Gateway",
      health: "健康态",
      workspace: "工作区",
      workspaceWaiting: "等待 bootstrap-state",
      healthLabels: {
        healthy: "健康",
        warning: "降级",
        error: "异常",
        unknown: "未知",
      },
    },
    en: {
      eyebrow: "Conversation-first shell",
      chatEyebrow: "Primary thread workspace",
      stageTitle: "Main Stage",
      threadLabel: "Active main session",
      gateway: "Gateway",
      health: "Health",
      workspace: "Workspace",
      workspaceWaiting: "Waiting for bootstrap-state",
      healthLabels: {
        healthy: "Healthy",
        warning: "Degraded",
        error: "Unhealthy",
        unknown: "Unknown",
      },
    },
  });

  const healthTone = resolveHealthTone(healthStatus);
  const isChatStage = mode === "main" && mainDesk === "chat";

  return (
    <section className={`v2-main-stage ${isChatStage ? "is-chat" : ""}`} data-testid="v2-main-stage">
      {isChatStage ? (
        <header className="v2-chat-stage-head" data-testid="v2-chat-stage-head">
          <div className="v2-chat-stage-head__row">
            <div className="v2-chat-stage-head__intro">
              <p className="section-eyebrow">{text.chatEyebrow}</p>
              <h1 className="v2-chat-stage-head__title">{activeDeskMeta.label}</h1>
              <p className="section-copy v2-chat-stage-head__summary">{activeDeskMeta.summary}</p>
            </div>
            <div className="v2-stage-head__controls">
              <LocaleToggle />
              <ModeBadge mode={mode} />
            </div>
          </div>

          <div className="v2-chat-stage-chips" data-testid="v2-chat-stage">
            <div className="v2-stage-chip v2-stage-chip--primary">
              <span className="metric-label">{text.threadLabel}</span>
              <span className="metric-value metric-value--path">{activeSessionValue}</span>
            </div>
            <div className="v2-stage-chip">
              <span className="metric-label">{text.gateway}</span>
              <span className="metric-value metric-value--path">{gatewayUrl}</span>
            </div>
            <div className="v2-stage-chip">
              <span className="metric-label">{text.health}</span>
              <span className={`metric-value metric-value--${healthTone}`}>{text.healthLabels[healthTone]}</span>
            </div>
            <div className="v2-stage-chip">
              <span className="metric-label">{text.workspace}</span>
              <span className="metric-value metric-value--path">{workspaceRootPath ?? text.workspaceWaiting}</span>
            </div>
          </div>
        </header>
      ) : (
        <header className="v2-stage-head">
          <div className="v2-stage-head__intro">
            <p className="section-eyebrow">{text.eyebrow}</p>
            <div className="v2-stage-head__title-row">
              <div>
                <h1 className="v2-stage-head__title">{activeDeskMeta.label}</h1>
                <p className="section-copy v2-stage-head__summary">{activeDeskMeta.summary}</p>
              </div>
              <div className="v2-stage-head__controls">
                <LocaleToggle />
                <ModeBadge mode={mode} />
              </div>
            </div>
          </div>

          <div className="v2-stage-head__metrics">
            <div className="metric-item metric-item--hero">
              <span className="metric-label">{text.stageTitle}</span>
              <span className="metric-value">{activeDeskMeta.eyebrow}</span>
            </div>
            <div className="metric-item">
              <span className="metric-label">{text.gateway}</span>
              <span className="metric-value metric-value--path">{gatewayUrl}</span>
            </div>
            <div className="metric-item">
              <span className="metric-label">{text.health}</span>
              <span className={`metric-value metric-value--${healthTone}`}>{text.healthLabels[healthTone]}</span>
            </div>
            <div className="metric-item">
              <span className="metric-label">{text.workspace}</span>
              <span className="metric-value metric-value--path">{workspaceRootPath ?? text.workspaceWaiting}</span>
            </div>
          </div>
        </header>
      )}

      <div className={`v2-stage-surface ${isChatStage ? "v2-stage-surface--chat" : ""}`}>{workbench}</div>
    </section>
  );
}
