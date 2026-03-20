import { useLocaleText } from "../i18n/I18nProvider";

type ModeBadgeProps = {
  mode: "bootstrap" | "main";
};

export function ModeBadge({ mode }: ModeBadgeProps) {
  const text = useLocaleText({
    zh: {
      bootstrap: "引导模式",
      main: "主控对话",
    },
    en: {
      bootstrap: "Bootstrap Mode",
      main: "Main Chat",
    },
  });

  return (
    <span className={`mode-badge mode-badge--${mode}`}>
      {mode === "bootstrap" ? text.bootstrap : text.main}
    </span>
  );
}
