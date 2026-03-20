import { ShellLayoutProps } from "../shell-shared/types";
import { ChatContextRail } from "./ChatContextRail";
import { useLocaleText } from "../i18n/I18nProvider";

type ContextRailProps = Pick<
  ShellLayoutProps,
  | "mode"
  | "activeDeskMeta"
  | "mainDesk"
  | "desks"
  | "onMainDeskChange"
  | "onOpenSessionDetail"
  | "bootstrapNavTitle"
  | "bootstrapNavBody"
  | "commandEyebrow"
  | "commandBody"
  | "railModeLabel"
  | "railModeValue"
  | "railSessionLabel"
  | "activeSessionId"
  | "activeSessionValue"
  | "railVersionLabel"
  | "workspaceVersionValue"
  | "contextPanel"
>;

export function ContextRail({
  mode,
  activeDeskMeta,
  mainDesk,
  desks,
  onMainDeskChange,
  onOpenSessionDetail,
  bootstrapNavTitle,
  bootstrapNavBody,
  commandEyebrow,
  commandBody,
  railModeLabel,
  railModeValue,
  railSessionLabel,
  activeSessionId,
  activeSessionValue,
  railVersionLabel,
  workspaceVersionValue,
  contextPanel,
}: ContextRailProps) {
  const text = useLocaleText({
    zh: {
      chatEyebrow: "辅助上下文",
      chatTitle: "会话脉络",
      chatBody: "左侧只保留主会话与最近轨迹，主线程舞台始终留给消息流与输入。",
    },
    en: {
      chatEyebrow: "Context layer",
      chatTitle: "Session pulse",
      chatBody: "Keep the active thread and recent traces in the side layer so the main stage stays focused on the live conversation.",
    },
  });

  const isChatMain = mode === "main" && mainDesk === "chat";

  return (
    <aside className="v2-context-rail" data-testid="v2-context-rail">
      <section className={`v2-context-card v2-context-card--hero ${isChatMain ? "is-chat" : ""}`}>
        <p className="section-eyebrow">
          {isChatMain ? text.chatEyebrow : mode === "main" ? commandEyebrow : activeDeskMeta.eyebrow}
        </p>
        <h2 className="v2-context-card__title">
          {isChatMain ? text.chatTitle : mode === "main" ? activeDeskMeta.label : bootstrapNavTitle}
        </h2>
        <p className="section-copy">
          {isChatMain ? text.chatBody : mode === "main" ? commandBody : bootstrapNavBody}
        </p>
      </section>

      <section className="v2-context-meta">
        <div className="metric-item">
          <span className="metric-label">{railModeLabel}</span>
          <span className="metric-value">{railModeValue}</span>
        </div>
        <div className="metric-item">
          <span className="metric-label">{railSessionLabel}</span>
          <span className="metric-value metric-value--path">{activeSessionValue}</span>
        </div>
        <div className="metric-item">
          <span className="metric-label">{railVersionLabel}</span>
          <span className="metric-value">{workspaceVersionValue}</span>
        </div>
      </section>

      {mode === "main" && mainDesk === "chat" ? (
        <ChatContextRail
          activeSessionId={activeSessionId}
          onOpenSessionsDesk={() => onMainDeskChange("sessions")}
          onOpenSessionDetail={onOpenSessionDetail}
        />
      ) : null}

      {mode === "main" && mainDesk !== "chat" ? (
        <section className="v2-context-stack" data-testid="v2-desk-stack">
          {desks.map((desk) => {
            const isActive = desk.id === mainDesk;
            return (
              <button
                key={desk.id}
                type="button"
                className={`v2-desk-card ${isActive ? "is-active" : ""}`}
                onClick={() => onMainDeskChange(desk.id)}
              >
                <span className="v2-desk-card__eyebrow">{desk.eyebrow}</span>
                <strong>{desk.label}</strong>
                <span>{desk.summary}</span>
              </button>
            );
          })}
        </section>
      ) : null}

      <div className="v2-context-rail__support">{contextPanel}</div>
    </aside>
  );
}
