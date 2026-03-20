import type { FormEvent, KeyboardEvent } from "react";
import { useLocaleText } from "../i18n/I18nProvider";

type ChatComposerProps = {
  value: string;
  disabled?: boolean;
  placeholder: string;
  isStreaming: boolean;
  onChange: (next: string) => void;
  onSubmit: () => void;
};

export function ChatComposer({
  value,
  disabled,
  placeholder,
  isStreaming,
  onChange,
  onSubmit,
}: ChatComposerProps) {
  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    onSubmit();
  }

  function handleKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      onSubmit();
    }
  }

  const text = useLocaleText({
    zh: {
      label: "指令投递",
      live: "正在接收流式回复",
      ready: "准备发送",
      waiting: "正在等待 Gateway 快照完成同步。",
      hint: "Shift+Enter 换行，Enter 直接发送。",
      submit: "发送给 Koda",
      streaming: "流式输出中…",
    },
    en: {
      label: "Dispatch",
      live: "Live stream in progress",
      ready: "Ready to send",
      waiting: "Waiting for gateway snapshot before dispatch.",
      hint: "Shift+Enter for line breaks, Enter to submit from the desk.",
      submit: "Send to Koda",
      streaming: "Streaming…",
    },
  });

  return (
    <form className="composer" data-testid="chat-composer" onSubmit={handleSubmit}>
      <div className="composer__meta">
        <label className="composer__label" htmlFor="chat-input">
          {text.label}
        </label>
        <span className={`composer__status ${isStreaming ? "is-live" : ""}`}>
          {isStreaming ? text.live : text.ready}
        </span>
      </div>
      <textarea
        id="chat-input"
        name="message"
        data-testid="chat-input"
        className="composer__input"
        value={value}
        disabled={disabled || isStreaming}
        placeholder={placeholder}
        rows={4}
        onKeyDown={handleKeyDown}
        onChange={(event) => onChange(event.target.value)}
      />
      <div className="composer__actions">
        <p className="composer__hint">
          {disabled
            ? text.waiting
            : text.hint}
        </p>
        <button
          data-testid="chat-submit"
          className="composer__submit"
          type="submit"
          disabled={disabled || isStreaming || value.trim().length === 0}
        >
          {isStreaming ? text.streaming : text.submit}
        </button>
      </div>
    </form>
  );
}
