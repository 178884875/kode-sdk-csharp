import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ChatComposer } from './components/ChatComposer';
import { MessageTimeline } from './components/MessageTimeline';
import { SessionHistoryPanel } from './components/chat/SessionHistoryPanel';
import { useChatConsole } from './hooks/useChatConsole';
import { useGatewaySnapshot } from './hooks/useGatewaySnapshot';
import { useTheme } from './hooks/useTheme';
import { useAppStrings } from './i18n/app-strings';
import {
  getInitialLaunchTarget,
  subscribeDesktopLaunchTargets,
  type DesktopDeskId,
  type DesktopLaunchTarget,
} from './lib/config';
import { fetchOnboardingState, rotateSession } from './lib/api';
import { OnboardingShell } from './onboarding/OnboardingShell';
import { AppShell } from './shell/AppShell';
import { MainContent } from './shell/MainContent';
import type { MainDesk } from './shell-shared/types';
import type { OnboardingState } from './types/contracts';

const MAIN_DESK_STORAGE_KEY = 'kodaclaw.mainDesk';

function resolveHealthTone(healthStatus: string): 'healthy' | 'warning' | 'error' | 'unknown' {
  const s = healthStatus.trim().toLowerCase();
  if (s === 'healthy' || s === 'ok') return 'healthy';
  if (s === 'degraded' || s === 'warning') return 'warning';
  if (s === 'unhealthy' || s === 'error' || s === 'failed') return 'error';
  return 'unknown';
}

function isMainDesk(value: DesktopDeskId | string | null | undefined): value is MainDesk {
  return value === 'chat' || value === 'inbox' || value === 'sessions' ||
    value === 'models' || value === 'automations' || value === 'channels' ||
    value === 'plugins' || value === 'canvas' || value === 'skills' ||
    value === 'settings';
}

function resolveMainDeskFromLaunchTarget(target: DesktopLaunchTarget | null): MainDesk | null {
  return target && isMainDesk(target.desk) ? target.desk : null;
}

function readStoredMainDesk(): MainDesk {
  if (typeof window === 'undefined') return 'chat';
  const initialTarget = getInitialLaunchTarget();
  if (initialTarget && isMainDesk(initialTarget.desk)) return initialTarget.desk as MainDesk;
  const stored = window.localStorage.getItem(MAIN_DESK_STORAGE_KEY);
  return isMainDesk(stored) ? stored : 'chat';
}

export default function App() {
  useTheme();
  const text = useAppStrings();
  const { health, snapshot, isLoading, error, refresh } = useGatewaySnapshot();
  const { draft, setDraft, isStreaming, messages, placeholder, sendMessage, appendSystemNote } =
    useChatConsole(text.chat);
  const [mainDesk, setMainDesk] = useState<MainDesk>(() => readStoredMainDesk());
  const [onboardingState, setOnboardingState] = useState<OnboardingState | null>(null);
  const [sessionsFocusRequest, setSessionsFocusRequest] = useState<{ sessionId: string; requestId: number } | null>(null);
  const sessionsFocusRequestId = useRef(0);
  const pendingLaunchTarget = useRef<DesktopLaunchTarget | null>(getInitialLaunchTarget());
  const lastSnapshotSignature = useRef<string | null>(null);
  const lastErrorSignature = useRef<string | null>(null);

  // Onboarding check
  useEffect(() => {
    if (isLoading) return;
    fetchOnboardingState()
      .then(state => { if (!state.isCompleted) setOnboardingState(state); })
      .catch(() => {});
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isLoading]);

  // Document title
  useEffect(() => {
    document.title = text.documentTitle;
    const desc = document.querySelector('meta[name="description"]');
    if (desc) desc.setAttribute('content', text.documentDescription);
  }, [text.documentTitle, text.documentDescription]);

  // Desktop launch targets
  useEffect(() => {
    return subscribeDesktopLaunchTargets(target => {
      pendingLaunchTarget.current = target;
      const nextDesk = resolveMainDeskFromLaunchTarget(target);
      if (!nextDesk) { pendingLaunchTarget.current = null; return; }
      setMainDesk(nextDesk);
      pendingLaunchTarget.current = null;
    });
  }, []);

  // Persist active desk
  useEffect(() => {
    window.localStorage.setItem(MAIN_DESK_STORAGE_KEY, mainDesk);
  }, [mainDesk]);

  // Gateway snapshot system notes
  useEffect(() => {
    if (!snapshot) return;
    const sig = `${health?.status ?? 'unknown'}|${snapshot.workspaceVersion}|${snapshot.workspaceRootPath}`;
    if (lastSnapshotSignature.current === sig) return;
    appendSystemNote(text.notes.gatewaySnapshot(
      text.healthLabels[resolveHealthTone(health?.status ?? 'unknown')],
      snapshot.workspaceVersion,
      snapshot.workspaceRootPath,
    ));
    lastSnapshotSignature.current = sig;
  }, [appendSystemNote, health?.status, snapshot, text.notes, text.healthLabels]);

  useEffect(() => {
    if (!error) return;
    if (lastErrorSignature.current === error) return;
    appendSystemNote(text.notes.gatewayError(error));
    lastErrorSignature.current = error;
  }, [appendSystemNote, error, text.notes]);

  const handleOpenSessionDetail = useCallback((sessionId: string) => {
    sessionsFocusRequestId.current += 1;
    setSessionsFocusRequest({ sessionId, requestId: sessionsFocusRequestId.current });
    setMainDesk('sessions');
  }, []);

  const handleRotateSession = useCallback(async () => {
    try { await rotateSession(); } catch { /* Gateway creates fresh session on next turn */ }
  }, []);

  const activeSessionId = snapshot?.activeMainSessionId ?? null;

  const chatHeaderActions = useMemo(() => (
    <SessionHistoryPanel
      activeSessionId={activeSessionId}
      onResumed={handleRotateSession}
    />
  ), [activeSessionId, handleRotateSession]);

  const healthTone = resolveHealthTone(health?.status ?? 'unknown');

  const chatTimeline = useMemo(() => (
    <MessageTimeline messages={messages} isStreaming={isStreaming} />
  ), [messages, isStreaming]);

  const chatComposer = useMemo(() => (
    <ChatComposer
      value={draft}
      placeholder={placeholder}
      disabled={isLoading}
      isStreaming={isStreaming}
      onChange={setDraft}
      onSubmit={sendMessage}
    />
  ), [draft, placeholder, isLoading, isStreaming, setDraft, sendMessage]);

  // Onboarding gate
  if (onboardingState && !onboardingState.isCompleted) {
    return (
      <OnboardingShell
        initialState={onboardingState}
        onComplete={() => setOnboardingState(prev => prev ? { ...prev, isCompleted: true } : null)}
      />
    );
  }

  return (
    <AppShell
      mainDesk={mainDesk}
      desks={text.desks}
      onDeskChange={setMainDesk}
      healthTone={healthTone}
      onRotateSession={handleRotateSession}
    >
      <MainContent
        mainDesk={mainDesk}
        chatTimeline={chatTimeline}
        chatComposer={chatComposer}
        chatHeaderActions={chatHeaderActions}
        sessionsFocusRequest={sessionsFocusRequest}
        onFocusRequestConsumed={() => setSessionsFocusRequest(null)}
        onOpenSessionDetail={handleOpenSessionDetail}
      />
    </AppShell>
  );
}
