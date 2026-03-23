import { Modal } from "./Modal";
import "./ConfirmModal.css";

export type ConfirmModalVariant = "danger" | "warning" | "default";

interface ConfirmModalProps {
  open: boolean;
  title: string;
  description?: string;
  confirmLabel?: string;
  cancelLabel?: string;
  variant?: ConfirmModalVariant;
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export function ConfirmModal({
  open,
  title,
  description,
  confirmLabel = "确认",
  cancelLabel = "取消",
  variant = "default",
  busy = false,
  onConfirm,
  onCancel,
}: ConfirmModalProps) {
  return (
    <Modal
      open={open}
      title={title}
      onClose={onCancel}
      width={400}
      footer={
        <>
          <button
            type="button"
            className="btn btn--ghost"
            onClick={onCancel}
            disabled={busy}
          >
            {cancelLabel}
          </button>
          <button
            type="button"
            className={`btn confirm-modal__confirm-btn confirm-modal__confirm-btn--${variant}`}
            onClick={onConfirm}
            disabled={busy}
          >
            {busy ? "处理中…" : confirmLabel}
          </button>
        </>
      }
    >
      {description && (
        <p className="confirm-modal__description">{description}</p>
      )}
    </Modal>
  );
}
