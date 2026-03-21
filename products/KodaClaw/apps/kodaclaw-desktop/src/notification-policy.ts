import { type DesktopLaunchTarget } from "./desktop-shell-types";
import { resolveLaunchTargetFromRoute } from "./launch-targets";

export type NotificationSettings = {
  notificationsEnabled: boolean;
  quietHoursEnabled: boolean;
  quietHoursStartLocalTime?: string | null;
  quietHoursEndLocalTime?: string | null;
};

export type NotificationApproval = {
  id: string;
  title: string;
  summary: string;
  updatedAt: string;
  status?: string | null;
  kind?: string | null;
  inboxItemId?: string | null;
};

export type NotificationInboxItem = {
  id: string;
  title: string;
  summary: string;
  updatedAt: string;
  approvalId?: string | null;
  requiresAction?: boolean;
  route?: string | null;
  status?: string | null;
};

export type DesktopNotificationCandidate = {
  id: string;
  title: string;
  body: string;
  updatedAt: string;
  target: DesktopLaunchTarget;
  signature: string;
  approvalId?: string | null;
  isChannelDelivery?: boolean;
};

function parseLocalMinutes(value: string | null | undefined): number | null {
  const next = value?.trim();
  if (!next) {
    return null;
  }

  const match = /^(\d{1,2}):(\d{2})$/.exec(next);
  if (!match) {
    return null;
  }

  const hours = Number.parseInt(match[1], 10);
  const minutes = Number.parseInt(match[2], 10);
  if (hours < 0 || hours > 23 || minutes < 0 || minutes > 59) {
    return null;
  }

  return hours * 60 + minutes;
}

export function isWithinQuietHours(
  settings: NotificationSettings,
  now = new Date(),
): boolean {
  if (!settings.notificationsEnabled || !settings.quietHoursEnabled) {
    return false;
  }

  const startMinutes = parseLocalMinutes(settings.quietHoursStartLocalTime);
  const endMinutes = parseLocalMinutes(settings.quietHoursEndLocalTime);
  if (startMinutes === null || endMinutes === null || startMinutes === endMinutes) {
    return false;
  }

  const currentMinutes = now.getHours() * 60 + now.getMinutes();
  if (startMinutes < endMinutes) {
    return currentMinutes >= startMinutes && currentMinutes < endMinutes;
  }

  return currentMinutes >= startMinutes || currentMinutes < endMinutes;
}

function buildApprovalCandidate(approval: NotificationApproval): DesktopNotificationCandidate {
  const target = resolveLaunchTargetFromRoute("/inbox", "approval") ?? { desk: "inbox", reason: "approval" };

  return {
    id: `approval:${approval.id}`,
    title: approval.title,
    body: approval.summary,
    updatedAt: approval.updatedAt,
    target,
    signature: `approval:${approval.id}:${approval.updatedAt}`,
    approvalId: approval.id,
    isChannelDelivery: approval.kind === "ChannelDelivery",
  };
}

function buildInboxCandidate(item: NotificationInboxItem): DesktopNotificationCandidate {
  const target = resolveLaunchTargetFromRoute(item.route ?? "/inbox", "inbox") ??
    { desk: "inbox", route: item.route ?? "/inbox", reason: "inbox" };

  return {
    id: `inbox:${item.id}`,
    title: item.title,
    body: item.summary,
    updatedAt: item.updatedAt,
    target,
    signature: `inbox:${item.id}:${item.updatedAt}`,
  };
}

export function buildNotificationCandidates(
  approvals: NotificationApproval[],
  inboxItems: NotificationInboxItem[],
): DesktopNotificationCandidate[] {
  const approvalLinkedInboxIds = new Set(
    approvals
      .map((approval) => approval.inboxItemId ?? null)
      .filter((value): value is string => Boolean(value)),
  );

  const approvalCandidates = approvals.map(buildApprovalCandidate);
  const inboxCandidates = inboxItems
    .filter((item) => !item.approvalId && !approvalLinkedInboxIds.has(item.id))
    .map(buildInboxCandidate);

  return [...approvalCandidates, ...inboxCandidates].sort((left, right) =>
    left.updatedAt.localeCompare(right.updatedAt));
}

export function filterUnseenCandidates(
  candidates: DesktopNotificationCandidate[],
  seenSignatures: Set<string>,
): DesktopNotificationCandidate[] {
  return candidates.filter((candidate) => !seenSignatures.has(candidate.signature));
}
