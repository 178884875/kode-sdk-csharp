import { useLocaleText } from "../i18n/I18nProvider";
import { LocaleToggle } from "./LocaleToggle";
import { ModeBadge } from "./ModeBadge";

type DeskHeaderProps = {
  mode: "bootstrap" | "main";
  workspaceRootPath?: string;
  gatewayUrl: string;
  healthStatus: string;
  activeDeskLabel: string;
  activeDeskEyebrow: string;
  activeDeskSummary: string;
};

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

export function DeskHeader({
  mode,
  workspaceRootPath,
  gatewayUrl,
  healthStatus,
  activeDeskLabel,
  activeDeskEyebrow,
  activeDeskSummary,
}: DeskHeaderProps) {
  const text = useLocaleText({
    zh: {
      brandEyebrow: "KodaClaw 指挥台",
      title: "现场中枢",
      subtitle: "把引导、对话、运维与控制面收拢到一个可审视、可追踪、可切换语言的本地工作台。",
      activeDesk: "当前工作台",
      gateway: "Gateway 目标",
      health: "健康状态",
      workspace: "工作区",
      waiting: "正在等待 bootstrap-state 返回",
      gatewayFallback: "代理 / 同源",
      healthLabels: {
        healthy: "健康",
        warning: "降级",
        error: "异常",
        unknown: "未知",
      },
    },
    en: {
      brandEyebrow: "KodaClaw Observatory Desk",
      title: "Field Console",
      subtitle: "A local-first operating surface for bootstrap decisions, conversational work, and control-plane supervision.",
      activeDesk: "Active Desk",
      gateway: "Gateway target",
      health: "Health",
      workspace: "Workspace",
      waiting: "Waiting for bootstrap-state",
      gatewayFallback: "proxy / same-origin",
      healthLabels: {
        healthy: "Healthy",
        warning: "Degraded",
        error: "Unhealthy",
        unknown: "Unknown",
      },
    },
  });
  const healthTone = resolveHealthTone(healthStatus);

  return (
    <header className="desk-header">
      <div className="desk-header__intro">
        <div className="desk-header__masthead">
          <div>
            <p className="eyebrow">{text.brandEyebrow}</p>
            <h1 className="desk-title" data-testid="desk-header-title">{text.title}</h1>
            <p className="desk-subtitle">{text.subtitle}</p>
          </div>
          <div className="desk-header__controls">
            <LocaleToggle />
            <ModeBadge mode={mode} />
          </div>
        </div>

        <div className="desk-headline-card">
          <p className="section-eyebrow">{activeDeskEyebrow}</p>
          <h2 className="section-title">{activeDeskLabel}</h2>
          <p className="section-copy">{activeDeskSummary}</p>
        </div>
      </div>

      <div className="desk-header__facts">
        <div className="metric-item metric-item--hero">
          <span className="metric-label">{text.activeDesk}</span>
          <span className="metric-value">{activeDeskLabel}</span>
        </div>
        <div className="metric-item">
          <span className="metric-label">{text.gateway}</span>
          <span className="metric-value">{gatewayUrl || text.gatewayFallback}</span>
        </div>
        <div className="metric-item">
          <span className="metric-label">{text.health}</span>
          <span className={`metric-value metric-value--${healthTone}`}>
            {text.healthLabels[healthTone]}
          </span>
        </div>
        <div className="metric-item">
          <span className="metric-label">{text.workspace}</span>
          <span className="metric-value metric-value--path">
            {workspaceRootPath ?? text.waiting}
          </span>
        </div>
      </div>
    </header>
  );
}
