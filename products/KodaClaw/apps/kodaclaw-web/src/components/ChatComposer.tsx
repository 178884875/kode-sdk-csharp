import { useEffect, useRef } from "react";
import type { FormEvent, KeyboardEvent } from "react";
import { ArrowUp } from "lucide-react";
import { useLocaleText } from "../i18n/I18nProvider";

type ChatComposerProps = {
  value: string;
  disabled?: boolean;
  placeholder: string;
  isStreaming: boolean;
  activeToolName?: string | null;
  onChange: (next: string) => void;
  onSubmit: () => void;
};

export function ChatComposer({
  value,
  disabled,
  placeholder,
  isStreaming,
  activeToolName,
  onChange,
  onSubmit,
}: ChatComposerProps) {
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  // Auto-grow textarea
  useEffect(() => {
    const el = textareaRef.current;
    if (!el) return;
    el.style.height = "auto";
    el.style.height = `${Math.min(el.scrollHeight, 160)}px`;
  }, [value]);

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
      live: (tool: string | null | undefined) => tool ? `Koda 正在执行 ${tool}…` : "正在接收回复…",
      waiting: "正在等待 Gateway 同步，请稍候。",
      hint: "Shift+Enter 换行",
      submit: "发送",
    },
    en: {
      live: (tool: string | null | undefined) => tool ? `Koda is running ${tool}…` : "Receiving response…",
      waiting: "Waiting for gateway sync…",
      hint: "Shift+Enter for new line",
      submit: "Send",
    },
  });

  const isSubmitDisabled = disabled || isStreaming || value.trim().length === 0;

  return (
    <form className="composer" data-testid="chat-composer" onSubmit={handleSubmit}>
      <textarea
        ref={textareaRef}
        id="chat-input"
        name="message"
        data-testid="chat-input"
        className="composer__input"
        value={value}
        disabled={disabled || isStreaming}
        placeholder={placeholder}
        rows={1}
        onKeyDown={handleKeyDown}
        onChange={(event) => onChange(event.target.value)}
      />
      <div className="composer__actions">
        <p className="composer__hint">
          {disabled ? text.waiting : isStreaming ? text.live(activeToolName) : text.hint}
        </p>
        <button
          data-testid="chat-submit"
          className="composer__submit"
          type="submit"
          disabled={isSubmitDisabled}
          aria-label={text.submit}
        >
          <ArrowUp size={16} strokeWidth={2.5} />
        </button>
      </div>
    </form>
  );
}
