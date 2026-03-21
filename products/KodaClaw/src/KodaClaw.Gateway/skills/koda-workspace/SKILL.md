---
name: koda-workspace
description: KodaClaw workspace protocol guide — workspace file layout, tool usage patterns, and session context differences
license: built-in
---

# KodaClaw Workspace Protocol

## Workspace File Layout

KodaClaw stores all state in `~/.kodaclaw/workspace/` as plain files:

| File | Purpose |
|------|---------|
| `IDENTITY.md` | Agent name, role, and personality definition |
| `SOUL.md` | Core behavior principles and boundaries |
| `USER.md` | User profile, preferences, and working style |
| `MEMORY.md` | Long-term memory index (pointers, not content) |
| `HEARTBEAT.md` | Scheduled automation rules (YAML format) |
| `AGENTS.md` | Session rules, tool guidance, workspace conventions |

## Tool Usage Patterns

### workspace_protocol_update
Use to update structured sections in workspace files. Available targets:
- `identity` — update name, role, personality traits
- `soul` — update principles and behavior boundaries
- `user` — update user profile information learned in conversation
- `memory` — add a new memory entry
- `agents` — add or update AGENTS.md sections
- `heartbeat` — add or modify a scheduled automation rule

```
workspace_protocol_update(target="user", section="Preferences", content="- Prefers concise answers\n- Works in PST timezone")
```

### workspace_memory_append
Use to append a fact or note to MEMORY.md without a section heading.

### workspace_read
Use to read any workspace file content during a session.

## Session Context Differences

| Session Type | Context Loaded | Skills Available |
|-------------|----------------|-----------------|
| Main (chat) | IDENTITY, SOUL, USER, MEMORY, HEARTBEAT, AGENTS | Yes |
| Channel DM | AGENTS, IDENTITY, SOUL, USER, thread SUMMARY | Yes |
| Channel Group | AGENTS, IDENTITY, SOUL (no USER by default) | Yes |
| Automation | AGENTS, IDENTITY, SOUL, USER, HEARTBEAT | Yes |

## Memory Writing Strategy

- Write to `user` after learning stable facts about user preferences or context.
- Write to `memory` for important events, decisions, or references to recall later.
- Write to `heartbeat` when the user asks for recurring tasks ("every morning at 9am...").
- Avoid writing ephemeral conversation details — only write what will be useful next session.

## Skill Self-Installation

To add domain knowledge for future sessions:
1. Write a SKILL.md file: `fs_write workspace/skills/<name>/SKILL.md`
2. Verify discovery: `skill_list`
3. Activate in current session: `skill_activate <name>`
