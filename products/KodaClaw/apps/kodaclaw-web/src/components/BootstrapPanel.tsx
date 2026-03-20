import type { FormEvent } from "react";
import { useLocaleText } from "../i18n/I18nProvider";

type BootstrapPanelProps = {
  needsBootstrap: boolean;
  detail?: string;
  identityMarkdown: string;
  userMarkdown: string;
  archiveBootstrapFile: boolean;
  isSubmitting: boolean;
  error?: string | null;
  success?: string | null;
  onIdentityChange: (next: string) => void;
  onUserChange: (next: string) => void;
  onArchiveBootstrapChange: (next: boolean) => void;
  onSubmit: () => void;
};

export function BootstrapPanel({
  needsBootstrap,
  detail,
  identityMarkdown,
  userMarkdown,
  archiveBootstrapFile,
  isSubmitting,
  error,
  success,
  onIdentityChange,
  onUserChange,
  onArchiveBootstrapChange,
  onSubmit,
}: BootstrapPanelProps) {
  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    onSubmit();
  }

  const text = useLocaleText({
    zh: {
      eyebrow: "引导档案",
      guidanceTitle: "引导编排",
      archivedTitle: "引导已归档",
      guidanceCopy: "把第一轮引导对话整理成可持久化的 Markdown，写入 Koda 的身份设定与用户的协作边界。",
      archivedCopy: "工作区已经越过引导阶段，这里保留的是最初协作契约的可追踪记录。",
      identityLabel: "Koda 身份档案",
      identityHint: "沉淀名称、角色、语气，以及引导完成后仍应持续生效的核心特征。",
      userLabel: "用户协作画像",
      userHint: "记录工作方式、边界、偏好的沟通节奏，以及什么样的结果才算成功。",
      archiveToggle: "完成后归档 `BOOTSTRAP.md`",
      step1: "先通过对话验证引导假设是否稳固。",
      step2: "将 Markdown 打磨成长期可复用的协作说明书。",
      step3: "提交引导结果，并让工作台切换到主控模式。",
      complete: "完成引导",
      completing: "正在提交引导…",
      archivedStep1: "身份设定与用户说明已经写入工作区。",
      archivedStep2: "工作台现在会直接进入主控对话视图。",
      archivedStep3: "后续迭代会继续补齐引导修订历史与回放能力。",
      archivedButton: "引导契约已存档",
    },
    en: {
      eyebrow: "Onboarding",
      guidanceTitle: "Bootstrap Guidance",
      archivedTitle: "Bootstrap Archived",
      guidanceCopy: "Translate the first bootstrap conversation into durable markdown for Koda's identity and the user's operating boundaries.",
      archivedCopy: "Workspace has already crossed bootstrap. This panel remains as a traceable record of the initial contract.",
      identityLabel: "Koda identity",
      identityHint: "Capture name, role, tone, and the traits that should persist after onboarding.",
      userLabel: "User profile",
      userHint: "Record working style, boundaries, preferred communication, and what success should feel like.",
      archiveToggle: "Archive `BOOTSTRAP.md` after completion",
      step1: "Use the chat lane to pressure-test the onboarding assumptions.",
      step2: "Refine the markdown until it reads like a durable operating brief.",
      step3: "Commit the bootstrap and reopen the desk in main mode.",
      complete: "Complete bootstrap",
      completing: "Committing bootstrap...",
      archivedStep1: "Identity and user guidance have been written to the workspace.",
      archivedStep2: "The desk now opens directly into main chat operations.",
      archivedStep3: "Future iterations will expose revision history and onboarding replay.",
      archivedButton: "Bootstrap contract stored",
    },
  });

  return (
    <section className="bootstrap-panel" data-testid="bootstrap-view" data-kc-mode="bootstrap">
      <div className="section-eyebrow">{text.eyebrow}</div>
      <h2 className="section-title" data-testid="bootstrap-panel-title">
        {needsBootstrap ? text.guidanceTitle : text.archivedTitle}
      </h2>
      <p className="section-copy">
        {needsBootstrap
          ? text.guidanceCopy
          : text.archivedCopy}
      </p>
      {detail ? <p className="bootstrap-panel__detail">{detail}</p> : null}

      {needsBootstrap ? (
        <form className="bootstrap-form" onSubmit={handleSubmit}>
          <label className="bootstrap-form__field">
            <span className="bootstrap-form__label">{text.identityLabel}</span>
            <span className="bootstrap-form__hint">
              {text.identityHint}
            </span>
            <textarea
              data-testid="bootstrap-identity-input"
              className="bootstrap-form__textarea"
              value={identityMarkdown}
              rows={8}
              disabled={isSubmitting}
              onChange={(event) => onIdentityChange(event.target.value)}
            />
          </label>

          <label className="bootstrap-form__field">
            <span className="bootstrap-form__label">{text.userLabel}</span>
            <span className="bootstrap-form__hint">
              {text.userHint}
            </span>
            <textarea
              data-testid="bootstrap-user-input"
              className="bootstrap-form__textarea"
              value={userMarkdown}
              rows={8}
              disabled={isSubmitting}
              onChange={(event) => onUserChange(event.target.value)}
            />
          </label>

          <label className="bootstrap-form__toggle">
            <input
              data-testid="bootstrap-archive-toggle"
              type="checkbox"
              checked={archiveBootstrapFile}
              disabled={isSubmitting}
              onChange={(event) => onArchiveBootstrapChange(event.target.checked)}
            />
            <span>{text.archiveToggle}</span>
          </label>

          {error ? (
            <p className="bootstrap-panel__feedback bootstrap-panel__feedback--error">{error}</p>
          ) : null}
          {success ? (
            <p className="bootstrap-panel__feedback bootstrap-panel__feedback--success">
              {success}
            </p>
          ) : null}

          <div className="bootstrap-form__actions">
            <ol className="bootstrap-panel__steps">
              <li>{text.step1}</li>
              <li>{text.step2}</li>
              <li>{text.step3}</li>
            </ol>
            <button
              className="bootstrap-form__submit"
              data-testid="bootstrap-submit"
              type="submit"
              disabled={
                isSubmitting ||
                identityMarkdown.trim().length === 0 ||
                userMarkdown.trim().length === 0
              }
            >
              {isSubmitting ? text.completing : text.complete}
            </button>
          </div>
        </form>
      ) : (
        <>
          <ol className="bootstrap-panel__steps">
            <li>{text.archivedStep1}</li>
            <li>{text.archivedStep2}</li>
            <li>{text.archivedStep3}</li>
          </ol>
          <button className="secondary-button" type="button" disabled>
            {text.archivedButton}
          </button>
        </>
      )}
    </section>
  );
}
