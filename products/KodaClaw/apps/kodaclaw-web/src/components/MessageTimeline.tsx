import { useCallback, useEffect, useRef, useState } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { CheckCircle, MessageSquare, X, XCircle, type LucideProps } from "lucide-react";
import type { ForwardRefExoticComponent, RefAttributes } from "react";
import type { ChatMessage, ChatRole } from "../types/chat";
import { useI18n, useLocaleText } from "../i18n/I18nProvider";
import { EmptyState } from "./ui/EmptyState";
import { ApprovalCard } from "./chat/ApprovalCard";

// ── Tool Band: groups consecutive tool calls into a flat chip row ──────────────

type ToolGroupItem = {
  msg: ChatMessage;
  seq: number;        // 1-indexed occurrence of this toolName within the group
  warning?: string;   // associated tool_warning text, if any
};

type RenderItem =
  | { kind: "message"; msg: ChatMessage }
  | { kind: "tool_group"; items: ToolGroupItem[] };

function groupMessages(messages: ChatMessage[]): RenderItem[] {
  const result: RenderItem[] = [];
  let currentGroup: ToolGroupItem[] | null = null;
  const nameCounts: Record<string, number> = {};

  function flushGroup() {
    if (currentGroup && currentGroup.length > 0) {
      result.push({ kind: "tool_group", items: currentGroup });
    }
    currentGroup = null;
    for (const k in nameCounts) delete nameCounts[k];
  }

  for (const msg of messages) {
    const isDecidedApproval = msg.role === "approval" && msg.decision !== "pending";
    const isToolActivity = msg.role === "tool_activity";
    const isToolWarning = msg.role === "system" && msg.isToolWarning === true;

    if (isDecidedApproval || isToolActivity) {
      if (!currentGroup) currentGroup = [];
      const toolName = msg.toolName ?? "tool";
      nameCounts[toolName] = (nameCounts[toolName] ?? 0) + 1;
      currentGroup.push({ msg, seq: nameCounts[toolName] });
    } else if (isToolWarning) {
      if (currentGroup && currentGroup.length > 0) {
        currentGroup[currentGroup.length - 1].warning = msg.text.replace(/^⚠\s*/, "");
      } else {
        flushGroup();
        result.push({ kind: "message", msg });
      }
    } else {
      flushGroup();
      result.push({ kind: "message", msg });
    }
  }

  flushGroup();
  return result;
}

function extractBashCommand(inputPreview?: string | null): string | null {
  if (!inputPreview) return null;
  try {
    const parsed = JSON.parse(inputPreview);
    if (typeof parsed.command === "string") return parsed.command;
  } catch { /* not JSON */ }
  return inputPreview.length < 300 ? inputPreview : null;
}

function ToolBand({ items }: { items: ToolGroupItem[] }) {
  // Compute total occurrences per tool name to decide whether to show sequence numbers
  const totalCounts = items.reduce<Record<string, number>>((acc, item) => {
    const name = item.msg.toolName ?? "tool";
    acc[name] = (acc[name] ?? 0) + 1;
    return acc;
  }, {});

  return (
    <div className="tool-band">
      {items.map((item, idx) => {
        const toolName = item.msg.toolName ?? "tool";
        const showSeq = totalCounts[toolName] > 1;
        const isBashRun = toolName === "bash_run";
        const command = isBashRun ? extractBashCommand(item.msg.inputPreview) : null;
        const hasWarning = !!item.warning;

        type LucideIcon = ForwardRefExoticComponent<Omit<LucideProps, "ref"> & RefAttributes<SVGSVGElement>>;
        let statusClass = "tool-chip--activity";
        let StatusIcon: LucideIcon | null = null;
        let statusLabel: string | null = null;

        if (item.msg.role === "approval") {
          if (item.msg.decision === "approved") {
            statusClass = hasWarning ? "tool-chip--warning" : "tool-chip--approved";
            StatusIcon = hasWarning ? null : CheckCircle;
          } else {
            statusClass = "tool-chip--rejected";
            StatusIcon = XCircle;
            statusLabel = "已拒绝";
          }
        } else if (hasWarning) {
          statusClass = "tool-chip--warning";
        }

        if (hasWarning) {
          statusLabel = item.warning ?? null;
        }

        const durationMs = item.msg.durationMs;

        return (
          <div key={item.msg.id ?? idx} className={`tool-chip ${statusClass}`}>
            <div className="tool-chip__header">
              {StatusIcon && (
                <StatusIcon size={11} strokeWidth={2} aria-hidden="true" />
              )}
              {hasWarning && !StatusIcon && (
                <span className="tool-chip__warn-icon" aria-hidden="true">△</span>
              )}
              <code className="tool-chip__name">
                {toolName}
                {showSeq && <sub className="tool-chip__seq">{item.seq}</sub>}
              </code>
              {statusLabel && (
                <span className="tool-chip__status-label">{statusLabel}</span>
              )}
              {!statusLabel && durationMs != null && (
                <span className="tool-chip__dur">· {durationMs}ms</span>
              )}
            </div>
            {command && (
              <div className="tool-chip__cmd" title={command}>{command}</div>
            )}
          </div>
        );
      })}
    </div>
  );
}

