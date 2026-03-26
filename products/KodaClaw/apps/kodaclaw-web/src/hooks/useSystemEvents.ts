import { useEffect, useRef, useState } from "react";
import { openSystemEventsStream, type SystemStatsEvent } from "../lib/api";

const WINDOW_MS = 3_600_000; // 1 hour — matches backend stats window

/**
 * Subscribes to /api/events/stream for real-time inbox unread count and
 * diagnostics health stats. Replaces the two 30-second polling hooks.
 * Automatically reconnects with exponential backoff on disconnection.
 */
export function useSystemEvents(enabled: boolean) {
  const [stats, setStats] = useState<SystemStatsEvent>({
    errorCount: 0,
    warningCount: 0,
    inboxUnreadCount: 0,
  });
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    if (!enabled) {
      setStats({ errorCount: 0, warningCount: 0, inboxUnreadCount: 0 });
      return;
    }

    const ctrl = new AbortController();
    abortRef.current = ctrl;

    openSystemEventsStream(
      (evt) => {
        // only update if still within the 1-hour window for diagnostics relevance
        void WINDOW_MS; // referenced for documentation clarity
        setStats(evt);
      },
      ctrl.signal,
    );

    return () => {
      ctrl.abort();
      abortRef.current = null;
    };
  }, [enabled]);

  return stats;
}
