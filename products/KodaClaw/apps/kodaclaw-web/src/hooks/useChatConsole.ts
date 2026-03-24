import { useCallback, useMemo, useRef, useState } from "react";
import { streamChatEvents, submitApprovalDecision, fetchSessionMessages } from "../lib/api";
import type { ChatMessage } from "../types/chat";
import type { SessionMessageItem } from "../types/contracts";

export type ChatConsoleCopy = {
  initialSystemNote: string;
  placeholderMain: string;
  emptyCompletion: string;
  unknownStreamError: string;
  streamClosed: string;
  failedToReachStream: string;
  newSessionNote?: string;
  sessionResumedNote?: string;
};

function createId(prefix: string): string {
  return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
}

function createMessage(
  role: ChatMessage["role"],
  text: string,
  status: ChatMessage["status"],
  sessionId?: string | null,
  approvalFields?: Pick<ChatMessage, "approvalId" | "callId" | "toolName" | "inputPreview" | "decision">,
): ChatMessage {
  return {
    id: createId(role),
    role,
    text,
    status,
    timestamp: Date.now(),
    sessionId,
    ...approvalFields,
  };
}

export function useChatConsole(copy: ChatConsoleCopy) {
  const [draft, setDraft] = useState("");
  const [isStreaming, setIsStreaming] = useState(false);
  const [activeToolName, setActiveToolName] = useState<string | null>(null);
  const [isLoadingHistory, setIsLoadingHistory] = useState(false);
  const [hasMoreHistory, setHasMoreHistory] = useState(false);
  const historySkipRef = useRef(0);
  const historySessionIdRef = useRef<string | null>(null);
  // Track callIds that already have an approval message, so tool_activity can skip duplicates
  const approvalCallIds = useRef<Set<string>>(new Set());
  const [messages, setMessages] = useState<ChatMessage[]>([
    createMessage(
      "system",
      copy.initialSystemNote,
      "done",
    ),
  ]);

  const placeholder = useMemo(() => copy.placeholderMain, [copy.placeholderMain]);

  const sendMessage = useCallback(async (mediaIds?: string[], mediaUrls?: string[]) => {
    const content = draft.trim();
    const hasMedia = mediaIds != null && mediaIds.length > 0;
    if ((!content && !hasMedia) || isStreaming) {
      return;
    }

    const userMessage: ChatMessage = {
      ...createMessage("user", content || "📎", "done"),
      ...(mediaUrls && mediaUrls.length > 0 ? { mediaUrls } : {}),
    };
    const assistantMessage = createMessage("assistant", "", "streaming");

    setDraft("");
    setIsStreaming(true);
    approvalCallIds.current = new Set();
    setMessages((current) => [...current, userMessage, assistantMessage]);

    try {
      let lastStep: number | null = null;
      for await (const event of streamChatEvents({ message: content, mediaIds: hasMedia ? mediaIds : null })) {
        if (event.type === "text_chunk") {
          const stepChanged =
            lastStep !== null && event.step != null && event.step !== lastStep;
          if (event.step != null) lastStep = event.step;
          setMessages((current) =>
            current.map((message) =>
              message.id === assistantMessage.id
                ? {
                    ...message,
                    text: `${message.text}${stepChanged ? "\n\n" : ""}${event.delta ?? ""}`,
                    status: "streaming",
                    timestamp: event.timestamp ?? Date.now(),
                    sessionId: event.sessionId,
                  }
                : message,
            ),
          );
          continue;
        }

        if (event.type === "approval_required") {
          if (event.callId) approvalCallIds.current.add(event.callId);
          setMessages((current) => [
            ...current,
            createMessage("approval", "", "done", event.sessionId, {
              approvalId: event.approvalId ?? null,
              callId: event.callId ?? null,
              toolName: event.toolName ?? null,
              inputPreview: event.inputPreview ?? null,
              decision: "pending",
            }),
          ]);
          continue;
        }

        if (event.type === "approval_decided") {
          setMessages((current) =>
            current.map((message) =>
              message.role === "approval" && message.approvalId === event.approvalId
                ? {
                    ...message,
                    decision: event.decision === "allow" ? "approved" : "rejected",
                  }
                : message,
            ),
          );
          continue;
        }

        if (event.type === "tool_warning") {
          setMessages((current) => [
            ...current,
            createMessage("system", `⚠ ${event.reason ?? "工具调用失败"}`, "done", event.sessionId),
          ]);
          continue;
        }

        if (event.type === "agent_working") {
          setActiveToolName(event.toolName ?? null);
          continue;
        }

        if (event.type === "tool_activity") {
          setActiveToolName(null);
          // Skip if an approval card already represents this call (no duplicate needed)
          if (event.callId && approvalCallIds.current.has(event.callId)) continue;
          const toolMsg = createMessage("tool_activity", "", "done", event.sessionId);
          setMessages((current) => [
            ...current,
            { ...toolMsg, toolName: event.toolName ?? null, durationMs: event.durationMs ?? null },
          ]);
          continue;
        }

        if (event.type === "done") {
          setActiveToolName(null);
          setMessages((current) =>
            current.map((message) =>
              message.id === assistantMessage.id
                ? {
                    ...message,
                    status: "done",
                    sessionId: event.sessionId,
                    timestamp: event.timestamp ?? Date.now(),
                    text: message.text || copy.emptyCompletion,
                  }
                : message,
            ),
          );
          setIsStreaming(false);
          return;
        }

        const detail = event.error?.message ?? event.reason ?? copy.unknownStreamError;
        setActiveToolName(null);
        setMessages((current) =>
          current.map((message) =>
            message.id === assistantMessage.id
              ? {
                  ...message,
                  role: "error",
                  status: "error",
                  text: detail,
                  sessionId: event.sessionId,
                  timestamp: event.timestamp ?? Date.now(),
                }
              : message,
          ),
        );
        setIsStreaming(false);
        return;
      }

      setMessages((current) =>
        current.map((message) =>
          message.id === assistantMessage.id
            ? {
                ...message,
                status: "done",
                text: message.text || copy.streamClosed,
                timestamp: Date.now(),
              }
            : message,
        ),
      );
    } catch (error) {
      const detail = error instanceof Error ? error.message : copy.failedToReachStream;
      setActiveToolName(null);
      setMessages((current) =>
        current.map((message) =>
          message.id === assistantMessage.id
            ? {
                ...message,
                role: "error",
                status: "error",
                text: detail,
                timestamp: Date.now(),
              }
            : message,
        ),
      );
    } finally {
      setIsStreaming(false);
    }
  }, [copy.emptyCompletion, copy.failedToReachStream, copy.initialSystemNote, copy.placeholderMain, copy.streamClosed, copy.unknownStreamError, draft, isStreaming]);

  const appendSystemNote = useCallback((note: string) => {
    setMessages((current) => [...current, createMessage("system", note, "done")]);
  }, []);

  const clearMessages = useCallback((systemNote?: string) => {
    setMessages([
      createMessage("system", systemNote ?? copy.initialSystemNote, "done"),
    ]);
    setHasMoreHistory(false);
    historySkipRef.current = 0;
    historySessionIdRef.current = null;
  }, [copy.initialSystemNote]);

  const prependHistory = useCallback((items: SessionMessageItem[], hasMore: boolean, sessionId: string) => {
    if (items.length === 0) return;
    const historyMessages: ChatMessage[] = items.map((item) => ({
      id: `history-${item.id}`,
      role: item.role,
      text: item.text,
      status: "done" as const,
      timestamp: item.timestamp ?? Date.now(),
      isHistory: true,
    }));
    const separator = createMessage("history_separator", "", "done");
    setMessages((current) => [...historyMessages, separator, ...current]);
    setHasMoreHistory(hasMore);
  }, []);

  const loadMoreHistory = useCallback(async () => {
    const sessionId = historySessionIdRef.current;
    if (!sessionId || isLoadingHistory) return;
    const limit = 20;
    const skip = historySkipRef.current + limit;
    setIsLoadingHistory(true);
    try {
      const result = await fetchSessionMessages(sessionId, limit, skip);
      if (result.items.length > 0) {
        const historyMessages: ChatMessage[] = result.items.map((item) => ({
          id: `history-${skip}-${item.id}`,
          role: item.role,
          text: item.text,
          status: "done" as const,
          timestamp: item.timestamp ?? Date.now(),
          isHistory: true,
        }));
        setMessages((current) => [...historyMessages, ...current]);
        historySkipRef.current = skip;
      }
      setHasMoreHistory(result.hasMore);
    } catch {
      // silently fail — history load is best-effort
    } finally {
      setIsLoadingHistory(false);
    }
  }, [isLoadingHistory]);

  const loadHistory = useCallback(async (sessionId: string) => {
    historySessionIdRef.current = sessionId;
    historySkipRef.current = 0;
    setIsLoadingHistory(true);
    try {
      const result = await fetchSessionMessages(sessionId, 20, 0);
      prependHistory(result.items, result.hasMore, sessionId);
    } catch {
      // silently fail
    } finally {
      setIsLoadingHistory(false);
    }
  }, [prependHistory]);

  const submitApproval = useCallback(async (approvalId: string, approve: boolean) => {
    setMessages((current) =>
      current.map((message) =>
        message.role === "approval" && message.approvalId === approvalId
          ? { ...message, decision: approve ? "approved" : "rejected" }
          : message,
      ),
    );
    try {
      await submitApprovalDecision(approvalId, approve);
    } catch {
      // Revert to pending on error
      setMessages((current) =>
        current.map((message) =>
          message.role === "approval" && message.approvalId === approvalId
            ? { ...message, decision: "pending" }
            : message,
        ),
      );
    }
  }, []);

  return {
    draft,
    setDraft,
    isStreaming,
    activeToolName,
    messages,
    placeholder,
    sendMessage,
    appendSystemNote,
    clearMessages,
    submitApproval,
    loadHistory,
    loadMoreHistory,
    isLoadingHistory,
    hasMoreHistory,
  };
}