// ── Lightbox ──────────────────────────────────────────────────────────────────

function ImageLightbox({ url, onClose }: { url: string; onClose: () => void }) {
  const [scale, setScale] = useState<number | null>(null); // null = not yet measured
  const [pos, setPos] = useState({ x: 0, y: 0 });
  const baseScaleRef = useRef(1); // fitted scale = "100%" for this image
  const [isDragging, setIsDragging] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const scaleRef = useRef(1);
  const posRef = useRef({ x: 0, y: 0 });
  const mouseStartRef = useRef<{ mx: number; my: number; tx: number; ty: number } | null>(null);
  const touchDistRef = useRef<number | null>(null);
  const touchPosRef = useRef<{ x: number; y: number } | null>(null);

  const resolvedScale = scale ?? 1;
  scaleRef.current = resolvedScale;
  posRef.current = pos;

  // ESC to close
  useEffect(() => {
    const handler = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [onClose]);

  // Wheel zoom (non-passive so we can preventDefault)
  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    const handler = (e: WheelEvent) => {
      e.preventDefault();
      const factor = e.deltaY > 0 ? 0.85 : 1.18;
      setScale(s => Math.min(Math.max((s ?? 1) * factor, baseScaleRef.current * 0.3), baseScaleRef.current * 10));
    };
    el.addEventListener("wheel", handler, { passive: false });
    return () => el.removeEventListener("wheel", handler);
  }, []);

  // Touch: pinch-to-zoom + single-finger pan
  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;

    function dist(t: TouchList) {
      return Math.hypot(t[0].clientX - t[1].clientX, t[0].clientY - t[1].clientY);
    }

    function onStart(e: TouchEvent) {
      if (e.touches.length === 2) {
        touchDistRef.current = dist(e.touches);
      } else if (e.touches.length === 1) {
        touchPosRef.current = { x: e.touches[0].clientX, y: e.touches[0].clientY };
      }
    }
    function onMove(e: TouchEvent) {
      e.preventDefault();
      if (e.touches.length === 2 && touchDistRef.current !== null) {
        const d = dist(e.touches);
        const ratio = d / touchDistRef.current;
        setScale(s => Math.min(Math.max((s ?? 1) * ratio, baseScaleRef.current * 0.3), baseScaleRef.current * 10));
        touchDistRef.current = d;
      } else if (e.touches.length === 1 && scaleRef.current > 1 && touchPosRef.current) {
        const dx = e.touches[0].clientX - touchPosRef.current.x;
        const dy = e.touches[0].clientY - touchPosRef.current.y;
        setPos(p => ({ x: p.x + dx, y: p.y + dy }));
        touchPosRef.current = { x: e.touches[0].clientX, y: e.touches[0].clientY };
      }
    }
    function onEnd() {
      touchDistRef.current = null;
      touchPosRef.current = null;
    }

    el.addEventListener("touchstart", onStart, { passive: true });
    el.addEventListener("touchmove", onMove, { passive: false });
    el.addEventListener("touchend", onEnd);
    return () => {
      el.removeEventListener("touchstart", onStart);
      el.removeEventListener("touchmove", onMove);
      el.removeEventListener("touchend", onEnd);
    };
  }, []);

  function handleDoubleClick() {
    const base = baseScaleRef.current;
    if (resolvedScale > base * 1.05) {
      setScale(base); setPos({ x: 0, y: 0 });
    } else {
      setScale(base * 2.5);
    }
  }

  function handleMouseDown(e: React.MouseEvent) {
    if (resolvedScale <= baseScaleRef.current * 1.05) return;
    e.preventDefault();
    mouseStartRef.current = { mx: e.clientX, my: e.clientY, tx: pos.x, ty: pos.y };
    setIsDragging(true);
  }
  function handleMouseMove(e: React.MouseEvent) {
    if (!mouseStartRef.current) return;
    setPos({
      x: mouseStartRef.current.tx + (e.clientX - mouseStartRef.current.mx),
      y: mouseStartRef.current.ty + (e.clientY - mouseStartRef.current.my),
    });
  }
  function handleMouseUp() {
    mouseStartRef.current = null;
    setIsDragging(false);
  }

  function handleImgLoad(e: React.SyntheticEvent<HTMLImageElement>) {
    const img = e.currentTarget;
    const sw = (window.innerWidth * 0.88) / img.naturalWidth;
    const sh = (window.innerHeight * 0.88) / img.naturalHeight;
    const fitted = Math.min(sw, sh, 1); // never upscale beyond natural size
    baseScaleRef.current = fitted;
    setScale(fitted);
  }

  function handleBackdropClick() {
    const base = baseScaleRef.current;
    if (resolvedScale > base * 1.05) { setScale(base); setPos({ x: 0, y: 0 }); } else { onClose(); }
  }

  const isZoomed = resolvedScale > baseScaleRef.current * 1.05;

  return (
    <div
      ref={containerRef}
      className="lightbox"
      onClick={handleBackdropClick}
      onMouseMove={handleMouseMove}
      onMouseUp={handleMouseUp}
      onMouseLeave={handleMouseUp}
      role="dialog"
      aria-modal="true"
    >
      <button
        className="lightbox__close"
        onClick={e => { e.stopPropagation(); onClose(); }}
        aria-label="关闭"
      >
        <X size={18} strokeWidth={2} />
      </button>
      {isZoomed && (
        <div className="lightbox__tip">双击 / 点击背景 重置 · ESC 关闭</div>
      )}
      <img
        className="lightbox__img"
        src={url}
        alt=""
        style={{
          transform: `translate(${pos.x}px, ${pos.y}px) scale(${resolvedScale})`,
          cursor: isZoomed ? (isDragging ? "grabbing" : "grab") : "zoom-in",
          transition: isDragging ? "opacity 0.2s ease" : "transform 0.18s ease, opacity 0.2s ease",
          opacity: scale === null ? 0 : 1,
        }}
        onClick={e => e.stopPropagation()}
        onDoubleClick={handleDoubleClick}
        onMouseDown={handleMouseDown}
        onLoad={handleImgLoad}
        draggable={false}
      />
    </div>
  );
}

