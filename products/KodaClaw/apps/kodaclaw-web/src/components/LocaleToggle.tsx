import { useI18n, useLocaleText, type AppLocale } from "../i18n/I18nProvider";

const OPTIONS: Array<{ locale: AppLocale; label: string; shortLabel: string }> = [
  { locale: "zh-CN", label: "中文", shortLabel: "CN" },
  { locale: "en-US", label: "English", shortLabel: "EN" },
];

export function LocaleToggle() {
  const { locale, setLocale } = useI18n();
  const text = useLocaleText({
    zh: {
      groupLabel: "语言切换",
    },
    en: {
      groupLabel: "Language switcher",
    },
  });

  return (
    <div className="locale-toggle" role="group" aria-label={text.groupLabel} data-testid="locale-toggle">
      {OPTIONS.map((option) => (
        <button
          key={option.locale}
          type="button"
          className={`locale-toggle__button ${locale === option.locale ? "locale-toggle__button--active" : ""}`}
          data-testid={`locale-toggle-${option.locale}`}
          aria-pressed={locale === option.locale}
          lang={option.locale}
          onClick={() => setLocale(option.locale)}
        >
          <span className="locale-toggle__short">{option.shortLabel}</span>
          <span>{option.label}</span>
        </button>
      ))}
    </div>
  );
}
