# Progress Log — Sean / TV → NT8 port

This file records every concrete action taken so the project can be audited
at any future point.

Timestamps are local to the working session.

---

## 2026-04-21 — Session start

### Context review
- Read `project_status&plan.md` (the master spec & build instructions).
- Read `chat.md` (full client conversation).
- Read every file in `files/`:
  - `BUY SELL ARTICULATION NT8.txt` (client's final entry/exit articulation).
  - `XTBUILDER2.8.4  PROTOTYPE.txt` (client's locked chain + settings).
  - `INDICATOR 17 ZLSMA.txt` (Pine v4 — veryfid).
  - `INDICATOR 27 LSMA.txt` (Pine v4 — LSMA Crossover).
  - `INDICATOR 37 macZLSMA.txt` (Pine v4 — veryfid).
  - `INDICATOR 47 stoch RVI.txt` (Pine v4).
  - `INDICATOR 57  RANGE FILTER.txt` (Pine v5 — DonovanWall / guikroth / tvenn).
  - `INDICATOR 67 TECHNICAL RATINGS.txt` (Pine v6 — TradingView, uses
    `TradingView/TechnicalRating/3` library import).
- Reviewed chart screenshot filenames for visual validation context.

### Findings
- No NinjaScript code had been produced yet — greenfield build.
- `INDICATOR 77 SLSMA.txt` is referenced in the plan but not present in
  `files/`. The client sent a follow-up SLSMA reference late in chat that
  could not be located as a standalone text file; it must be **reconstructed**
  per the plan's explicit formula description:
  *"SLSMA uses linreg smoothing against linreg(lsma)"*.
- Technical Ratings Pine source imports `TradingView/TechnicalRating/3`,
  which is not open — exact parity is not possible. Per plan, this is
  implemented as a clearly-labeled approximation, disabled by default.

### Spec locked (from truth-priority order in plan)
- Default chain (top → bottom = outermost → innermost source):
  - Range Filter  src = Stoch RVI.K
  - Stoch RVI     src = SLSMA.Default
  - SLSMA         src = LSMA Crossover.Trigger
  - LSMA Cross    src = ZLSMA.Default
  - ZLSMA         src = macZLSMA.Default
  - macZLSMA      src = Close
  - Technical Ratings — optional, last, unchecked by default.
- Reversal default = ON. Must fully disable when unchecked
  (opposite signal = close only).
- Flatten First = optional, not default.
- Session = 09:33–12:00 NY. Force flat at session end.
- Calculate = OnBarClose baseline.
- Default instrument = MNQ 06-26 (user selects front month at import).

---

## 2026-04-21 — Build phase

### File plan decided
Modular layout, one file per indicator plus one strategy plus one shared
enums file. All code placed under `nt8/` within the project root so the
raw Pine sources and the NT8 output are clearly separated.

```
nt8/
  Indicators/
    TV_MacZLSMA.cs
    TV_ZLSMA.cs
    TV_LSMACrossover.cs
    TV_SLSMA.cs
    TV_StochRVI.cs
    TV_RangeFilter.cs
    TV_TechnicalRatingsApprox.cs
  Strategies/
    TV_RenkoRangeStrategy.cs
  AddOns/
    TV_SharedEnums.cs
```

Supporting docs also produced at project root:
- `SPEC_SUMMARY.md`          — spec interpreted in plain English
- `INSTALL_NOTES.md`         — NT8 import / chart / session setup
- `VALIDATION_CHECKLIST.md`  — side-by-side TV vs NT8 verification plan
- `ASSUMPTIONS_LOG.md`       — every assumption made, labelled as exact or approximate
- `PROGRESS_LOG.md`          — this file

### Build order (respects dependency direction)
1. `TV_SharedEnums.cs`       — enums / helpers
2. `TV_MacZLSMA.cs`          — bottom of chain (src = Close)
3. `TV_ZLSMA.cs`             — src = macZLSMA
4. `TV_LSMACrossover.cs`     — src = ZLSMA
5. `TV_SLSMA.cs`             — src = LSMAC.Trigger (reconstructed)
6. `TV_StochRVI.cs`          — src = SLSMA
7. `TV_RangeFilter.cs`       — src = Stoch RVI K (final signal generator)
8. `TV_TechnicalRatingsApprox.cs` — optional, default off
9. `TV_RenkoRangeStrategy.cs` — ties the chain together, handles orders

### Key implementation choices (rationale)
- **Pine `linreg` is implemented manually** (not via NT8 `LinReg`) for bit-for-bit
  parity. Formula: `intercept + slope * (length - 1 - offset)` with x running
  0..length-1 from oldest bar to current bar.
- **Pine `stdev` is implemented manually** using population denominator
  (N, not N-1) to match Pine exactly.
- **Pine `ema` uses NT8 `EMA` with seeded first value = source[0]** to match
  Pine's recursive seeding. For the `change(src)<=0 ? 0 : stddev` idiom inside
  Stoch RVI we implement the EMA manually so the conditional zeroing happens
  before smoothing — identical to Pine's behavior.
- **Pine `stoch(rvi, rvi, rvi, n)` = 100 * (rvi - llv(rvi,n)) / (hhv - llv)** —
  implemented directly.
- **Source chaining** uses NT8 `ISeries<double>` overloads on each indicator —
  the cleanest native pattern. The strategy wires the chain explicitly in
  `State.DataLoaded`.
- **Alignment voting**: each indicator exposes a `Direction` series (+1 / −1 / 0).
  Strategy records which indicators are `UseAsFilter = true` and requires all
  checked indicators' Direction = sign(signal) before entering.
- **Reversal logic** coded exactly as:
  - `ReversalEnabled = false` → opposite signal *only closes* open position
    (no new entry on that bar)
  - `ReversalEnabled = true`  → opposite signal closes + immediately reverses
    (submitted as single `EnterLong`/`EnterShort` — NT8 auto-reverses)
  - `FlattenFirst = true` (optional) → closes first, waits one bar, re-enters
    on next confirmed opposite trigger.
- **Session enforcement** uses the strategy's own `SessionStart`/`SessionEnd`
  `TimeSpan` properties + `TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")`
  conversion so DST is handled correctly. Force-flat at session end is a hard
  close via `ExitLong("SessionEnd")` + `ExitShort("SessionEnd")` on the bar
  whose NY time ≥ SessionEnd.

### Build completed
All nine source files written to `nt8/` subtree. See individual file headers
for per-file notes.

### Supporting docs written
- SPEC_SUMMARY.md
- INSTALL_NOTES.md
- VALIDATION_CHECKLIST.md
- ASSUMPTIONS_LOG.md

### Self-check pass results
After writing all files, I did a full re-read pass and caught one real
bug in the warm-up guards of the four double-linreg indicators:

- **Bug**: Original code returned early before populating `lsmaSeries`,
  which meant the first bar that passed the guard read `lsmaSeries[1..]`
  values of `0` (default), producing a spurious first `zlsma2` / `zlsma`
  / `slsma` / `trigger` value.
- **Fix**: Staged the warm-up so `lsmaSeries[0]` is populated as soon as
  `CurrentBar >= Length - 1`, and the second-pass linreg / SMA is only
  computed once `lsmaSeries` itself has enough filled history. Applied
  to `TV_MacZLSMA`, `TV_ZLSMA`, `TV_SLSMA`, `TV_LSMACrossover`.

Other items verified during the self-check (all passed):
- NT8 time-zone conversion uses `Core.Globals.GeneralOptions.TimeZoneInfo`
  as the source zone (correct canonical API).
- Generated factory-method boilerplate matches NT8 auto-generation format
  so the indicators are callable from Indicator, MarketAnalyzerColumn,
  and Strategy namespaces.
- Pine `linreg` / `stdev` (population) / `ema` (seeded with first input)
  are all manual implementations for bit-for-bit parity.
- Strategy `EntriesPerDirection = 1` prevents scaling in.
- Strategy handles three reversal modes: ReversalEnabled=false (close
  only), ReversalEnabled=true+FlattenFirst=false (ExitShort+EnterLong
  same bar), ReversalEnabled=true+FlattenFirst=true (pending re-entry).
- Session force-flat uses edge detection (prev bar inside, current bar
  at/past end) so it only fires once per crossing.
- `BarsRequiredToTrade = 300` gives reasonable warm-up headroom for
  Range Filter's 240-period EMAs.

### Delivered package — final file list

```
SPEC_SUMMARY.md              — interpreted spec in plain English
ASSUMPTIONS_LOG.md           — every assumption labelled EXACT / APPROX / OPEN
INSTALL_NOTES.md             — NT8 import, chart, and session setup guide
VALIDATION_CHECKLIST.md      — 8-section side-by-side TV vs NT8 verification
PROGRESS_LOG.md              — this file
nt8/AddOns/TV_SharedEnums.cs
nt8/Indicators/TV_MacZLSMA.cs
nt8/Indicators/TV_ZLSMA.cs
nt8/Indicators/TV_LSMACrossover.cs
nt8/Indicators/TV_SLSMA.cs            (reconstructed — see ASSUMPTIONS_LOG §A9)
nt8/Indicators/TV_StochRVI.cs
nt8/Indicators/TV_RangeFilter.cs
nt8/Indicators/TV_TechnicalRatingsApprox.cs   (labelled approx — off by default)
nt8/Strategies/TV_RenkoRangeStrategy.cs
```

### Outstanding items for future sessions
- Client must provide 3–5 marked-up chart examples (date + approx time +
  expected entry/exit) so side-by-side historical validation can be run.
- Technical Ratings approximation must be validated against TV separately
  before enabling by default.
- Once OnBarClose parity is confirmed, a second pass can evaluate moving
  Range Filter to `Calculate.OnEachTick` for earlier entry (per client's
  stated future goal).
- Second data-series / alternate-instrument execution (NQ vs MNQ) — plan
  called this "alternate later"; not built in v1, deferred to v2.

---

## 2026-04-21 — Follow-up hardening pass

Asked by the user to exercise senior judgment on what else was worth
doing before they attempt the first NT8 compile. My call: tighten
everything I can reasonably judge without compile feedback, so the
first compile is a real check, not a noise storm.

### Done in this pass
- **Visually inspected the JPGs** (not just their filenames). Confirmed:
  - `slsma 1.jpg` shows the simpler SLSMA variant with the same three
    fields my reconstruction exposes (Source / Length / Offset /
    Timeframe). The reconstruction matches this variant, which the
    client accepted in chat as "basically identical to what was missing".
  - `slsma pullbacks 4 setting default.jpg` reveals the full "SLSMA
    Pullbacks" has an extra `Filtersigs` toggle and additional pb1/pb2
    signal plots — functionality not in the reconstruction but NOT
    required for the chain (chain only needs the smoothed line value).
  - `obc.jpg` confirms the locked chain (ZLSMA ← macZLSMA, LSMA ←
    ZLSMA, etc.) matches the XTBUILDER spec — good.
  - `range and rvi settings.jpg` shows Stoch RVI source as LSMA
    Crossover on a specific experimental chart; XTBUILDER's spec wins
    per truth priority. Flagged in `ASSUMPTIONS_LOG §H5`.
- **NT8 API audit** of every built-in used:
  - Verified `SMA/EMA/RSI/CCI/MACD/ADX/StochasticsFast/WilliamsR/Momentum`
    signatures; all match how I called them.
  - Verified `macd.Diff` output exists on NT8 MACD class.
  - Verified `StochasticsFast` exposes `.K` and `.D` Series<double>.
  - Refactored Tech Ratings' inline `Vote` local function into a
    private static method (`MAVote`) — defensive against older C# compilers.
  - Confirmed `Core.Globals.GeneralOptions.TimeZoneInfo` is the canonical
    NT8 8.x API for the display-zone source of `Time[i]`.
  - Added `using NinjaTrader.NinjaScript.DrawingTools;` to strategy;
    removed unused `using NinjaTrader.NinjaScript.AddOns.TVPort;`.
- **Visible Buy/Sell markers** now drawn on the chart every signal bar
  (`Draw.ArrowUp` + `Draw.Text` for longs; `Draw.ArrowDown` + `Draw.Text`
  for shorts). Mirrors Pine's `plotshape(longCondition, "Buy", …)` so
  side-by-side TV↔NT8 validation is now eye-comparable directly, not
  just via the Strategy Performance tab.
- **All six chain indicators auto-added to chart** on enable (previously
  only Range Filter + Stoch RVI were added). This lets the user see
  every stage of the chain visually during validation.
- **`BarsRequiredToTrade` raised from 300 → 1000** so Range Filter's
  second EMA (effective period ≈ 479) is fully converged before any
  trade fires. With `Days to load = 5`, this warm-up happens entirely
  in the pre-session load — transparent to live use. Documented in
  `INSTALL_NOTES`.

### Files changed this pass
- `nt8/Indicators/TV_TechnicalRatingsApprox.cs` — Vote → MAVote private method.
- `nt8/Strategies/TV_RenkoRangeStrategy.cs` — DrawingTools import, chart
  wiring for all indicators, `BarsRequiredToTrade = 1000`, visible
  Buy/Sell arrow + text drawings on signal bars.
- `INSTALL_NOTES.md` — documented arrow markers, chart wiring, and
  1000-bar warm-up.
- `VALIDATION_CHECKLIST.md` — §2 rewritten to reference the new arrow
  markers for direct visual comparison.
- `ASSUMPTIONS_LOG.md` — added §H4 (SLSMA missing-file note cross-checked
  vs. screenshot), §H5 (Stoch RVI source chain variance flag).

### Still requires client action before v1 sign-off
1. Compile the code in NT8 — I did not run the compiler, and my API audit
   is a best-effort mitigation, not a substitute for the compiler itself.
2. Provide 3–5 marked historical trades (date + approx NY time + expected
   long/short) for side-by-side signal-bar parity verification.
3. Run NT8 Strategy Analyzer on the last 5 trading days, 09:33–12:00 NY,
   and compare the trade list + net PnL direction to the TradingView
   playback reference. Checklist in `VALIDATION_CHECKLIST.md`.

---

## 2026-04-21 — First compile pass (user ran F5)

### Compile result — 7 errors, all same root cause
User pressed F5 in NT8's NinjaScript Editor. Compile surfaced 7 instances
of C# error **CS0176**:

> Member 'ScaleJustification.Right' cannot be accessed with an instance
> reference; qualify it with a type name instead

Affected files (one occurrence each):
- `TV_ZLSMA.cs` line 54
- `TV_TechnicalRatingsApprox.cs` line 84
- `TV_StochRVI.cs` line 85
- `TV_SLSMA.cs` line 62
- `TV_RangeFilter.cs` line 94
- `TV_MacZLSMA.cs` line 58
- `TV_LSMACrossover.cs` line 53

### Root cause
The NT8 indicator base class exposes a property named `ScaleJustification`
(of enum type `ScaleJustification`). When the identifier appears on both
sides of an assignment (`ScaleJustification = ScaleJustification.Right`),
C#'s name resolution picks the property (instance member) for the
right-hand side and then complains that the property is being accessed as
if it were the enum type. This is a C# language rule (CS0176), not an NT8
quirk.

### Fix
Qualified the right-hand side with the full enum namespace:

```csharp
ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
```

Applied to all 7 files. Verified via grep that every remaining usage is
fully qualified — no regressions.

### Lesson for future NT8 work
Any time an NT8 property and its enum type share a name, the enum must be
fully qualified on the RHS of the assignment. Common NT8 cases where this
trips people up: `ScaleJustification`, `Calculate` (enum `Calculate` vs
property `Calculate`), `StopTargetHandling`. The strategy file already
uses `Calculate.OnBarClose` safely because `Calculate` (enum) and the
`Calculate` property are in compatible positions; but the pattern is the
same risk.

### Action for user
Re-compile (F5 again). This specific class of error should be gone.
Anything new that appears → paste the error list and I'll keep fixing.

---

## 2026-04-21 — Second compile pass (user ran F5 after ScaleJustification fix)

### Compile result — 28 errors, all same root cause (CS0111)
After the `ScaleJustification` fix, F5 surfaced a different class of error:

> Type 'MarketAnalyzerColumn' already defines a member called 'TV_X' with
> the same parameter types

> Type 'Strategy' already defines a member called 'TV_X' with the same
> parameter types

Two errors per indicator × two partial classes (MarketAnalyzerColumn,
Strategy) × 7 indicators = 28 total errors.

### Root cause
I had manually written a `#region NinjaScript generated code` block at the
bottom of each indicator file that included partial-class factory methods
on THREE classes:
1. `NinjaTrader.NinjaScript.Indicators.Indicator`
2. `NinjaTrader.NinjaScript.MarketAnalyzerColumns.MarketAnalyzerColumn`
3. `NinjaTrader.NinjaScript.Strategies.Strategy`

NT8's compiler AUTO-GENERATES the MarketAnalyzerColumn and Strategy
factory methods during compile as a post-processing step — but it does
NOT auto-generate the `Indicator` one. So my manual `Indicator` extension
is fine; my manual MarketAnalyzerColumn + Strategy extensions collided
with NT8's auto-gen versions.

The clue is in the error pattern: zero errors for the Indicator partial
class (because only one version exists — mine), but two errors each for
MarketAnalyzerColumn and Strategy (because two versions exist — mine + NT8's).

### Fix
Deleted the MarketAnalyzerColumn and Strategy namespace blocks from all
7 indicator files, keeping the Indicator partial class extension intact.
Replaced with a one-line comment so future editors know this is
intentional, not a missing feature:

```csharp
// NOTE: MarketAnalyzerColumn and Strategy partial-class factory methods
// are intentionally omitted — NT8 auto-generates them during compile.
```

Verified via grep that none of the 7 files still contain the deleted
namespace blocks. Copied all 7 cleaned files back into
`Documents\NinjaTrader 8\bin\Custom\Indicators\`.

### Lesson for future NT8 work
When hand-writing factory-method boilerplate for a public NT8 indicator,
include ONLY the `NinjaTrader.NinjaScript.Indicators.Indicator` partial
class extension. Never include `MarketAnalyzerColumns.MarketAnalyzerColumn`
or `Strategies.Strategy` extensions — those are auto-generated by NT8's
compiler. The `"NinjaScript generated code. Neither change nor remove."`
region comment at the top of that section is misleading and only applies
to the Indicator one.

### Action for user
Re-compile (F5 a third time). Expected result: "Compile succeeded"
(green). If any new error appears, paste it.

---

## 2026-04-21 — Third compile pass (user ran F5 after MarketAnalyzer/Strategy removal)

### Compile result — still duplicates, this time in the Indicator partial class
F5 surfaced a new batch of errors of the same family:

> CS0102: The type 'Indicator' already contains a definition for 'cacheTV_X'
> CS0111: Type 'Indicator' already defines a member called 'TV_X'…
> CS0229: Ambiguity between 'Indicator.cacheTV_X' and 'Indicator.cacheTV_X'
> CS0121: The call is ambiguous between the following methods…

### Root cause (corrected understanding)
My previous diagnosis was wrong. NT8 auto-generates factory methods for
**all three** partial classes (`Indicator`, `MarketAnalyzerColumn`, and
`Strategy`) — not just the MarketAnalyzer and Strategy ones.

The earlier compile passes just short-circuited at different error types
first:
- Pass 1 halted on `ScaleJustification` syntax errors before ever checking
  partial-class conflicts.
- Pass 2 halted on MarketAnalyzer + Strategy duplicates (those two were
  deleted, compilation then went further).
- Pass 3 (this one) finally surfaced the Indicator partial class duplicates.

### Fix
Deleted the **entire** `#region NinjaScript generated code. Neither
change nor remove.` block from every indicator file. This includes the
Indicator partial-class extension I had previously kept. Left a short
comment in its place:

```csharp
// NT8 auto-generates the Indicator / MarketAnalyzerColumn / Strategy
// partial-class factory methods for this indicator during compile.
// Do not author them by hand — doing so collides with NT8's auto-gen.
```

Verified no remaining `cacheTV_*` declarations or `CacheIndicator<TVPort`
calls in any indicator file. Copied all 7 cleaned files into NT8's
`Custom\Indicators\` folder.

### Lesson for future NT8 work (final corrected version)
When writing a public NinjaScript indicator, DO NOT author any
`#region NinjaScript generated code` block yourself. The comment
"Neither change nor remove" applies ONLY when NT8's own compiler wrote
that block during a previous compile. If you're writing the indicator
from scratch, omit the entire region. NT8 will add it on first compile
and it will take care of itself thereafter.

This is the NT8 convention that was not obvious from the documentation:
the `#region NinjaScript generated code` is **output**, never **input**.

### Action for user
Re-compile (F5 a fourth time). Expected: "Compile succeeded" (green).
This should be the last structural error class. If any new errors
appear, paste them.

---

## 2026-04-22 — Monolithic strategy rewrite + clean compile on target

### Context
Between the "fourth compile succeeded on user's dev machine" milestone
(2026-04-21) and today, the package was delivered to the client for
install on their trading PC. The same code that compiled cleanly on the
dev machine produced multiple waves of failures on the client's install.

### Failures observed on client's install (in order)
1. **CS1955 "Non-invocable member"** (first install attempt) — NT8's
   factory-method auto-generator did not produce Strategy-partial-class
   factory methods for the `TV_*` indicators. This meant calls like
   `TV_MacZLSMA(Close, …)` in the strategy resolved to the type, not
   a method.
2. **Deleting `@Strategy.cs`** was suggested as a recovery step (since
   it was stale from 2026-01-15) but cascaded into CS0103 "The name
   'indicator' does not exist" across dozens of the client's vendor
   files and NT8 built-ins — `@Strategy.cs` contained the `indicator`
   field declaration those files depend on.
3. **A `CacheIndicator<T>` refactor** was attempted as a bypass, based
   on a mistaken assumption that `CacheIndicator<T>` is part of the
   public NinjaScriptBase surface. It isn't — produced CS0103 "The
   name 'CacheIndicator' does not exist in the current context".
   Reverted.
4. **Tools → Remove NinjaScript Assembly** was blocked by NT8 because
   of pre-existing compile errors (chicken-and-egg).
5. **Full NT8 reinstall** restored the framework `@*.cs` files and
   produced a clean baseline on the client PC. Reinstalling our code
   on top of the clean baseline reproduced the original CS1955
   problem — confirming the auto-gen failure was environmental /
   install-specific and not something our code could repair.

### Final resolution — monolithic strategy
Rewrote `TV_RenkoRangeStrategy.cs` as a fully self-contained strategy
that inlines every Pine math primitive (linreg, stdev, ema, stoch, sma,
Range Filter smoothrng/rngfilt/CondIni recursion) as private methods on
the strategy class itself. The strategy no longer references any
`TV_MacZLSMA` / `TV_ZLSMA` / `TV_LSMACrossover` / `TV_SLSMA` /
`TV_StochRVI` / `TV_RangeFilter` / `TV_TechnicalRatingsApprox` as
types or methods. Chain intermediate values are held in ~30 private
`Series<double>` fields allocated in `State.DataLoaded`.

Net effect: zero dependency on NT8's factory-method auto-generator, so
the original class of failure is structurally impossible now. Compile
behaviour is deterministic across NT8 installs.

One follow-up fix after the first compile attempt of the monolithic
build: CS0136 "variable shadowing" on local `prev` names — two inner
`if` blocks declared `double prev` before the outer method scope also
declared `double prev` for Range Filter recursion. C# disallows this
even though the outer declaration textually follows the inner ones.
Renamed inner variables to `zPrev` / `zCur` and `slPrev` / `slCur`.

### Net result
- User's own dev machine: compiles clean.
- Client's production PC (after NT8 reinstall + monolithic strategy
  install): compiles clean.

### Files changed since the 2026-04-21 milestone
- `nt8/Strategies/TV_RenkoRangeStrategy.cs` — rewritten monolithic.
- Supporting commits in git log:
  - `03be351` — moved TV_* classes out of TVPort sub-namespace
  - `ed20c64` — attempted (wrong) CacheIndicator<T> refactor
  - `110076f` — reverted CacheIndicator<T> mistake
  - `da255ee` — monolithic rewrite
  - `df68647` — CS0136 variable-rename fix

### Indicator files (TV_MacZLSMA.cs, TV_ZLSMA.cs, …)
Left unchanged in the repo. They are standalone NT8 indicators, usable
on a chart individually. They are NOT referenced by the monolithic
strategy. On the client's install they can be present or absent — the
strategy works either way. The client install currently has them
removed (the pre-reinstall cleanup step did not re-install them).
If the client wants them on a chart for visual validation, they can be
added back via git pull + copy at any time; they compile independently
of the strategy.

### Technical Ratings status
The `TV_TechnicalRatingsApprox` indicator file is also unchanged in the
repo. The monolithic strategy does NOT wire it in — the `UseTechnicalRatings`
and `UseTechRatingsFilter` toggles exist on the strategy UI but are
currently reserved / no-ops in the monolithic build. Per the locked spec
this is not a blocker (Technical Ratings was always off by default).

### Outstanding items for client validation
1. Apply `TV_RenkoRangeStrategy` to an MNQ Renko-6 1-second chart on
   the client's PC.
2. Enable during the 09:33–12:00 NY session window.
3. Visually compare the Buy/Sell arrows drawn by the strategy against
   the client's TradingView chart signals. See `VALIDATION_CHECKLIST.md`.
4. Run NT8 Strategy Analyzer backtest over the last 5 trading days
   within the session window, compare trade list + per-trade direction
   against TradingView playback.
5. Client to mark 3–5 historical trades with date + approximate NY
   time so side-by-side signal-bar parity can be verified at specific
   known-good points.

---

## 2026-04-22 — Live-chart tuning: signal-count parity reached

### Context
Client applied the monolithic strategy to a live MNQ Renko chart during
the NYC morning session. Strategy ran without errors. Client then
reported two observations:

1. Visual chart sometimes shows a lone "Buy" / "Sell" text without an
   associated order ticket. Clarified: this is a same-direction
   redundant signal that fired while the strategy was already in that
   direction; `EntriesPerDirection = 1` correctly prevents adding a
   second lot. Chart draws the detection marker but no order is placed.
   Offered to suppress these redundant markers as a future option;
   client preferred to keep them visible for now.

2. NT8 was generating significantly more trades than TradingView over
   the same time window (~20+ on NT8 vs ~4 on TV per 15-minute window).
   Client hypothesis: "the range filter is more sensitive on this type
   of renko chart."

### Parameter-tuning attempt (did not resolve)
Client raised `Range Multiplier` from `0.1` → `0.5` and `Sampling Period`
from `240` → `480`. Signal count reduced but strategy was still chopping
in and out of trends rather than holding through big pushes.

### Root cause found — Renko Value unit mismatch (not a tuning issue)
- NT8's built-in Renko `Value` field is in **ticks**.
- TradingView's Traditional Renko box size is in **points**.
- For MNQ (tick 0.25), `Value = 6` on NT8 = **1.5 points per brick**.
- TV box size 6 = **6.00 points per brick**.
- That's a **4× scale mismatch**. NT8 was producing 4× more bricks than
  TV over the same price path, so the Range Filter saw 4× more data
  points and fired ~4× more signals.

No amount of Range Filter parameter tuning can compensate for brick
data that arrives 4× more frequently than the tuning was calibrated for.

### Fix
Client changed the NT8 chart's `Data Series → Value` from `6` to `24`
and reverted Range Filter parameters to the defaults (`Sampling Period
= 240`, `Range Multiplier = 0.1`).

### Result
Over a roughly 1 h 45 min window (09:35–11:18 NY), NT8 produced ~20–25
trades, matching TV's ~3–4 trades per 15-minute window pace. Client
confirmed:

> *"this is already looking better, this looks much closer to the TV
> chart and also its seemingly not chopping out at big trend pushes —
> which is key to this strategy."*

Signal-count parity is effectively reached. Residual fine-timing
differences are expected due to the NT8-vs-TV brick *formation* rule
difference (not size) — documented in `ASSUMPTIONS_LOG §E1a`. A
community "TradingView-style Renko" NT8 bar type can close the residual
if the client wants exact bar-by-bar parity; otherwise current setup
is acceptable per the locked spec ("signal behavior is priority,
chart naming parity is not required").

### Docs updated in this milestone
- `INSTALL_NOTES.md §3` — critical `Value = 24` setup note added
  prominently; troubleshooting §8 cross-references it; §7 difference
  table updated.
- `ASSUMPTIONS_LOG.md §E1` — corrected; §E1a added for the residual
  formation-rule difference.
- `PROGRESS_LOG.md` — this entry.

### Notes on the Basic vs Advanced Builder gap
Client had previously described Crosstrade.io's **Advanced Builder**
as subscription-gated and the source of TV's profitability over the
open-source **Basic Builder**. Only the Basic Builder source was ever
delivered, so the port faithfully implements Basic Builder logic.
Any day where TV's Advanced Builder outperforms NT8 by more than the
brick-formation residual will be traceable to that paywalled filter
delta, which cannot be replicated without its source. If the client
wants generic anti-chop heuristics (min-bricks-between-entries,
hold-duration floor, consecutive-signal confirmation count), they can
be added as optional strategy parameters — client has been offered
this and has deferred.

### Outstanding items for v1 sign-off (carried forward)
1. Client to sim-trade this week during the NYC session.
2. Client to run NT8 Strategy Analyzer on last 5 trading days of MNQ
   (Renko, **Value = 24**, Days to load = 5) and compare the trades tab
   against TV bar replay.
3. Client to flag any specific bars where NT8 fires and TV doesn't
   (or vice versa) for targeted investigation.

---

## 2026-04-21 — Fourth compile pass: SUCCESS ✓

User ran F5 a fourth time. The red error panel is gone. NinjaScript
Explorer now shows:
- `AddOns\TV_SharedEnums` ✓
- `Indicators\` (collapsed — contains all 7 TV_* indicators) ✓
- `Strategies\TV_RenkoRangeStrategy` ✓

Compile ran clean. All four compile passes have now resolved:
1. Pass 1 — 7 × `ScaleJustification` qualification errors. FIXED.
2. Pass 2 — 28 × duplicate MarketAnalyzerColumn / Strategy factory methods. FIXED.
3. Pass 3 — 28 × duplicate Indicator partial-class factory methods. FIXED.
4. Pass 4 — clean.

### v1 code-complete status
The NT8 code package compiles and is now runnable. This closes out the
"compile in NT8" item of the three remaining v1 action items listed in
the 2026-04-21 build-phase notes.

Next actions (user-side):
1. ~~Compile in NT8~~ **DONE**
2. Provide 3–5 marked historical TradingView trades for side-by-side
   signal-bar parity verification.
3. Run NT8 Strategy Analyzer on the last 5 trading days, 09:33–12:00 NY,
   and compare trade list + net PnL direction to TV playback per
   `VALIDATION_CHECKLIST.md`.

---

## 2026-04-21 — Fifth compile pass (client's machine only)

### Compile result on client's machine — 7 × CS1955 errors
Client installed the zip, pressed F5, got:

> TV_RenkoRangeStrategy.cs — Non-invocable member 'TV_MacZLSMA' cannot be used like a method  (CS1955)
> (and 6 more identical errors for the other TV_* indicators)

Note: the SAME code compiled cleanly on the freelancer's machine during
pass 4. Only the client's machine failed. Artefacts shown in screenshots:
- Client's `Documents\NinjaTrader 8\bin\Custom\Strategies\@Strategy.cs`
  dated **2026-01-15** (stale, only 1 KB).
- Client's `Documents\NinjaTrader 8\bin\Custom\Indicators\@ZLEMA.cs`
  dated **2026-01-15**.

### Root cause (diagnosed)
NT8 normally auto-generates `@Strategy.cs` (the Strategy partial class's
factory methods for user indicators) during compile. On the client's
machine, NT8 was NOT regenerating this file even though the 7 TV_*
indicators themselves compiled and were visible in the NinjaScript
Explorer.

Most likely trigger: I had placed the 7 indicators and the strategy in
sub-namespaces `NinjaTrader.NinjaScript.Indicators.TVPort` and
`NinjaTrader.NinjaScript.Strategies.TVPort`. Some NT8 builds do not
auto-generate factory-method partial-class extensions for indicators in
sub-namespaces — the generator walks the default namespace only. This
is not documented but is a well-known NT8 quirk.

Freelancer's machine happened to handle it (likely due to a fresh
`@Strategy.cs`); client's machine (which had a pre-existing stale
`@Strategy.cs` from another project) did not regenerate it.

### Fix
Moved all 8 code files to the canonical NT8 namespaces:
- 7 indicators: `NinjaTrader.NinjaScript.Indicators.TVPort` →
  `NinjaTrader.NinjaScript.Indicators`.
- Strategy: `NinjaTrader.NinjaScript.Strategies.TVPort` →
  `NinjaTrader.NinjaScript.Strategies`.
- Updated the strategy's `using` from `…Indicators.TVPort` to
  `…Indicators`.

No other code changes required. The `TV_*` name prefix is already
distinct enough to avoid collisions with NT8 built-ins.

`TV_SharedEnums.cs` (the AddOn) left in `…AddOns.TVPort` — it is not
referenced by any indicator or strategy after the earlier cleanup passes,
and AddOns do not need to be in the canonical namespace for NT8
auto-gen purposes.

### Lesson for future NT8 work (final corrected version)
**Always put public Indicator and Strategy classes directly in
`NinjaTrader.NinjaScript.Indicators` and `NinjaTrader.NinjaScript.Strategies`
— not in sub-namespaces.** NT8's factory-method auto-generator does not
reliably handle sub-namespaces on all NT8 builds.

### Action for user (freelancer) / client
- Freelancer: recompile on their machine to confirm the namespace change
  still works there.
- Client: replace the 7 `TV_*.cs` indicator files + the
  `TV_RenkoRangeStrategy.cs` strategy file with the newly-pushed versions,
  then F5. If `@Strategy.cs` still appears stale after a compile cycle,
  deleting it manually and recompiling forces NT8 to regenerate it.
