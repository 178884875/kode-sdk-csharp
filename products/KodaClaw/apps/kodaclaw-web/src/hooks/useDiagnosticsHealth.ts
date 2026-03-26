import { useCallback, useEffect, useState } from 'react';
import { fetchDiagnosticsStats } from '../lib/api';
import { getLastSeen, SEEN_EVENT } from '../lib/diagLastSeen';

const POLL_INTERVAL_MS = 30_000;
const WINDOW_MS = 3_600_000; // 1 hour

function effectiveSince(): string {
  const oneHourAgo = Date.now() - WINDOW_MS;
  const lastSeen = getLastSeen();
  const since = lastSeen !== null ? Math.max(oneHourAgo, lastSeen) : oneHourAgo;
  return new Date(since).toISOString();
}

export function useDiagnosticsHealth() {
  const [errorCount, setErrorCount] = useState(0);
  const [warningCount, setWarningCount] = useState(0);

  const poll = useCallback(async () => {
    try {
      const stats = await fetchDiagnosticsStats({ since: effectiveSince() });
      setErrorCount(stats.errorCount);
      setWarningCount(stats.warningCount);
    } catch {
      // ignore — keep last known values
    }
  }, []);

  useEffect(() => {
    void poll();
    const id = setInterval(poll, POLL_INTERVAL_MS);

    window.addEventListener(SEEN_EVENT, poll);
    return () => {
      clearInterval(id);
      window.removeEventListener(SEEN_EVENT, poll);
    };
  }, [poll]);

  return { errorCount, warningCount };
}
