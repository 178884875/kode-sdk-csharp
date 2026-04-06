import { useEffect } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { fetchDiagnosticsStats } from '../lib/api';
import { getLastSeen, SEEN_EVENT } from '../lib/diagLastSeen';
import { queryKeys } from '../lib/queryKeys';

const POLL_INTERVAL_MS = 30_000;
const WINDOW_MS = 3_600_000; // 1 hour

function effectiveSince(): string {
  const oneHourAgo = Date.now() - WINDOW_MS;
  const lastSeen = getLastSeen();
  const since = lastSeen !== null ? Math.max(oneHourAgo, lastSeen) : oneHourAgo;
  return new Date(since).toISOString();
}

export function useDiagnosticsHealth() {
  const queryClient = useQueryClient();
  const since = effectiveSince();
  const { data } = useQuery({
    queryKey: queryKeys.diagnosticsStats(since),
    queryFn: () => fetchDiagnosticsStats({ since }),
    refetchInterval: POLL_INTERVAL_MS,
    retry: false,
  });

  // Re-invalidate on SEEN_EVENT so unread count resets promptly
  useEffect(() => {
    const handler = () => {
      void queryClient.invalidateQueries({ queryKey: ['diagnosticsStats'] });
    };
    window.addEventListener(SEEN_EVENT, handler);
    return () => window.removeEventListener(SEEN_EVENT, handler);
  }, [queryClient]);

  return {
    errorCount: data?.errorCount ?? 0,
    warningCount: data?.warningCount ?? 0,
  };
}
