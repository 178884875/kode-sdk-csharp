import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AlertTriangle } from 'lucide-react';
import { ChatComposer, type AttachedMedia } from './components/ChatComposer';
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
import { fetchOnboardingState, rotateSession, fetchModels, setDefaultModelEndpoint, uploadMedia } from './lib/api';
import type { ModelOption } from './components/ChatComposer';
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
    value === 'settings' || value === 'mcpServers';
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
  const prevActiveSessionIdRef = useRef<string | null | undefined>(undefined);

  // Called by useChatConsole when a workspace rotation happened during a turn.
  // We pre-update prevActiveSessionIdRef so the loadHistory effect doesn't fire when
  // the snapshot refreshes — the current turn's messages are already visible.
  const handleSessionRotated = useCallback((newSessionId: string) => {
    prevActiveSessionIdRef.current = newSessionId;
    refresh();
  }, [refresh]);

  const { draft, setDraft, isStreaming, activeToolName, messages, placeholder, sendMessage, appendSystemNote, clearMessages, submitApproval, loadHistory, loadMoreHistory, isLoadingHistory, hasMoreHistory } =
    useChatConsole(text.chat, handleSessionRotated);
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

  // Internal desk navigation via custom event (e.g. from RiskSummaryCard)
  useEffect(() => {
    function handler(e: Event) {
      const desk = (e as CustomEvent<{ desk: string }>).detail?.desk;
      if (isMainDesk(desk)) setMainDesk(desk);
    }
    window.addEventListener('kc:desk-navigate', handler);
    return () => window.removeEventListener('kc:desk-navigate', handler);
  }, []);

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

  // Gateway snapshot system notes — only show when not healthy (warning / error / unknown)
  useEffect(() => {
    if (!snapshot) return;
    const sig = `${health?.status ?? 'unknown'}|${snapshot.workspaceVersion}|${snapshot.workspaceRootPath}`;
    if (lastSnapshotSignature.current === sig) return;
    lastSnapshotSignature.current = sig;
    const tone = resolveHealthTone(health?.status ?? 'unknown');
    if (tone !== 'healthy') {
      appendSystemNote(text.notes.gatewaySnapshot(
        text.healthLabels[tone],
        snapshot.workspaceVersion,
        snapshot.workspaceRootPath,
      ));
    }
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
    clearMessages(text.chat.newSessionNote);
    refresh();
  }, [clearMessages, text.chat.newSessionNote, refresh]);

  // KC-BUG-301: pass sessionId to load history directly, don't depend on snapshot polling
  const handleResumeSession = useCallback((sessionId: string) => {
    clearMessages(text.chat.sessionResumedNote);
    loadHistory(sessionId);
    refresh();
  }, [clearMessages, text.chat.sessionResumedNote, loadHistory, refresh]);

  // When activeMainSessionId changes from one non-null value to another, load history.
  // Covers both resume (has history) and rotate (new session = 0 items, silent no-op).
  // Note: handleSessionRotated pre-updates prevActiveSessionIdRef to prevent loadHistory from
  // firing after a workspace-triggered rotation (those messages are already visible in the stream).
  useEffect(() => {
    const current = snapshot?.activeMainSessionId ?? null;
    const prev = prevActiveSessionIdRef.current;
    // undefined = initial render, skip to avoid loading on app start
    if (prev !== undefined && prev !== null && current !== null && current !== prev) {
      loadHistory(current);
    }
    prevActiveSessionIdRef.current = current;
  }, [snapshot?.activeMainSessionId, loadHistory]);

  const activeSessionId = snapshot?.activeMainSessionId ?? null;

  // KC-4403/4404: model info + attachment state
  const [modelName, setModelName] = useState<string | null>(null);
  const [modelCapabilities, setModelCapabilities] = useState<number>(0);
  const [selectedModelId, setSelectedModelId] = useState<string | null>(null);
  const [availableModels, setAvailableModels] = useState<ModelOption[]>([]);
  // KC-BUG-303: pending model switch confirm
  const [pendingModelChange, setPendingModelChange] = useState<{ id: string; displayName: string } | null>(null);
  const [attachedMedia, setAttachedMedia] = useState<AttachedMedia[]>([]);

  // Load available chat models once gateway is ready (snapshot present, not loading)
  const modelsLoadedRef = useRef(false);
  useEffect(() => {
    if (isLoading || !snapshot || modelsLoadedRef.current) return;
    modelsLoadedRef.current = true;
    const CAP_TEXT_CHAT = 1;
    fetchModels()
      .then(res => {
        const eligible = res.items.filter(m => m.enabled && (m.capabilities & CAP_TEXT_CHAT) !== 0);
        setAvailableModels(eligible.map(m => ({ id: m.id, displayName: m.displayName })));
        const ep = eligible.find(m => m.isDefault) ?? eligible[0];
        setModelName(ep?.displayName ?? null);
        setModelCapabilities(ep?.capabilities ?? 0);
        setSelectedModelId(ep?.id ?? null);
      })
      .catch(() => { modelsLoadedRef.current = false; }); // allow retry on error
  }, [isLoading, snapshot]);

  // KC-BUG-303: show confirm before rotating session on model change
  const handleModelChange = useCallback((modelId: string) => {
    if (modelId === selectedModelId) return;
    const candidate = availableModels.find(m => m.id === modelId);
    if (candidate) setPendingModelChange({ id: modelId, displayName: candidate.displayName });
  }, [selectedModelId, availableModels]);

  const handleModelChangeConfirm = useCallback(async () => {
    if (!pendingModelChange) return;
    const { id, displayName } = pendingModelChange;
    setPendingModelChange(null);
    try {
      const updated = await setDefaultModelEndpoint(id);
      setModelName(updated.displayName);
      setModelCapabilities(updated.capabilities);
      setSelectedModelId(updated.id);
    } catch { /* ignore, pill stays on old model */ return; }
    try { await rotateSession(); } catch { /* ignore */ }
    clearMessages(text.chat.modelSwitchedNote(displayName));
    refresh();
  }, [pendingModelChange, clearMessages, text.chat, refresh]);

  const handleModelChangeCancel = useCallback(() => {
    setPendingModelChange(null);
  }, []);

  const handleAttachMedia = useCallback(async (files: File[]) => {
    const placeholders: AttachedMedia[] = files.map(f => ({
      mediaId: `pending-${Date.now()}-${f.name}`,
      previewUrl: URL.createObjectURL(f),
      contentType: f.type,
      uploading: true,
    }));
    setAttachedMedia(prev => [...prev, ...placeholders]);
    for (let i = 0; i < files.length; i++) {
      const file = files[i];
      const placeholder = placeholders[i];
      try {
        const meta = await uploadMedia(file);
        setAttachedMedia(prev => prev.map(m =>
          m.mediaId === placeholder.mediaId
            ? { mediaId: meta.id, previewUrl: placeholder.previewUrl, contentType: meta.contentType, uploading: false }
            : m
        ));
      } catch {
        setAttachedMedia(prev => prev.filter(m => m.mediaId !== placeholder.mediaId));
      }
    }
  }, []);

  const handleRemoveMedia = useCallback((mediaId: string) => {
    setAttachedMedia(prev => prev.filter(m => m.mediaId !== mediaId));
  }, []);

  const handleChatSubmit = useCallback(() => {
    const ready = attachedMedia.filter(m => !m.uploading);
    const mediaIds = ready.map(m => m.mediaId);
    const mediaUrls = ready.map(m => m.previewUrl);
    sendMessage(mediaIds.length > 0 ? mediaIds : undefined, mediaUrls.length > 0 ? mediaUrls : undefined);
    setAttachedMedia([]);
  }, [attachedMedia, sendMessage]);

  const chatHeaderActions = useMemo(() => (
    <SessionHistoryPanel
      activeSessionId={activeSessionId}
      isStreaming={isStreaming}
      onResumed={handleResumeSession}
    />
  ), [activeSessionId, isStreaming, handleResumeSession]);

  // KC-BUG-303: model switch confirm banner
  const chatBanner = useMemo(() => {
    if (!pendingModelChange) return null;
    return (
      <div className="kc-chat-confirm-banner">
        <AlertTriangle size={14} strokeWidth={2} className="kc-chat-confirm-banner__icon" />
        <span className="kc-chat-confirm-banner__body">
          {text.chat.modelSwitchConfirmBody(pendingModelChange.displayName)}
        </span>
        <div className="kc-chat-confirm-banner__actions">
          <button className="kc-chat-confirm-banner__btn kc-chat-confirm-banner__btn--cancel" onClick={handleModelChangeCancel}>
            {text.chat.confirmCancel}
          </button>
          <button className="kc-chat-confirm-banner__btn kc-chat-confirm-banner__btn--ok" onClick={handleModelChangeConfirm}>
            {text.chat.confirmOk}
          </button>
        </div>
      </div>
    );
  }, [pendingModelChange, handleModelChangeCancel, handleModelChangeConfirm, text.chat]);

  const healthTone = resolveHealthTone(health?.status ?? 'unknown');

  const chatTimeline = useMemo(() => (
    <MessageTimeline
      messages={messages}
      isStreaming={isStreaming}
      onSubmitApproval={submitApproval}
      hasMoreHistory={hasMoreHistory}
      isLoadingHistory={isLoadingHistory}
      onLoadMoreHistory={loadMoreHistory}
    />
  ), [messages, isStreaming, submitApproval, hasMoreHistory, isLoadingHistory, loadMoreHistory]);

  const chatComposer = useMemo(() => (
    <ChatComposer
      value={draft}
      placeholder={placeholder}
      disabled={isLoading}
      isStreaming={isStreaming}
      activeToolName={activeToolName}
      onChange={setDraft}
      onSubmit={handleChatSubmit}
      modelName={modelName}
      modelCapabilities={modelCapabilities}
      selectedModelId={selectedModelId}
      availableModels={availableModels}
      onModelChange={handleModelChange}
      attachedMedia={attachedMedia}
      onAttachMedia={handleAttachMedia}
      onRemoveMedia={handleRemoveMedia}
    />
  ), [draft, placeholder, isLoading, isStreaming, activeToolName, setDraft, handleChatSubmit, modelName, modelCapabilities, selectedModelId, availableModels, handleModelChange, attachedMedia, handleAttachMedia, handleRemoveMedia]);

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
        chatBanner={chatBanner}
        sessionsFocusRequest={sessionsFocusRequest}
        onFocusRequestConsumed={() => setSessionsFocusRequest(null)}
        onOpenSessionDetail={handleOpenSessionDetail}
      />
    </AppShell>
  );
}
