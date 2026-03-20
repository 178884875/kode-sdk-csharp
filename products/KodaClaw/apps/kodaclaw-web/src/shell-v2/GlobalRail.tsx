import { MainDesk, ShellMode } from "../shell-shared/types";

type GlobalRailProps = {
  mode: ShellMode;
  desks: Array<{
    id: MainDesk;
    label: string;
  }>;
  activeDesk: MainDesk;
  onDeskChange: (desk: MainDesk) => void;
};

const DESK_GLYPHS: Record<MainDesk, string> = {
  chat: "CH",
  inbox: "IN",
  sessions: "SE",
  models: "MD",
  automations: "AU",
  channels: "CN",
  plugins: "PL",
  canvas: "CV",
};

export function GlobalRail({ mode, desks, activeDesk, onDeskChange }: GlobalRailProps) {
  return (
    <aside className="v2-global-rail" data-testid="v2-global-rail">
      <div className="v2-global-rail__brand">
        <div className="v2-global-rail__seal">KC</div>
        <div className="v2-global-rail__wordmark">
          <span>Koda</span>
          <span>Claw</span>
        </div>
      </div>

      {mode === "main" ? (
        <nav className="v2-global-rail__nav" aria-label="Desk navigation">
          {desks.map((desk) => {
            const isActive = desk.id === activeDesk;
            return (
              <button
                key={desk.id}
                type="button"
                className={`v2-global-rail__button ${isActive ? "is-active" : ""}`}
                data-testid={`desk-tab-${desk.id}`}
                aria-pressed={isActive}
                onClick={() => onDeskChange(desk.id)}
              >
                <span className="v2-global-rail__glyph" aria-hidden="true">{DESK_GLYPHS[desk.id]}</span>
                <span className="v2-global-rail__label">{desk.label}</span>
              </button>
            );
          })}
        </nav>
      ) : (
        <div className="v2-global-rail__bootstrap" data-testid="v2-bootstrap-pill">
          <span>ON</span>
          <span>BOARD</span>
        </div>
      )}
    </aside>
  );
}
