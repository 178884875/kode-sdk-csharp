import { useCallback, useEffect, useRef, useState } from 'react';
import { fetchSessions, resumeSession } from '../lib/api';
import type { SessionSummary } from '../types/contracts';

export type SessionHistoryState = {
  sessions: SessionSummary[];
  isLoading: boolean;
  error: string | null;
  isResuming: boolean;
  resumeError: string | null;
  resume: (sessionId: string) => Promise<void>;
  load: () => void;
};

export function useSessionHistory(onResumed?: (sessionId: string) => void): SessionHistoryState {
  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isResuming, setIsResuming] = useState(false);
  const [resumeError, setResumeError] = useState<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  const load = useCallback(() => {
    abortRef.current?.abort();
    const ctrl = new AbortController();
    abortRef.current = ctrl;
    setIsLoading(true);
    setError(null);
    fetchSessions(20, ctrl.signal)
      .then(data => {
        if (!ctrl.signal.aborted) {
          setSessions(data.sessions.filter(s => s.sessionKind === 'Main'));
          setIsLoading(false);
        }
      })
      .catch(err => {
        if (!ctrl.signal.aborted) {
          setError(err instanceof Error ? err.message : 'Failed to load sessions');
          setIsLoading(false);
        }
      });
  }, []);

  useEffect(() => {
    return () => { abortRef.current?.abort(); };
  }, []);

  const resume = useCallback(async (sessionId: string) => {
    setIsResuming(true);
    setResumeError(null);
    try {
      await resumeSession(sessionId);
      onResumed?.(sessionId);
      load();
    } catch (err) {
      setResumeError(err instanceof Error ? err.message : 'Failed to resume session');
    } finally {
      setIsResuming(false);
    }
  }, [load, onResumed]);

  return { sessions, isLoading, error, isResuming, resumeError, resume, load };
}
