import { render, type RenderOptions } from "@testing-library/react";
import type { ReactElement } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { I18nProvider, type AppLocale } from "../i18n/I18nProvider";

type I18nRenderOptions = Omit<RenderOptions, "wrapper"> & {
  locale?: AppLocale;
};

export function renderWithI18n(ui: ReactElement, options?: I18nRenderOptions) {
  const { locale = "zh-CN", ...renderOptions } = options ?? {};
  window.localStorage.setItem("kodaclaw.locale", locale);
  document.documentElement.lang = locale;

  // 每个测试用独立的 QueryClient，避免缓存跨测试污染
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <I18nProvider>{ui}</I18nProvider>
    </QueryClientProvider>,
    renderOptions,
  );
}
