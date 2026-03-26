const LS_KEY = 'kc_diag_last_seen';
const SEEN_EVENT = 'kc:diag-seen';

export function getLastSeen(): number | null {
  try {
    const raw = localStorage.getItem(LS_KEY);
    if (raw === null) return null;
    const ts = parseInt(raw, 10);
    return isNaN(ts) ? null : ts;
  } catch {
    return null;
  }
}

export function markDiagAsSeen(): void {
  try {
    localStorage.setItem(LS_KEY, Date.now().toString());
    window.dispatchEvent(new CustomEvent(SEEN_EVENT));
  } catch {
    // ignore — storage may be unavailable
  }
}

export { SEEN_EVENT };
