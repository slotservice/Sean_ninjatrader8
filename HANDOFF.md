# HANDOFF — current state for next session

**Read this first.** Everything you need to continue the project.

## Role and setup

- The USER of this Claude session is a freelance developer working on
  behalf of a trader client named **Sean**.
- The USER is NOT a trader. They do not know TradingView, NinjaTrader, or
  the trading side. They are purely the development + relay layer.
- **Sean** is the end-user / trader. Messages that say *"client: …"*
  are Sean. The USER relays Sean's messages and pastes your replies back
  to Sean.
- Communication is currently happening over a chat app + AnyDesk remote
  control of Sean's PC when needed.

## The project

- Repo: https://github.com/slotservice/Sean_ninjatrader8 (remote `origin`).
- Working directory: `d:\Freelancer-Project\Daniel\Sean\`.
- Purpose: port a seven-indicator TradingView strategy to NinjaTrader 8
  for live automated MNQ Renko trading during the NYC morning session.

## Critical constraints

- **Git commits must NOT include** `Co-Authored-By: Claude` or
  `Generated with Claude Code` trailers. Use the commit message body
  only. This is a non-negotiable project and global user rule.
- Do NOT rewrite published commits or force-push to `main` without
  explicit user permission.
- When modifying the NinjaScript strategy file, always also copy it
  into `C:\Users\com\Documents\NinjaTrader 8\bin\Custom\Strategies\`
  so the user can verify it compiles on their own NT8 before pushing
  anything to the client.

## Project-specific reading order (after this file)

1. [SPEC_SUMMARY.md](SPEC_SUMMARY.md) — interpreted spec (what v1 is).
2. [PROGRESS_LOG.md](PROGRESS_LOG.md) — full build history, all compile
   passes, the eventual monolithic rewrite, the Value=24 fix. Read this
   end-to-end — it is the single source of truth for what has happened
   and why.
3. [ASSUMPTIONS_LOG.md](ASSUMPTIONS_LOG.md) — every assumption labelled
   EXACT / APPROX / OPEN.
4. [INSTALL_NOTES.md](INSTALL_NOTES.md) — client-side install + chart setup.
5. [VALIDATION_CHECKLIST.md](VALIDATION_CHECKLIST.md) — side-by-side
   TV vs NT8 validation plan.

The client-side raw materials are in `files/` and `chat.md` (not pushed
to git, read only locally). `project_status&plan.md` is the original
master brief from the freelancer to the AI.

## Current state (as of handoff)

- Code: **compiles clean** on the user's dev machine AND on Sean's PC.
- Strategy architecture: **monolithic** — all indicator math is inlined
  inside `nt8/Strategies/TV_RenkoRangeStrategy.cs` (single file).
  The standalone indicator files in `nt8/Indicators/` still exist and
  compile but are NOT referenced by the strategy. Reason: NT8's
  factory-method auto-generator is flaky on certain installs (as was
  the case for Sean), so the strategy sidesteps it entirely. See
  PROGRESS_LOG.md 2026-04-22 entry for the full story — do NOT try
  to "restore" the chain-wiring factory-method style, it will break
  Sean's install again.
- Chart setup on Sean's PC: MNQ JUN26, NT8 Renko, `Value = 24` (NOT 6
  — NT8's Value is in ticks, TV's box size is in points, so TV box 6
  = NT8 Value 24 for MNQ). This is the critical realization from
  2026-04-22 that brought signal-count parity with TV.
- Signal-count parity: **achieved** after the Value=24 fix. Over
  ~1h45min of live test on 2026-04-22, NT8 produced ~20–25 trades,
  matching TV's ~3–4 trades per 15-min window.
- Known residual differences vs TV:
  1. NT8's Renko formation rule is not byte-identical to TV's
     Traditional Renko even at matched brick size. Small timing drift.
     A community "TradingView-style Renko" NT8 bar type would close
     this — client may try one.
  2. Client's TV uses **Crosstrade.io Advanced Builder** which has
     subscription-gated additional filtering. We only had access to
     the open-source Basic Builder Pine sources. Any day TV
     outperforms NT8 by more than the brick-formation residual is
     traceable to this gap, which is not fixable without Crosstrade's
     Advanced Builder source.

## Most recent commit (git log --oneline)

```
456bf54 Docs: record Renko Value=24 fix and signal-count parity milestone
444f1f5 Log monolithic-strategy milestone — clean compile on target install
df68647 Fix CS0136 variable shadowing in ComputeChain …
da255ee Monolithic strategy — inline all indicator math, remove factory-method calls
110076f Revert to factory-method calls; use Tools → Remove NinjaScript Assembly …
ed20c64 Use CacheIndicator<T> directly instead of NT8 factory methods
```

## Active task when this handoff was written

Sean requested **three optional anti-chop filters** on the strategy,
all defaulting to OFF so current behavior is unchanged unless enabled.
I (the previous Claude session) had just committed to building them
and estimated ~30 minutes. They were NOT yet built.

### Anti-chop filter spec (to implement)

Add three new strategy parameters, all `int` with default `0` (=disabled):

1. **MinBarsBetweenEntries** — after any entry/exit, skip new signals
   for N bars. Track a `lastTradeBar` field. In entry logic:
   `if (MinBarsBetweenEntries > 0 && CurrentBar - lastTradeBar <
   MinBarsBetweenEntries) return;`
2. **MinHoldBars** — block exit signals until the current position has
   been open N bars. Track `entryBar` when a position opens. In exit
   logic: `if (MinHoldBars > 0 && CurrentBar - entryBar < MinHoldBars)
   skip this opposite-direction signal.`
3. **SignalConfirmationBars** — require the Range Filter direction
   (`s_rf_dir`) to have been consistent for N bars before allowing
   the signal to fire. At the final signal step:
   `if (SignalConfirmationBars > 0) check that s_rf_dir[0..N-1] all
   equal signalDir.`

All three go in group `"05 Anti-chop filters"` on the strategy param
panel. The logic changes live in `OnBarUpdate` / entry-exit block.

Update `PROGRESS_LOG.md` with a new entry when done, commit, push,
tell the user what the commit hash is so they can relay a message to
Sean asking him to `git pull` on his PC and re-test.

## Immediate user-facing deliverable after code changes

Write a short message (≤6 lines) for the user to copy-paste to Sean,
saying "anti-chop filters added, defaults are 0 (disabled), here's
how to enable them, pull and recompile."

## Things that are DONE — do not redo

- All seven indicators ported (monolithic strategy has the math inline;
  standalone indicator files exist for chart use).
- Installation and compile on Sean's PC.
- NT8 unit-mismatch fix (Value = 24 on chart).
- Docs: SPEC_SUMMARY, INSTALL_NOTES, ASSUMPTIONS_LOG, PROGRESS_LOG,
  VALIDATION_CHECKLIST, README.

## Things that are DEFERRED (v2+, don't build unless asked)

- Separate signal-chart vs execution-instrument (trade NQ on MNQ chart).
- Calculate.OnEachTick earlier-entry mode.
- Porting the `BOOM PRO PINESCRIPT.txt` 8th indicator Sean sent later
  in chat (he said "nevermind, lets move forward").
- Reverse-engineering Crosstrade.io Advanced Builder.

## Communication style expected

- Short, practical, direct. Not over-cheerful.
- When the user asks "how should I reply?" — give a message Sean-ready
  that they can copy-paste.
- When the user says "give me shorter version" — cut by ~70%.
- Do NOT narrate internal deliberation in responses. State decisions.
- Always verify code changes compile-locally before having the user
  push to Sean's PC. The user trusts you for correctness; Sean trusts
  them for delivery.

## If you hit an issue

- NT8 compile errors: paste → diagnose → patch → push → user copies
  to Sean's PC. Never ask the user to "try deleting things" in NT8
  without a clear understanding of NT8's auto-gen cycle (see
  PROGRESS_LOG 2026-04-21 and 2026-04-22 for the scars from that
  approach).
- Anything API-shaped (like the CacheIndicator<T> mistake): verify
  from NT8's documented public surface before committing. Don't
  assume protected methods are available.
