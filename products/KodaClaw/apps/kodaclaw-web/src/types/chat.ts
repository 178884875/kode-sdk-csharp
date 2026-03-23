export type ChatRole = "user" | "assistant" | "system" | "error" | "approval" | "tool_activity" | "history_separator";
export type ChatMessageStatus = "streaming" | "done" | "error";
export type ApprovalDecision = "approved" | "rejected" | "pending";

export interface ChatMessage {
  id: string;
  role: ChatRole;
  text: string;
  status: ChatMessageStatus;
  timestamp: number;
  sessionId?: string | null;
  // approval card fields
  approvalId?: string | null;
  callId?: string | null;
  toolName?: string | null;
  inputPreview?: string | null;
  decision?: ApprovalDecision;
  // tool activity fields
  durationMs?: number | null;
  // history fields
  isHistory?: boolean;
}
