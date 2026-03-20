import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useLocaleText } from "../i18n/I18nProvider";
import {
  fetchBootstrapState,
  fetchGatewayHealth,
} from "../lib/api";
import type {
  BootstrapStateResponse,
  GatewayHealthResponse,
} from "../types/contracts";

export type ShellMode = "bootstrap" | "main";

export function useGatewaySnapshot() {
  const text = useLocaleText({
    zh: {
      fetchFailed: "获取 Gateway 快照失败。",
    },
    en: {
      fetchFailed: "Failed to fetch gateway snapshot.",
    },
  });
  const fallbackErrorTextRef = useRef(text.fetchFailed);
  const [health, setHealth] = useState<GatewayHealthResponse | null>(null);
  const [snapshot, setSnapshot] = useState<BootstrapStateResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fallbackErrorTextRef.current = text.fetchFailed;
  }, [text.fetchFailed]);

  const refresh = useCallback(async (signal?: AbortSignal) => {
    setIsLoading(true);
    setError(null);

    try {
      const [healthResult, snapshotResult] = await Promise.allSettled([
        fetchGatewayHealth(signal),
        fetchBootstrapState(signal),
      ]);

      if (healthResult.status === "fulfilled") {
        setHealth(healthResult.value);
      }

      if (snapshotResult.status === "fulfilled") {
        setSnapshot(snapshotResult.value);
      }

      const errors = [healthResult, snapshotResult]
        .filter((result): result is PromiseRejectedResult => result.status === "rejected")
        .map((result) => result.reason)
        .filter((reason) => (reason as Error).name !== "AbortError")
        .map((reason) =>
          reason instanceof Error ? reason.message : fallbackErrorTextRef.current,
        );

      if (errors.length > 0) {
        setError(errors.join(" | "));
      }
    } catch (nextError) {
      if ((nextError as Error).name === "AbortError") {
        return;
      }

      const detail =
        nextError instanceof Error
          ? nextError.message
          : fallbackErrorTextRef.current;
      setError(detail);
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void refresh(controller.signal);
    return () => controller.abort();
  }, [refresh]);

  const mode = useMemo<ShellMode>(() => {
    return snapshot?.mode === "Bootstrap" ? "bootstrap" : "main";
  }, [snapshot]);

  return {
    health,
    snapshot,
    isLoading,
    error,
    mode,
    refresh,
  };
}
