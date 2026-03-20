import type { FormEvent } from "react";
import { useLocaleText } from "../i18n/I18nProvider";

type BootstrapPanelProps = {
  needsBootstrap: boolean;
  detail?: string;
  identityMarkdown: string;
  soulMarkdown: string;
  userMarkdown: string;
  archiveBootstrapFile: boolean;
  isGeneratingDraft: boolean;
  isSubmitting: boolean;
  draftSummary?: string | null;
  error?: string | null;
  success?: string | null;
  onIdentityChange: (next: string) => void;
  onSoulChange: (next: string) => void;
  onUserChange: (next: string) => void;
  onArchiveBootstrapChange: (next: boolean) => void;
  onGenerateDraft: () => void;
  onSubmit: () => void;
};

export function BootstrapPanel({
  needsBootstrap,
  detail,
  identityMarkdown,
  soulMarkdown,
  userMarkdown,
  archiveBootstrapFile,
  isGeneratingDraft,
  isSubmitting,
  draftSummary,
  error,
  success,
  onIdentityChange,
  onSoulChange,
  onUserChange,
  onArchiveBootstrapChange,
  onGenerateDraft,
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
      guidanceCopy: "把第一轮引导对话整理成可持久化的 Markdown，写入 Koda 的身份设定、灵魂准则与用户协作边界。",
      archivedCopy: "工作区已经越过引导阶段，这里保留的是最初协作契约的可追踪记录。",
      identityLabel: "Koda 身份档案",
      identityHint: "沉淀名称、角色、语气，以及引导完成后仍应持续生效的核心特征。",
      soulLabel: "Koda 灵魂准则",
      soulHint: "记录长期行为准则、价值取向，以及在模糊场景下优先遵守的决策原则。",
      userLabel: "用户协作画像",
      userHint: "记录工作方式、边界、偏好的沟通节奏，以及什么样的结果才算成功。",
      archiveToggle: "完成后归档 `BOOTSTRAP.md`",
      generate: "根据对话生成草稿",
      generating: "正在生成草稿…",
      summaryLabel: "草稿摘要",
      step1: "先通过对话验证引导假设是否稳固。",
      step2: "根据对话生成身份、灵魂与用户画像的草稿。",
      step3: "将 Markdown 打磨成长期可复用的协作说明书。",
      step4: "提交引导结果，并让工作台切换到主控模式。",
      complete: "完成引导",
      completing: "正在提交引导…",
      archivedStep1: "身份设定与用户说明已经写入工作区。",
      archivedStepSoul: "灵魂准则已经写入工作区，可作为长期行为约束。",
      archivedStep2: "工作台现在会直接进入主控对话视图。",
      archivedStep3: "后续迭代会继续补齐引导修订历史与回放能力。",
      archivedButton: "引导契约已存档",
    },
    en: {
      eyebrow: "Onboarding",
      guidanceTitle: "Bootstrap Guidance",
      archivedTitle: "Bootstrap Archived",
      guidanceCopy: "Translate the first bootstrap conversation into durable markdown for Koda's identity, soul, and the user's operating boundaries.",
      archivedCopy: "Workspace has already crossed bootstrap. This panel remains as a traceable record of the initial contract.",
      identityLabel: "Koda identity",
      identityHint: "Capture name, role, tone, and the traits that should persist after onboarding.",
      soulLabel: "Koda soul",
      soulHint: "Record enduring principles, values, and the behavior rules Koda should prefer in ambiguous situations.",
      userLabel: "User profile",
      userHint: "Record working style, boundaries, preferred communication, and what success should feel like.",
      archiveToggle: "Archive `BOOTSTRAP.md` after completion",
      generate: "Generate draft from chat",
      generating: "Generating draft...",
      summaryLabel: "Draft summary",
      step1: "Use the chat lane to pressure-test the onboarding assumptions.",
      step2: "Generate identity, soul, and user drafts from the conversation.",
      step3: "Refine the markdown until it reads like a durable operating brief.",
      step4: "Commit the bootstrap and reopen the desk in main mode.",
      complete: "Complete bootstrap",
      completing: "Committing bootstrap...",
      archivedStep1: "Identity and user guidance have been written to the workspace.",
      archivedStepSoul: "Soul guidance has been written to the workspace as a durable behavior contract.",
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
            <span className="bootstrap-form__label">{text.soulLabel}</span>
            <span className="bootstrap-form__hint">
              {text.soulHint}
            </span>
            <textarea
              data-testid="bootstrap-soul-input"
              className="bootstrap-form__textarea"
              value={soulMarkdown}
              rows={8}
              disabled={isSubmitting}
              onChange={(event) => onSoulChange(event.target.value)}
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
              <li>{text.step4}</li>
            </ol>
            {draftSummary ? (
              <p
                className="bootstrap-panel__feedback bootstrap-panel__feedback--success"
                data-testid="bootstrap-draft-summary"
              >
                {text.summaryLabel}: {draftSummary}
              </p>
            ) : null}
            <button
              className="secondary-button"
              data-testid="bootstrap-generate-draft"
              type="button"
              disabled={isGeneratingDraft || isSubmitting}
              onClick={onGenerateDraft}
            >
              {isGeneratingDraft ? text.generating : text.generate}
            </button>
            <button
              className="bootstrap-form__submit"
              data-testid="bootstrap-submit"
              type="submit"
              disabled={
                isGeneratingDraft ||
                isSubmitting ||
                identityMarkdown.trim().length === 0 ||
                soulMarkdown.trim().length === 0 ||
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
            <li>{text.archivedStepSoul}</li>
            <li>{text.archivedStep2}</li>
          </ol>
          <button className="secondary-button" type="button" disabled>
            {text.archivedButton}
          </button>
        </>
      )}
    </section>
  );
}
