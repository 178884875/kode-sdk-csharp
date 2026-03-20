import { type DesktopLaunchTarget, isDesktopLaunchTarget } from "./desktop-shell-types";

type LaunchTargetInput = {
  argv?: string[];
  initialTargetJson?: string | null;
};

const KNOWN_DESKS = new Set([
  "chat",
  "inbox",
  "sessions",
  "models",
  "automations",
  "channels",
  "plugins",
  "canvas",
]);

function normalizeText(value: string | null | undefined): string | undefined {
  const next = value?.trim();
  return next ? next : undefined;
}

function normalizeRoutePath(route: string): string {
  const trimmed = route.trim();
  if (!trimmed) {
    return "/";
  }

  if (trimmed.startsWith("http://") || trimmed.startsWith("https://")) {
    const url = new URL(trimmed);
    return url.pathname || "/";
  }

  if (trimmed.startsWith("/")) {
    return trimmed;
  }

  return `/${trimmed}`;
}

function buildLaunchTarget(
  desk: string,
  input: {
    entityId?: string | null;
    route?: string | null;
    reason?: string | null;
  } = {},
): DesktopLaunchTarget | null {
  if (!KNOWN_DESKS.has(desk)) {
    return null;
  }

  return {
    desk,
    entityId: normalizeText(input.entityId),
    route: normalizeText(input.route),
    reason: normalizeText(input.reason),
  };
}

export function resolveLaunchTargetFromRoute(
  route: string | null | undefined,
  reason?: string | null,
): DesktopLaunchTarget | null {
  const nextRoute = normalizeText(route);
  if (!nextRoute) {
    return null;
  }

  const normalizedRoute = normalizeRoutePath(nextRoute);
  const segments = normalizedRoute.split("/").filter(Boolean);
  const [first, second] = segments;
  if (!first) {
    return buildLaunchTarget("chat", { route: normalizedRoute, reason });
  }

  if (first === "settings") {
    return buildLaunchTarget("models", { route: normalizedRoute, reason });
  }

  if (!KNOWN_DESKS.has(first)) {
    return null;
  }

  return buildLaunchTarget(first, {
    entityId: second,
    route: normalizedRoute,
    reason,
  });
}

export function resolveLaunchTargetFromProtocolUrl(
  rawUrl: string | null | undefined,
  reason?: string | null,
): DesktopLaunchTarget | null {
  const nextUrl = normalizeText(rawUrl);
  if (!nextUrl) {
    return null;
  }

  try {
    const url = new URL(nextUrl);
    if (url.protocol !== "kodaclaw:") {
      return null;
    }

    const explicitDesk = normalizeText(url.searchParams.get("desk"));
    const entityId = normalizeText(url.searchParams.get("entityId"));
    const route = normalizeText(url.searchParams.get("route"));
    const urlReason = normalizeText(url.searchParams.get("reason")) ?? reason ?? "protocol";

    if (explicitDesk) {
      return buildLaunchTarget(explicitDesk, {
        entityId,
        route,
        reason: urlReason,
      });
    }

    if (url.hostname && url.hostname !== "open") {
      return buildLaunchTarget(url.hostname, {
        entityId,
        route: route ?? url.pathname,
        reason: urlReason,
      });
    }

    if (route) {
      return resolveLaunchTargetFromRoute(route, urlReason);
    }

    const fromPath = url.pathname.replace(/^\/+/, "");
    if (fromPath) {
      return resolveLaunchTargetFromRoute(`/${fromPath}`, urlReason);
    }
  } catch {
    return null;
  }

  return null;
}

export function resolveLaunchTargetFromArgv(
  argv: string[],
  reason = "launch-arg",
): DesktopLaunchTarget | null {
  for (const arg of argv) {
    const routePrefix = "--kodaclaw-route=";
    if (arg.startsWith(routePrefix)) {
      return resolveLaunchTargetFromRoute(arg.slice(routePrefix.length), reason);
    }

    const targetPrefix = "--kodaclaw-target=";
    if (arg.startsWith(targetPrefix)) {
      try {
        const parsed = JSON.parse(arg.slice(targetPrefix.length)) as unknown;
        if (!isDesktopLaunchTarget(parsed)) {
          continue;
        }

        return {
          desk: parsed.desk,
          entityId: normalizeText(parsed.entityId),
          route: normalizeText(parsed.route),
          reason: normalizeText(parsed.reason) ?? reason,
        };
      } catch {
        continue;
      }
    }

    const protocolTarget = resolveLaunchTargetFromProtocolUrl(arg, reason);
    if (protocolTarget) {
      return protocolTarget;
    }
  }

  return null;
}

export function resolveInitialLaunchTarget(input: LaunchTargetInput): DesktopLaunchTarget | null {
  const initialTargetJson = normalizeText(input.initialTargetJson);
  if (initialTargetJson) {
    try {
      const parsed = JSON.parse(initialTargetJson) as unknown;
      if (isDesktopLaunchTarget(parsed)) {
        return {
          desk: parsed.desk,
          entityId: normalizeText(parsed.entityId),
          route: normalizeText(parsed.route),
          reason: normalizeText(parsed.reason) ?? "env",
        };
      }
    } catch {
      // fall through to argv parsing
    }
  }

  return resolveLaunchTargetFromArgv(input.argv ?? [], "startup");
}
