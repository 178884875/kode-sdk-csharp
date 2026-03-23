import { useState } from 'react';
import { CheckCircle, XCircle, Clock, Wrench, ChevronDown } from 'lucide-react';
import { useLocaleText } from '../../i18n/I18nProvider';
import type { ApprovalDecision } from '../../types/chat';

type Props = {
  approvalId: string;
  toolName: string;
  inputPreview?: string | null;
  decision: ApprovalDecision;
  onApprove: (approvalId: string) => void;
  onReject: (approvalId: string) => void;
};

export function ApprovalCard({ approvalId, toolName, inputPreview, decision, onApprove, onReject }: Props) {
  const [previewExpanded, setPreviewExpanded] = useState(false);

  const text = useLocaleText({
    zh: {
      title: '工具调用待审批',
      tool: '工具',
      params: '参数预览',
      approve: '允许',
      reject: '拒绝',
      approved: '已允许',
      rejected: '已拒绝',
      pending: '等待审批',
      showParams: '展开参数',
      hideParams: '收起参数',
    },
    en: {
      title: 'Tool call pending approval',
      tool: 'Tool',
      params: 'Parameters',
      approve: 'Allow',
      reject: 'Deny',
      approved: 'Allowed',
      rejected: 'Denied',
      pending: 'Awaiting approval',
      showParams: 'Show params',
      hideParams: 'Hide params',
    },
  });

  const isPending = decision === 'pending';

  // Decided cards collapse to a compact pill
  if (!isPending) {
    const isApproved = decision === 'approved';
    return (
      <div
        className={`approval-pill approval-pill--${decision}`}
        data-testid="approval-card"
        data-approval-id={approvalId}
      >
        {isApproved
          ? <CheckCircle size={12} strokeWidth={2} aria-hidden="true" />
          : <XCircle size={12} strokeWidth={2} aria-hidden="true" />
        }
        <code className="approval-pill__tool">{toolName}</code>
        <span className="approval-pill__label">{isApproved ? text.approved : text.rejected}</span>
      </div>
    );
  }

  return (
    <article className="approval-card" data-testid="approval-card" data-approval-id={approvalId}>
      <header className="approval-card__header">
        <Wrench size={14} strokeWidth={1.75} className="approval-card__icon" aria-hidden="true" />
        <span className="approval-card__title">{text.title}</span>
        <span className="approval-card__status approval-card__status--pending">
          <Clock size={13} strokeWidth={1.75} aria-hidden="true" />
          {text.pending}
        </span>
      </header>

      <div className="approval-card__body">
        <div className="approval-card__row">
          <span className="approval-card__label">{text.tool}</span>
          <code className="approval-card__tool-name">{toolName}</code>
          {inputPreview && (
            <button
              type="button"
              className="approval-card__toggle"
              onClick={() => setPreviewExpanded(v => !v)}
              aria-expanded={previewExpanded}
            >
              <ChevronDown
                size={12}
                strokeWidth={2}
                className={`approval-card__toggle-icon${previewExpanded ? ' approval-card__toggle-icon--open' : ''}`}
                aria-hidden="true"
              />
              {previewExpanded ? text.hideParams : text.showParams}
            </button>
          )}
        </div>
        {inputPreview && previewExpanded && (
          <div className="approval-card__row approval-card__row--params">
            <span className="approval-card__label">{text.params}</span>
            <pre className="approval-card__preview">{inputPreview}</pre>
          </div>
        )}
      </div>

      <footer className="approval-card__actions">
        <button
          type="button"
          className="approval-card__btn approval-card__btn--approve"
          data-testid="approval-allow-btn"
          onClick={() => onApprove(approvalId)}
        >
          <CheckCircle size={13} strokeWidth={1.75} aria-hidden="true" />
          {text.approve}
        </button>
        <button
          type="button"
          className="approval-card__btn approval-card__btn--reject"
          data-testid="approval-reject-btn"
          onClick={() => onReject(approvalId)}
        >
          <XCircle size={13} strokeWidth={1.75} aria-hidden="true" />
          {text.reject}
        </button>
      </footer>
    </article>
  );
}