// ── Image grid inside user bubble ─────────────────────────────────────────────

function MessageImageGrid({ urls, onPreview }: { urls: string[]; onPreview: (url: string) => void }) {
  const count = urls.length;
  return (
    <div className={`msg-images msg-images--${Math.min(count, 4)}`}>
      {urls.slice(0, 4).map((url, i) => (
        <button
          key={i}
          type="button"
          className="msg-images__thumb"
          onClick={() => onPreview(url)}
          aria-label="预览图片"
        >
          <img src={url} alt="" draggable={false} />
          {i === 3 && count > 4 && (
            <span className="msg-images__overflow">+{count - 4}</span>
          )}
        </button>
      ))}
    </div>
  );
}

// ── Timeline ──────────────────────────────────────────────────────────────────

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
  const [lightboxUrl, setLightboxUrl] = useState<string | null>(null);

  const openLightbox = useCallback((url: string) => setLightboxUrl(url), []);
  const closeLightbox = useCallback(() => setLightboxUrl(null), []);

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

  useEffect(() => {
    const el = bottomRef.current;
    if (el && typeof el.scrollIntoView === "function") {
      el.scrollIntoView({ behavior: "smooth" });
    }
  }, [messages.length, isStreaming]);

  return (
    <>
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
            groupMessages(messages).map((item, idx) => {
              if (item.kind === "tool_group") {
                return <ToolBand key={`tg-${idx}`} items={item.items} />;
              }

              const message = item.msg;

              if (message.role === "user") {
                return (
                  <article key={message.id} className={`message message--user${message.isHistory ? " message--history" : ""}`}>
                    {message.mediaUrls && message.mediaUrls.length > 0 && (
                      <MessageImageGrid urls={message.mediaUrls} onPreview={openLightbox} />
                    )}
                    {message.text && <p className="message__text">{message.text}</p>}
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
                      <div className="msg-koda-label">
                        <span className="msg-koda-label__avatar">KC</span>
                        <span className="message__role">{text.roles.assistant}</span>
                      </div>
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

              if (message.role === "system") {
                return (
                  <div key={message.id} className="system-note">
                    <span className="system-note__text">{message.text}</span>
                  </div>
                );
              }

              // error
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

      {lightboxUrl && <ImageLightbox url={lightboxUrl} onClose={closeLightbox} />}
    </>
  );
}
