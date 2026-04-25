# `kun/.autonomy/` — Two-Role Self-Managed Queue

This directory holds the **self-managed framework queue** for
`claude-kun`. The authoritative protocol lives in
`/home/dawn/project/neuroagent/.omc/plans/kun-autonomy-protocol.md`;
this README is the short-form guide for anyone opening the repo fresh.

## Why self-managed?

The main controller (window 1) PMs two worker windows (`claude-kimi` on
C# business services, `claude-glm` on Python/frontend) and intentionally
does **not** PM Kun framework work. kun knows the framework internals
best, so kun wears both hats — 角色1 (Planner) and 角色2 (Executor).

## Daily loop

```
                    ┌─────────────────────────────┐
                    │  Is there a P1-KN-xxx in    │
                    │  ~/.claude/dispatch-queue/  │
                    │  claude-kun/pending/ ?      │
                    └──────┬──────────────────────┘
                           │
          ┌────────────Yes─┴─No──────────┐
          ▼                               ▼
  Pause any active-task.md,       Is active-task.md
  pivot to business brief         missing / status: done ?
          │                               │
          │                  ┌───────Yes──┴──No───┐
          │                  ▼                    ▼
          │        Wear 角色1 hat:       Wear 角色2 hat:
          │        pick from BACKLOG,     execute current AC,
          │        write active-task.md,  one commit per sub-step,
          │        commit `plan(autonomy)`push develop immediately
          │                  │                    │
          │                  └────────┬───────────┘
          │                           │
          └────────────  loop  ───────┘
```

## Files in this directory

| File | Purpose | Committed? |
|------|---------|------------|
| `README.md` | This file. | yes |
| `active-task.md.template` | Copy-start for each new task brief. | yes |
| `active-task.md` | Current Planner-authored brief; Executor reads this. `status: in-flight` while running, `status: done` when finished, `status: paused` if pre-empted by a business task. | yes (history is the audit trail) |

## Non-negotiable rules

1. Planner commits and Executor commits are **separate** — never write
   an "think + implement" commit in one shot. Planner commit subject
   is always `plan(autonomy): pick KN-SELF-xxx — <title>`.
2. Executor commits follow existing Conventional Commits + one-commit-
   per-sub-step cadence (see `feedback_kun_commit_often` in main's
   memory). Push to `develop` immediately after each commit.
3. Specs-first per `kun/CLAUDE.md` still applies — the Planner brief
   **must** list the relevant specs to read before coding.
4. AOT 0-warning gate applies to every sub-step touching an AOT-
   candidate module.
5. Do not expand the active task mid-flight. If new scope appears,
   either note it as a follow-up in `BACKLOG.md` or pause and repick.

## Picking the next item

Open `kun/docs/roadmap/BACKLOG.md`, read the queue (ordered by
priority), and select the top item whose size is `S` or `M`. `L`-sized
items stay parked — they need a ralplan / ADR round outside the
self-managed loop.

## When finished

1. Update `active-task.md` → `status: done` + final commit SHA.
2. Move the BACKLOG entry from its priority section to `## Done`.
3. `echo "<summary>" > /home/dawn/project/neuroagent/.omc/state/kun_done.txt`
   so the main controller's poll can pick up completion.
4. Wear 角色1 hat again and pick the next item.
