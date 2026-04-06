import { useQuery } from "@tanstack/react-query";
import { fetchInbox } from "../lib/api";
import { queryKeys } from "../lib/queryKeys";

/**
 * Polls the Inbox API every 30 seconds and returns the count of open items.
 * Returns 0 when Gateway is unreachable or no items exist.
 */
export function useInboxUnreadCount(enabled: boolean): number {
  const { data } = useQuery({
    queryKey: queryKeys.inbox("Open"),
    queryFn: () => fetchInbox({ status: "Open", limit: 50 }),
    enabled,
    refetchInterval: 30_000,
    // On error keep previous data
    retry: false,
  });

  if (!enabled) return 0;
  return Array.isArray(data?.items) ? data.items.length : 0;
}
