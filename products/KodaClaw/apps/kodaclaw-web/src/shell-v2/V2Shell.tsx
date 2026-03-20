import { GlobalRail } from "./GlobalRail";
import { ContextRail } from "./ContextRail";
import { MainStage } from "./MainStage";
import { ShellLayoutProps } from "../shell-shared/types";
import "./shell-v2.css";

export function V2Shell(props: ShellLayoutProps) {
  return (
    <div className="app-shell v2-shell-wrap" data-testid="v2-shell">
      <div className="paper-haze" />
      <div className="grain-layer" />
      <main className={`v2-shell v2-shell--${props.mode}`} data-kc-mode={props.mode}>
        <GlobalRail
          mode={props.mode}
          desks={props.desks.map((desk) => ({ id: desk.id, label: desk.label }))}
          activeDesk={props.mainDesk}
          onDeskChange={props.onMainDeskChange}
        />
        <ContextRail {...props} />
        <MainStage {...props} />
      </main>
    </div>
  );
}
