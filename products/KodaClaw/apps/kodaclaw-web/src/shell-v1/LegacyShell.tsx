import { DeskHeader } from "../components/DeskHeader";
import { ShellLayoutProps } from "../shell-shared/types";

export function LegacyShell({
  mode,
  activeDeskMeta,
  mainDesk,
  desks,
  onMainDeskChange,
  railEyebrow,
  railTitle,
  railCopy,
  railModeLabel,
  railModeValue,
  railSessionLabel,
  activeSessionValue,
  railVersionLabel,
  workspaceVersionValue,
  bootstrapNavTitle,
  bootstrapNavBody,
  commandEyebrow,
  commandBody,
  gatewayUrl,
  healthStatus,
  workspaceRootPath,
  workbench,
  contextPanel,
}: ShellLayoutProps) {
  return (
    <div className="app-shell">
      <div className="paper-haze" />
      <div className="grain-layer" />
      <main className={`shell-grid shell-grid--${mode}`} data-kc-mode={mode} data-testid={`${mode}-shell`}>
        <aside className="shell-rail">
          <div className="shell-rail__brand">
            <p className="section-eyebrow">{railEyebrow}</p>
            <h2 className="shell-rail__title">{railTitle}</h2>
            <p className="section-copy">{railCopy}</p>
          </div>

          {mode === "main" ? (
            <section className="desk-command-bar shell-nav" data-testid="desk-switcher">
              <div>
                <p className="section-eyebrow">{commandEyebrow}</p>
                <h3 className="command-title">{activeDeskMeta.label}</h3>
                <p className="section-copy">{commandBody}</p>
              </div>
              <div className="desk-command-bar__actions shell-nav__actions">
                {desks.map((option) => (
                  <button
                    key={option.id}
                    type="button"
                    className={`desk-tab ${mainDesk === option.id ? "desk-tab--active" : ""}`}
                    data-testid={`desk-tab-${option.id}`}
                    onClick={() => onMainDeskChange(option.id)}
                  >
                    <span className="metric-label">{option.eyebrow}</span>
                    <span>{option.label}</span>
                  </button>
                ))}
              </div>
            </section>
          ) : (
            <section className="desk-command-bar shell-nav shell-nav--bootstrap" data-testid="desk-switcher">
              <div>
                <p className="section-eyebrow">{activeDeskMeta.eyebrow}</p>
                <h3 className="command-title">{bootstrapNavTitle}</h3>
                <p className="section-copy">{bootstrapNavBody}</p>
              </div>
            </section>
          )}

          <div className="shell-rail__meta">
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
          </div>
        </aside>

        <section className="shell-hero">
          <DeskHeader
            mode={mode}
            workspaceRootPath={workspaceRootPath}
            gatewayUrl={gatewayUrl}
            healthStatus={healthStatus}
            activeDeskLabel={activeDeskMeta.label}
            activeDeskEyebrow={activeDeskMeta.eyebrow}
            activeDeskSummary={activeDeskMeta.summary}
          />
        </section>

        <aside className="shell-context">{contextPanel}</aside>

        {workbench}
      </main>
    </div>
  );
}
