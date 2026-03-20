# Frontend Refresh And I18N Plan

Last updated: 2026-03-20
Status: Completed

## Objectives

This plan covers two explicitly separated frontend iterations for `kodaclaw-web`:

1. Iteration A: full UI/UX refresh of the shared KodaClaw web shell
2. Iteration B: bilingual support with default Simplified Chinese and switchable English

The work stays inside `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web` and related docs/tests.

## Design Direction

The refresh keeps KodaClaw's warm, inspectable operator feel, but pushes it into a more intentional "atlas observatory" visual system:

- strong left-rail / stage layout instead of a flat dashboard grid
- clearer hierarchy between mission status, navigation, and active desk content
- consistent control surfaces, action trays, pills, and status treatments
- better mobile collapse behavior for the multi-desk shell
- improved affordances for chat dispatch, bootstrap editing, and control-plane review

## Iteration A - UI/UX Refresh

### Scope

- rework the global shell layout, header, desk navigation, and responsive behavior
- unify shared visual primitives in `src/index.css`
- improve desk-level composition for chat, bootstrap, control-plane, and operator surfaces
- reduce ad-hoc inline layout styling where practical in favor of reusable CSS classes
- preserve existing API contracts and test ids unless there is a strong reason to change them

### Planned implementation slices

1. Shell architecture
   - refactor `src/App.tsx`
   - add an explicit mission rail, desk stage, and desk navigation model
2. Shared chrome
   - update `src/components/DeskHeader.tsx`
   - update `src/components/SystemStatusCard.tsx`
   - update `src/components/ModeBadge.tsx`
   - add locale-aware top controls to the shell chrome
3. Chat and bootstrap experience
   - update `src/components/ChatComposer.tsx`
   - update `src/components/MessageTimeline.tsx`
   - update `src/components/BootstrapPanel.tsx`
4. Desk surface polish
   - restyle and normalize the desk wrappers in inbox/sessions/models/automations/channels/plugins/canvas
   - preserve domain behavior while improving readability and action flow
5. Styling system
   - rebuild `src/index.css` around reusable surface/layout primitives and responsive rules

### Acceptance

- shell feels materially different and more coherent on desktop and mobile
- chat, bootstrap, and desk switching remain functional
- all primary desks remain reachable without layout breakage
- `npm run build`, `npm run test`, and `npm run test:e2e` stay green after follow-up fixes

## Iteration B - Bilingual Chinese/English

### Scope

- add frontend i18n infrastructure with default locale `zh-CN`
- support runtime switching between Simplified Chinese and English
- persist locale choice locally
- localize all primary shell chrome, desk labels, common status text, and frontend-generated guidance/errors
- keep server-returned opaque backend error details as pass-through when needed

### Planned implementation slices

1. I18n foundation
   - add `src/i18n/` provider, locale state, messages, formatting helpers
   - wrap the app in the provider from `src/main.tsx`
2. Shell localization
   - localize `src/App.tsx`, `src/components/DeskHeader.tsx`, `src/components/ModeBadge.tsx`
   - add a locale switch control in the shell chrome
3. Desk localization
   - localize primary visible strings in chat/bootstrap/control-plane desk components
   - localize major status/enum display helpers used by the UI
4. Locale-aware metadata
   - update `index.html` language defaults and document metadata hooks where appropriate
   - localize date/time formatting through the active locale
5. Test adaptation
   - update component and Playwright tests to avoid brittle single-language assumptions
   - add explicit coverage for default Chinese and English switching

### Acceptance

- app defaults to Chinese on first load
- user can switch to English without reload
- locale persists across refreshes
- primary visible shell/desks show translated UI chrome in both languages
- test suite remains green after locale-aware updates

## Verification plan

- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test:e2e`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`

## Delivery closure

Both frontend iterations are now closed.

### Iteration A delivered

- `src/App.tsx`, `src/components/DeskHeader.tsx`, `src/components/SystemStatusCard.tsx`, `src/components/ChatComposer.tsx`, `src/components/BootstrapPanel.tsx`, and `src/index.css` now form the refreshed "atlas observatory" shell with a stronger rail / stage / context layout.
- Primary desks were normalized onto shared surface primitives, with dedicated polish on inbox, sessions, models, automations, channels, plugins, and canvas surfaces.
- Additional stable test hooks were added where needed so shell refresh work does not force brittle selector coupling.

### Iteration B delivered

- `src/i18n/I18nProvider.tsx` and `src/components/LocaleToggle.tsx` now provide app-level locale state, persisted switching, and locale-aware date formatting.
- The web shell defaults to `zh-CN`, supports runtime switch to `en-US`, syncs `document.documentElement.lang`, and updates document title/description with the active locale.
- Primary desks and shared chrome now localize frontend-owned labels, summaries, status chips, empty states, and action text while still passing backend-originated opaque error details through.
- Test infrastructure now includes `src/__tests__/test-utils.tsx`, locale-aware component coverage, and Playwright assertions aligned with the default Chinese shell.

### Final verification result

- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build` ✅
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test` ✅
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test:e2e` ✅

## Documentation sync targets

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/README.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_STATUS.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_BACKLOG.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/DEVELOPER_GUIDE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/USER_MANUAL.md`
- this plan document
