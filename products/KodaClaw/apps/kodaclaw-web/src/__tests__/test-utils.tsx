import { render, type RenderOptions } from "@testing-library/react";
import type { ReactElement } from "react";
import { I18nProvider, type AppLocale } from "../i18n/I18nProvider";

type I18nRenderOptions = Omit<RenderOptions, "wrapper"> & {
  locale?: AppLocale;
};

export function renderWithI18n(ui: ReactElement, options?: I18nRenderOptions) {
  const { locale = "zh-CN", ...renderOptions } = options ?? {};
  window.localStorage.setItem("kodaclaw.locale", locale);
  document.documentElement.lang = locale;

  return render(<I18nProvider>{ui}</I18nProvider>, renderOptions);
}
