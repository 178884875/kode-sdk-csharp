import { useEffect, useRef, useState } from "react";
import type { FormEvent, KeyboardEvent, ClipboardEvent } from "react";
import { ArrowUp, Check, ChevronDown, Paperclip, X } from "lucide-react";
import { useLocaleText } from "../i18n/I18nProvider";

const CAP_TEXT_CHAT = 1 << 0; // 1
const CAP_VISION = 1 << 2;    // 4

export type AttachedMedia = {
  mediaId: string;
  previewUrl: string;
  contentType: string;
  uploading?: boolean;
};

export type ModelOption = { id: string; displayName: string };

type ChatComposerProps = {
  value: string;
  disabled?: boolean;
  placeholder: string;
  isStreaming: boolean;
  activeToolName?: string | null;
  onChange: (next: string) => void;
  onSubmit: () => void;
  // KC-4403: model pill
  modelName?: string | null;
  modelCapabilities?: number;
  selectedModelId?: string | null;
  availableModels?: ModelOption[];
  onModelChange?: (modelId: string) => void;
  // KC-4404: vision attachment
  attachedMedia?: AttachedMedia[];
  onAttachMedia?: (files: File[]) => void;
  onRemoveMedia?: (mediaId: string) => void;
};

export function ChatComposer({
  value,
  disabled,
  placeholder,
  isStreaming,
  activeToolName,
  onChange,
  onSubmit,
  modelName,
  modelCapabilities,
  selectedModelId,
  availableModels,
  onModelChange,
  attachedMedia,
  onAttachMedia,
  onRemoveMedia,
}: ChatComposerProps) {
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const pillWrapRef = useRef<HTMLDivElement>(null);
  const [dropdownOpen, setDropdownOpen] = useState(false);

  const supportsVision =
    onAttachMedia != null &&
    modelCapabilities != null &&
    (modelCapabilities & CAP_TEXT_CHAT) !== 0 &&
    (modelCapabilities & CAP_VISION) !== 0;

  // Close model dropdown on outside click
  useEffect(() => {
    if (!dropdownOpen) return;
    function handleOutside(e: MouseEvent) {
      if (pillWrapRef.current && !pillWrapRef.current.contains(e.target as Node)) {
        setDropdownOpen(false);
      }
    }
    document.addEventListener("mousedown", handleOutside);
    return () => document.removeEventListener("mousedown", handleOutside);
  }, [dropdownOpen]);

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

  function handlePaste(event: ClipboardEvent<HTMLTextAreaElement>) {
    if (!supportsVision) return;
    const files = Array.from(event.clipboardData.files).filter((f) =>
      f.type.startsWith("image/"),
    );
    if (files.length > 0) {
      event.preventDefault();
      onAttachMedia!(files);
    }
  }

  function handleFileChange() {
    const files = Array.from(fileInputRef.current?.files ?? []);
    if (files.length > 0) onAttachMedia!(files);
    if (fileInputRef.current) fileInputRef.current.value = "";
  }

  const text = useLocaleText({
    zh: {
      live: (tool: string | null | undefined) => tool ? `Koda 正在执行 ${tool}…` : "正在接收回复…",
      waiting: "正在等待 Gateway 同步，请稍候。",
      hint: "Shift+Enter 换行",
      submit: "发送",
      attach: "附加图片",
    },
    en: {
      live: (tool: string | null | undefined) => tool ? `Koda is running ${tool}…` : "Receiving response…",
      waiting: "Waiting for gateway sync…",
      hint: "Shift+Enter for new line",
      submit: "Send",
      attach: "Attach image",
    },
  });

  const isSubmitDisabled =
    disabled ||
    isStreaming ||
    (value.trim().length === 0 && (!attachedMedia || attachedMedia.length === 0));

  return (
    <form className="composer" data-testid="chat-composer" onSubmit={handleSubmit}>
      {/* KC-4404: attachment preview bar */}
      {attachedMedia && attachedMedia.length > 0 && (
        <div className="composer__attachment-bar">
          {attachedMedia.map((m) => (
            <div key={m.mediaId} className="composer__attachment-thumb">
              {m.uploading ? (
                <div className="composer__attachment-spinner" />
              ) : (
                <img src={m.previewUrl} alt="" draggable={false} />
              )}
              {onRemoveMedia && !m.uploading && (
                <button
                  type="button"
                  className="composer__attachment-remove"
                  onClick={() => onRemoveMedia(m.mediaId)}
                  aria-label="Remove"
                >
                  <X size={10} strokeWidth={2.5} />
                </button>
              )}
            </div>
          ))}
        </div>
      )}

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
        onPaste={handlePaste}
        onChange={(event) => onChange(event.target.value)}
      />

      <div className="composer__actions">
        {/* KC-4403: model pill (switchable) */}
        <div className="composer__actions-left">
          {modelName && (
            <div ref={pillWrapRef} className="composer__model-pill-wrap">
              <button
                type="button"
                className="composer__model-pill"
                title={modelName}
                onClick={() => availableModels && availableModels.length > 1 && setDropdownOpen(o => !o)}
                disabled={!availableModels || availableModels.length <= 1}
              >
                <span className="composer__model-pill-name">{modelName}</span>
                {availableModels && availableModels.length > 1 && (
                  <ChevronDown
                    size={10}
                    strokeWidth={2.5}
                    className={dropdownOpen ? "composer__model-pill-chevron composer__model-pill-chevron--open" : "composer__model-pill-chevron"}
                  />
                )}
              </button>
              {dropdownOpen && availableModels && (
                <div className="composer__model-dropdown">
                  {availableModels.map(m => (
                    <button
                      key={m.id}
                      type="button"
                      className="composer__model-dropdown-item"
                      onClick={() => { onModelChange?.(m.id); setDropdownOpen(false); }}
                    >
                      <span className="composer__model-dropdown-item-name">{m.displayName}</span>
                      {m.id === selectedModelId && <Check size={12} strokeWidth={2.5} />}
                    </button>
                  ))}
                </div>
              )}
            </div>
          )}
          {/* KC-4404: attach button */}
          {supportsVision && (
            <>
              <input
                ref={fileInputRef}
                type="file"
                accept="image/*"
                multiple
                hidden
                onChange={handleFileChange}
              />
              <button
                type="button"
                className="composer__attach-btn"
                onClick={() => fileInputRef.current?.click()}
                disabled={disabled || isStreaming}
                aria-label={text.attach}
              >
                <Paperclip size={15} strokeWidth={2} />
              </button>
            </>
          )}
        </div>

        {(disabled || isStreaming) && (
          <p className="composer__hint">
            {disabled ? text.waiting : text.live(activeToolName)}
          </p>
        )}
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
