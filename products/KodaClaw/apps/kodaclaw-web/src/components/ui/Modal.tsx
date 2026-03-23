import { type ReactNode, useEffect, useRef } from "react";
import { X } from "lucide-react";
import "./Modal.css";

interface ModalProps {
  open: boolean;
  title: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  /** Width of the dialog panel, default 480px */
  width?: number | string;
}

export function Modal({ open, title, onClose, children, footer, width = 480 }: ModalProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const el = dialogRef.current;
    if (!el) return;
    if (open) {
      if (!el.open) el.showModal();
    } else {
      if (el.open) el.close();
    }
  }, [open]);

  // Close on native dialog cancel (Escape key)
  useEffect(() => {
    const el = dialogRef.current;
    if (!el) return;
    const handler = (e: Event) => {
      e.preventDefault();
      onClose();
    };
    el.addEventListener("cancel", handler);
    return () => el.removeEventListener("cancel", handler);
  }, [onClose]);

  // Close on backdrop click — when the user clicks the backdrop, the event
  // target IS the dialog element itself (not a child), so this check is reliable.
  function handleDialogClick(e: React.MouseEvent<HTMLDialogElement>) {
    if (e.target === e.currentTarget) {
      onClose();
    }
  }

  return (
    <dialog
      ref={dialogRef}
      className="kc-modal"
      style={{ width: typeof width === "number" ? `${width}px` : width }}
      onClick={handleDialogClick}
    >
      <div className="kc-modal__header">
        <h2 className="kc-modal__title">{title}</h2>
        <button type="button" className="kc-modal__close btn btn--ghost btn--sm" onClick={onClose} aria-label="关闭">
          <X size={16} />
        </button>
      </div>

      <div className="kc-modal__body">
        {children}
      </div>

      {footer && (
        <div className="kc-modal__footer">
          {footer}
        </div>
      )}
    </dialog>
  );
}
