import {
  createContext,
  type PropsWithChildren,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from "react";

export type AppLocale = "zh-CN" | "en-US";
type LocaleKey = "zh" | "en";
export type LocaleText<T> = Record<LocaleKey, T>;

type I18nContextValue = {
  locale: AppLocale;
  localeKey: LocaleKey;
  setLocale: (locale: AppLocale) => void;
  formatDateTime: (value?: string | number | Date | null, fallback?: string) => string;
  formatTime: (value: string | number | Date) => string;
};

const DEFAULT_LOCALE: AppLocale = "zh-CN";
const LOCALE_STORAGE_KEY = "kodaclaw.locale";
const SUPPORTED_LOCALES: AppLocale[] = ["zh-CN", "en-US"];

const I18nContext = createContext<I18nContextValue | null>(null);

function readStoredLocale(): AppLocale {
  if (typeof window === "undefined") {
    return DEFAULT_LOCALE;
  }

  const stored = window.localStorage.getItem(LOCALE_STORAGE_KEY);
  return stored === "en-US" || stored === "zh-CN" ? stored : DEFAULT_LOCALE;
}

function toLocaleKey(locale: AppLocale): LocaleKey {
  return locale === "en-US" ? "en" : "zh";
}

function normalizeDate(value?: string | number | Date | null): Date | null {
  if (value == null) {
    return null;
  }

  const next = value instanceof Date ? value : new Date(value);
  return Number.isNaN(next.getTime()) ? null : next;
}

export function I18nProvider({ children }: PropsWithChildren) {
  const [locale, setLocaleState] = useState<AppLocale>(() => readStoredLocale());

  useEffect(() => {
    if (typeof window !== "undefined") {
      window.localStorage.setItem(LOCALE_STORAGE_KEY, locale);
    }

    document.documentElement.lang = locale;
  }, [locale]);

  const setLocale = useCallback((nextLocale: AppLocale) => {
    if (SUPPORTED_LOCALES.includes(nextLocale)) {
      setLocaleState(nextLocale);
    }
  }, []);

  const formatDateTime = useCallback(
    (value?: string | number | Date | null, fallback?: string) => {
      const date = normalizeDate(value);
      if (!date) {
        return fallback ?? (locale === "zh-CN" ? "暂无" : "n/a");
      }

      return new Intl.DateTimeFormat(locale, {
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
      }).format(date);
    },
    [locale],
  );

  const formatTime = useCallback(
    (value: string | number | Date) => {
      const date = normalizeDate(value);
      if (!date) {
        return "--:--";
      }

      return new Intl.DateTimeFormat(locale, {
        hour: "2-digit",
        minute: "2-digit",
      }).format(date);
    },
    [locale],
  );

  const contextValue = useMemo<I18nContextValue>(
    () => ({
      locale,
      localeKey: toLocaleKey(locale),
      setLocale,
      formatDateTime,
      formatTime,
    }),
    [formatDateTime, formatTime, locale, setLocale],
  );

  return <I18nContext.Provider value={contextValue}>{children}</I18nContext.Provider>;
}

export function useI18n(): I18nContextValue {
  const context = useContext(I18nContext);
  if (!context) {
    throw new Error("useI18n must be used within I18nProvider.");
  }

  return context;
}

export function useLocaleText<T>(texts: LocaleText<T>): T {
  const { localeKey } = useI18n();
  return texts[localeKey];
}
