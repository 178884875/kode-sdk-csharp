import type { ChatMessage } from "../types/chat";
import { useI18n, useLocaleText } from "../i18n/I18nProvider";

type MessageTimelineProps = {
  messages: ChatMessage[];
  isStreaming: boolean;
};

export function MessageTimeline({ messages, isStreaming }: MessageTimelineProps) {
  const { formatTime } = useI18n();
  const text = useLocaleText({
    zh: {
      eyebrow: "对话航迹",
      title: "消息时间线",
      streaming: "流式输出中",
      idle: "待命",
      empty: "还没有消息。使用下方输入框开启新一轮对话。",
      roles: {
        user: "用户",
        assistant: "Koda",
        system: "系统",
        error: "错误",
      },
    },
    en: {
      eyebrow: "Conversation",
      title: "Message Timeline",
      streaming: "Streaming",
      idle: "Idle",
      empty: "No messages yet. Use the composer to start a new turn.",
      roles: {
        user: "User",
        assistant: "Koda",
        system: "System",
        error: "Error",
      },
    },
  });

  return (
    <section className="timeline" data-testid="chat-stream">
      <div className="timeline__header">
        <div>
          <div className="section-eyebrow">{text.eyebrow}</div>
          <h2 className="section-title">{text.title}</h2>
        </div>
        <span className={`stream-indicator ${isStreaming ? "is-live" : ""}`}>
          {isStreaming ? text.streaming : text.idle}
        </span>
      </div>
      <div className="timeline__body" role="log" aria-live="polite">
        {messages.length === 0 ? (
          <p className="timeline__empty">{text.empty}</p>
        ) : (
          messages.map((message) => (
            <article
              key={message.id}
              className={`message message--${message.role} message--${message.status}`}
            >
              <header className="message__meta">
                <span className="message__role">{text.roles[message.role]}</span>
                <span className="message__time">{formatTime(message.timestamp)}</span>
              </header>
              <p className="message__text">{message.text || " "}</p>
            </article>
          ))
        )}
      </div>
    </section>
  );
}
