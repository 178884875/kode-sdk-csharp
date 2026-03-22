import { useEffect, useRef } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { MessageSquare } from "lucide-react";
import type { ChatMessage, ChatRole } from "../types/chat";
import { useI18n, useLocaleText } from "../i18n/I18nProvider";
import { EmptyState } from "./ui/EmptyState";

type MessageTimelineProps = {
  messages: ChatMessage[];
  isStreaming: boolean;
};

export function MessageTimeline({ messages, isStreaming }: MessageTimelineProps) {
  const { formatTime } = useI18n();
  const bottomRef = useRef<HTMLDivElement>(null);

  const text = useLocaleText({
    zh: {
      empty: "还没有消息。使用下方输入框开启新一轮对话。",
      roles: {
        user: "用户",
        assistant: "Koda",
        system: "系统",
        error: "错误",
      } as Record<ChatRole, string>,
    },
    en: {
      empty: "No messages yet. Use the composer below to start a conversation.",
      roles: {
        user: "You",
        assistant: "Koda",
        system: "System",
        error: "Error",
      } as Record<ChatRole, string>,
    },
  });

  // Auto-scroll to bottom on new messages or streaming update
  useEffect(() => {
    const el = bottomRef.current;
    if (el && typeof el.scrollIntoView === "function") {
      el.scrollIntoView({ behavior: "smooth" });
    }
  }, [messages.length, isStreaming]);

  return (
    <section className="timeline" data-testid="chat-stream">
      <div className="timeline__body" role="log" aria-live="polite">
        {messages.length === 0 ? (
          <EmptyState
            icon={<MessageSquare size={32} strokeWidth={1.5} />}
            title={text.empty}
          />
        ) : (
          messages.map((message) => {
            if (message.role === "user") {
              return (
                <article key={message.id} className="message message--user">
                  <p className="message__text">{message.text}</p>
                </article>
              );
            }

            if (message.role === "assistant") {
              return (
                <article
                  key={message.id}
                  className={`message message--assistant message--${message.status}`}
                >
                  <header className="message__meta">
                    <span className="message__role">{text.roles.assistant}</span>
                    <span className="message__time">{formatTime(message.timestamp)}</span>
                  </header>
                  <div className="message__prose">
                    <ReactMarkdown remarkPlugins={[remarkGfm]}>
                      {message.text || ""}
                    </ReactMarkdown>
                    {message.status === "streaming" && (
                      <span className="message__cursor" aria-hidden="true">▋</span>
                    )}
                  </div>
                </article>
              );
            }

            // system / error
            return (
              <article
                key={message.id}
                className={`message message--${message.role}`}
              >
                <header className="message__meta">
                  <span className="message__role">{text.roles[message.role]}</span>
                  <span className="message__time">{formatTime(message.timestamp)}</span>
                </header>
                <p className="message__text">{message.text}</p>
              </article>
            );
          })
        )}
        <div ref={bottomRef} />
      </div>
    </section>
  );
}
