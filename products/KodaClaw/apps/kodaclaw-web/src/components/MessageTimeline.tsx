import { useEffect, useRef } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { MessageSquare, Settings } from "lucide-react";
import type { ChatMessage, ChatRole } from "../types/chat";
import { useI18n, useLocaleText } from "../i18n/I18nProvider";
import { EmptyState } from "./ui/EmptyState";
import { ApprovalCard } from "./chat/ApprovalCard";

type MessageTimelineProps = {
  messages: ChatMessage[];
  isStreaming: boolean;
  onSubmitApproval?: (approvalId: string, approve: boolean) => void;
  hasMoreHistory?: boolean;
  isLoadingHistory?: boolean;
  onLoadMoreHistory?: () => void;
};

export function MessageTimeline({ messages, isStreaming, onSubmitApproval, hasMoreHistory, isLoadingHistory, onLoadMoreHistory }: MessageTimelineProps) {
  const { formatTime } = useI18n();
  const bottomRef = useRef<HTMLDivElement>(null);

  const text = useLocaleText({
    zh: {
      empty: "还没有消息。使用下方输入框开启新一轮对话。",
      historySeparator: "以下为历史对话",
      loadMore: "加载更多",
      loadingHistory: "加载中…",
      roles: {
        user: "用户",
        assistant: "Koda",
        system: "系统",
        error: "错误",
        approval: "审批",
        history_separator: "",
        tool_activity: "",
      } as Record<ChatRole, string>,
    },
    en: {
      empty: "No messages yet. Use the composer below to start a conversation.",
      historySeparator: "History",
      loadMore: "Load more",
      loadingHistory: "Loading…",
      roles: {
        user: "You",
        assistant: "Koda",
        system: "System",
        error: "Error",
        approval: "Approval",
        history_separator: "",
        tool_activity: "",
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
      {hasMoreHistory && (
        <div className="load-more-history">
          <button
            type="button"
            className="load-more-history__btn"
            disabled={isLoadingHistory}
            onClick={onLoadMoreHistory}
          >
            {isLoadingHistory ? text.loadingHistory : text.loadMore}
          </button>
        </div>
      )}
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
                <article key={message.id} className={`message message--user${message.isHistory ? " message--history" : ""}`}>
                  <p className="message__text">{message.text}</p>
                </article>
              );
            }

            if (message.role === "assistant") {
              return (
                <article
                  key={message.id}
                  className={`message message--assistant message--${message.status}${message.isHistory ? " message--history" : ""}`}
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

            if (message.role === "tool_activity") {
              return (
                <div key={message.id} className="tool-activity-block">
                  <Settings size={11} strokeWidth={2} className="tool-activity-block__icon" />
                  <span className="tool-activity-block__name">{message.toolName ?? "tool"}</span>
                  {message.durationMs != null && (
                    <span className="tool-activity-block__dur">{message.durationMs}ms</span>
                  )}
                </div>
              );
            }

            if (message.role === "history_separator") {
              return (
                <div key={message.id} className="history-separator">
                  <span className="history-separator__label">{text.historySeparator}</span>
                </div>
              );
            }

            if (message.role === "approval" && message.approvalId) {
              return (
                <ApprovalCard
                  key={message.id}
                  approvalId={message.approvalId}
                  toolName={message.toolName ?? "unknown"}
                  inputPreview={message.inputPreview}
                  decision={message.decision ?? "pending"}
                  onApprove={(id) => onSubmitApproval?.(id, true)}
                  onReject={(id) => onSubmitApproval?.(id, false)}
                />
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
