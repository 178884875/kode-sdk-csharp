export type ChatRole = "user" | "assistant" | "system" | "error";
export type ChatMessageStatus = "streaming" | "done" | "error";

export interface ChatMessage {
  id: string;
  role: ChatRole;
  text: string;
  status: ChatMessageStatus;
  timestamp: number;
  sessionId?: string | null;
}
