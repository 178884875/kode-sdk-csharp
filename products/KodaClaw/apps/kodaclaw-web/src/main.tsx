import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import App from "./App";
import { I18nProvider } from "./i18n/I18nProvider";
import { initializeRuntimeConfig } from "./lib/config";
import "./index.css";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Gateway 是本地 loopback，数据不会因网络原因过期，关闭窗口聚焦时自动 refetch
      refetchOnWindowFocus: false,
      retry: 1,
    },
  },
});

async function bootstrap(): Promise<void> {
  await initializeRuntimeConfig();

  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <QueryClientProvider client={queryClient}>
        <I18nProvider>
          <App />
        </I18nProvider>
      </QueryClientProvider>
    </StrictMode>,
  );
}

void bootstrap();
