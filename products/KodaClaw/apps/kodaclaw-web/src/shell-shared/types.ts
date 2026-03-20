import type { ReactNode } from "react";

export type ShellMode = "bootstrap" | "main";
export type MainDesk = "chat" | "inbox" | "sessions" | "models" | "automations" | "channels" | "plugins" | "canvas";

export type DeskMeta = {
  id: MainDesk;
  label: string;
  eyebrow: string;
  summary: string;
};

export type DeskPresentation = Pick<DeskMeta, "label" | "eyebrow" | "summary">;

export type StatusModel = {
  title: string;
  body: string;
  level: "normal" | "warning" | "error";
};

export type ShellLayoutProps = {
  mode: ShellMode;
  activeDeskMeta: DeskPresentation;
  mainDesk: MainDesk;
  desks: DeskMeta[];
  onMainDeskChange: (desk: MainDesk) => void;
  onOpenSessionDetail: (sessionId: string) => void;
  railEyebrow: string;
  railTitle: string;
  railCopy: string;
  railModeLabel: string;
  railModeValue: string;
  railSessionLabel: string;
  activeSessionId?: string | null;
  activeSessionValue: string;
  railVersionLabel: string;
  workspaceVersionValue: string;
  bootstrapNavTitle: string;
  bootstrapNavBody: string;
  commandEyebrow: string;
  commandBody: string;
  gatewayUrl: string;
  healthStatus: string;
  workspaceRootPath?: string;
  workbench: ReactNode;
  contextPanel: ReactNode;
};
