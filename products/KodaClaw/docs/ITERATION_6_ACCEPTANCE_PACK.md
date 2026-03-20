# Iteration 6 Acceptance Pack

Last updated: 2026-03-19

## Goal

Freeze the acceptance evidence for Iteration 6 (`KC-0601` ~ `KC-0608`):

- `kodaclaw-desktop` is a real Electron shell around the existing `kodaclaw-web`
- desktop supports both `AttachOnly` and `ManagedChild` Gateway lifecycle modes
- renderer runtime config comes from the desktop preload/main bridge instead of Vite env only
- tray/menu, hide-on-close, restore semantics, and the global shortcut stay stable
- notification polling reuses `/api/settings`, `/api/approvals`, and `/api/inbox`, respects quiet hours, and avoids duplicate approval/inbox alerts
- startup args, protocol payloads, tray actions, and notification clicks all converge on the same desktop launch-target routing model
- macOS-first packaging smoke remains green after shell integration
- all existing backend/frontend regressions remain green after the desktop slices land

## Automated Acceptance Matrix

| Layer | Coverage | Evidence |
| --- | --- | --- |
| `L0` | Solution restore/build/test baseline | `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` |
| `L1` | Desktop pure helper coverage for launch-target parsing and quiet-hours/notification dedupe policy | `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run test` |
| `L2` | Desktop Wave 3 fixture smoke: notification polling + dedupe + startup route mapping | `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run smoke:wave3` |
| `L2` | Desktop managed-child smoke: Gateway launch, health wait, tray/shortcut readiness | `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run smoke:managed-gateway` |
| `L2` | Desktop packaging smoke | `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run package:smoke` |
| `L3` | Shared web-shell desktop bridge regression | `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test -- --run src/__tests__/desktop-config.spec.ts src/__tests__/app-shell.spec.tsx` |
| `L4` | Full web regression after desktop integration | `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build && npm run test && npm run test:e2e` |
| `L5` | Hybrid operator drill for desktop shell + managed Gateway + launch-target behavior | commands in this document |

## Standard Verification Commands

- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run typecheck`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run build`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run test`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run smoke:wave3`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run smoke:managed-gateway`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run package:smoke`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test -- --run src/__tests__/desktop-config.spec.ts src/__tests__/app-shell.spec.tsx`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test:e2e`

## Dogfood Drill

Use a disposable workspace such as `~/.kodaclaw-desktop-i6`.

### 1. Prepare the shared web dist

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web
npm run build
```

Expected:

- `dist/index.html` is generated successfully
- the current desktop shell can fall back to that dist even if no Vite dev server is running

### 2. Start desktop in `ManagedChild` mode against the built web shell

```bash
export KODACLAW_WORKSPACE_ROOT="$HOME/.kodaclaw-desktop-i6"
export KODACLAW_DESKTOP_GATEWAY_LIFECYCLE_MODE="ManagedChild"
export KODACLAW_DESKTOP_GATEWAY_URL="http://127.0.0.1:5076"
export KODACLAW_DESKTOP_WEB_DIST_INDEX="/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/dist/index.html"

cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop
npm run dev
```

Expected:

- desktop launches a healthy local Gateway automatically
- the main window loads the existing KodaClaw desks instead of `placeholder.html`
- closing the main window hides it instead of terminating the process
- clicking the tray icon or pressing `CommandOrControl+Shift+K` restores and focuses the window

### 3. Verify tray/menu launch targets

Use the tray/menu entries:

- `Open Chat`
- `Open Inbox`
- `Open Canvas`
- `Open Channels`

Expected:

- each menu action keeps the operator in the same shared shell window
- the active desk changes to the requested product surface without reloading the entire product

### 4. Verify startup launch-target routing

In a second terminal:

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop
npm run start -- --kodaclaw-route=/channels/binding-01
```

Expected:

- the existing desktop instance is reused
- the current window is restored/focused if hidden
- the shell opens the `Channels` desk and preserves the launch target metadata for the renderer bridge

### 5. Verify notification settings and quiet-hours behavior

Inside the running app:

- open `Models / Settings`
- confirm `notificationsEnabled` is on and quiet hours are off
- create or reuse at least one pending approval / open inbox result in the disposable workspace
- confirm a native desktop notification appears and clicking it returns the operator to the matching desk
- then enable quiet hours for the current local time window and repeat the trigger

Expected:

- when notifications are enabled and outside quiet hours, pending approvals/open inbox results surface as desktop notifications
- approval-linked inbox items do not produce a second duplicate notification
- when quiet hours cover the current local time, the same poll cycle does not produce a desktop notification

### 6. Verify restart behavior

From the tray/menu, click `Restart Gateway`.

Expected:

- the managed child Gateway is terminated and started again
- the desktop shell remains alive
- after health recovers, web desks continue to function without re-entering bootstrap unexpectedly

## Exit Criteria

Iteration 6 is accepted when all of the following are true:

- desktop shell engineering, runtime bridge, Gateway lifecycle, tray/menu, notifications, launch-target routing, and packaging smoke all have automated evidence
- the renderer still reuses the existing `kodaclaw-web` shell and the desktop integration does not fork a second UI stack
- `AttachOnly` / `ManagedChild` lifecycle behavior is stable enough for local development and smoke packaging
- notification routing reuses existing Control Plane state and quiet-hours settings instead of inventing a second notification backend
- startup args / protocol payloads / tray / notification clicks all converge on one launch-target model
- full backend/frontend regressions remain green on the current workspace state
