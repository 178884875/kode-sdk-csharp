import { useEffect, useRef, useState } from "react";
import { fetchInbox } from "../lib/api";

const POLL_INTERVAL_MS = 30_000;

/**
 * Polls the Inbox API every 30 seconds and returns the count of open items.
 * Returns 0 when Gateway is unreachable or no items exist.
 */
export function useInboxUnreadCount(enabled: boolean): number {
  const [count, setCount] = useState(0);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (!enabled) {
      setCount(0);
      return;
    }

    let isDisposed = false;

    async function poll() {
      try {
        const payload = await fetchInbox({ status: "Open", limit: 50 });
        if (!isDisposed) {
          setCount(Array.isArray(payload.items) ? payload.items.length : 0);
        }
      } catch {
        // Gateway not reachable — keep previous count, don't clear it
      } finally {
        if (!isDisposed) {
          timerRef.current = setTimeout(poll, POLL_INTERVAL_MS);
        }
      }
    }

    void poll();

    return () => {
      isDisposed = true;
      if (timerRef.current !== null) {
        clearTimeout(timerRef.current);
        timerRef.current = null;
      }
    };
  }, [enabled]);

  return count;
}
