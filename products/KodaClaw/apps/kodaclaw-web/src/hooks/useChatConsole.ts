import { useCallback, useMemo, useState } from "react";
import { streamChatEvents } from "../lib/api";
import type { ChatMessage } from "../types/chat";
import type { ShellMode } from "./useGatewaySnapshot";

export type ChatConsoleCopy = {
  initialSystemNote: string;
  placeholderBootstrap: string;
  placeholderMain: string;
  emptyCompletion: string;
  unknownStreamError: string;
  streamClosed: string;
  failedToReachStream: string;
};

function createId(prefix: string): string {
  return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
}

function createMessage(
  role: ChatMessage["role"],
  text: string,
  status: ChatMessage["status"],
  sessionId?: string | null,
): ChatMessage {
  return {
    id: createId(role),
    role,
    text,
    status,
    timestamp: Date.now(),
    sessionId,
  };
}

export function useChatConsole(mode: ShellMode, copy: ChatConsoleCopy) {
  const [draft, setDraft] = useState("");
  const [isStreaming, setIsStreaming] = useState(false);
  const [messages, setMessages] = useState<ChatMessage[]>([
    createMessage(
      "system",
      copy.initialSystemNote,
      "done",
    ),
  ]);

  const placeholder = useMemo(() => {
    return mode === "bootstrap"
      ? copy.placeholderBootstrap
      : copy.placeholderMain;
  }, [copy.placeholderBootstrap, copy.placeholderMain, mode]);

  const sendMessage = useCallback(async () => {
    const content = draft.trim();
    if (!content || isStreaming) {
      return;
    }

    const userMessage = createMessage("user", content, "done");
    const assistantMessage = createMessage("assistant", "", "streaming");

    setDraft("");
    setIsStreaming(true);
    setMessages((current) => [...current, userMessage, assistantMessage]);

    try {
      for await (const event of streamChatEvents({ message: content })) {
        if (event.type === "text_chunk") {
          setMessages((current) =>
            current.map((message) =>
              message.id === assistantMessage.id
                ? {
                    ...message,
                    text: `${message.text}${event.delta ?? ""}`,
                    status: "streaming",
                    timestamp: event.timestamp ?? Date.now(),
                    sessionId: event.sessionId,
                  }
                : message,
            ),
          );
          continue;
        }

        if (event.type === "done") {
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
  }, [copy.emptyCompletion, copy.failedToReachStream, copy.initialSystemNote, copy.placeholderBootstrap, copy.placeholderMain, copy.streamClosed, copy.unknownStreamError, draft, isStreaming]);

  const appendSystemNote = useCallback((note: string) => {
    setMessages((current) => [...current, createMessage("system", note, "done")]);
  }, []);

  return {
    draft,
    setDraft,
    isStreaming,
    messages,
    placeholder,
    sendMessage,
    appendSystemNote,
  };
}
