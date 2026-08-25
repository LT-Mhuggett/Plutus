# Handover — next session

> **Empty on purpose.** Matt, 2026-08-25: *"Handover should be empty as we are working, then a new
> one created for tomorrow when I finish tonight."*
>
> An empty handover means **there is no stale state to mislead you.** The last one was written
> 2026-08-21 and by 2026-08-25 every version number in it was wrong, it described none of the four
> days' work that followed, and two items had sat on its "do first" list for three days after being
> closed. That is worse than nothing, because a list is read as current.

## Where to look instead

| You want… | Open |
|---|---|
| **What is missing, blocked, or switched off** | [`Build/Platform Gaps.md`](Build/Platform%20Gaps.md) |
| How to build, test, migrate, deploy — and the traps | [`Build/repo-runbook.md`](Build/repo-runbook.md) |
| What a till can do, and what stops the money rules drifting | [`Build/till-design.md`](Build/till-design.md) — A0, Part B, C1/C2, Part E |
| How to prove the MAUI build works | [`Build/Test Maui.md`](Build/Test%20Maui.md) |
| What every document is for | [`Build/index.md`](Build/index.md) |
| What actually happened | **the commit log** — it is the record, and it does not go stale |

## Writing tomorrow's handover

⚠ **One day long. Not a history.** (Matt, 2026-08-17: *"I only want the handover to be for the
following day."*) The moment it starts accumulating, it stops being read and starts being trusted,
which is the worst of both.

Keep it to four things:

1. **What is deployed right now** — the version of each of the four components, and how that was
   *verified*, not assumed.
2. **What I changed today**, one line each, with the commit.
3. **The single next action**, and who has to take it.
4. **Anything I left broken or half-done** — stated plainly, because this is the only thing here that
   cannot be recovered from the commits.

Everything else belongs in a standing document:

- a missing capability, a blocked decision, a feature switched off → **`Platform Gaps.md`**
- a trap that cost time → **`repo-runbook.md`** codebase pitfalls
- a ruling Matt made → **`till-design.md`** Part E
- something a person must click to prove → **`Test Maui.md`**

⚠ **Before putting a to-do here, check it against the code.** On 2026-08-21 two of the five items on
the do-first list were not real — one was already closed, one named the wrong till — and both had
been contradicted elsewhere in the same documents for days. The check costs minutes; twice it
deleted the item.

*(The 2026-08-21 handover is kept verbatim in
[`Build/archive/handover-history-to-2026-08-17.md`](Build/archive/handover-history-to-2026-08-17.md).)*
