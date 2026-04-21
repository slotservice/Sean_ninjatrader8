# Assumptions Log

Every assumption made while building the port, labelled by confidence level.

Legend:
- **EXACT**  — can be proven by source-reading; behavior matches Pine bit-for-bit or within rounding.
- **APPROX** — documented approximation, not claimed as exact parity.
- **OPEN**   — unresolved, may require client input.

---

## A. Indicator math

| # | Area | Assumption | Label |
|---|------|------------|-------|
| A1 | Pine `linreg(src, length, offset)` | Endpoint of least-squares fit with x=0 oldest, x=length-1 newest, result = `intercept + slope * (length-1-offset)`. Manual NT8 implementation in every indicator. | EXACT |
| A2 | Pine `sma(src, length)` | Arithmetic mean over last `length` bars. Manual loop. | EXACT |
| A3 | Pine `stdev(src, length)` | **Population** stdev (divides by N). Used inside Stoch RVI's `stddev = stdev(src, length)`. | EXACT |
| A4 | Pine `ema(src, length)` | Alpha = `2/(length+1)`, seeded with the first input value on the first bar; recursive from bar 1 onward. | EXACT |
| A5 | Pine `stoch(a, b, c, n)` | `100 * (a - lowest(c, n)) / (highest(b, n) - lowest(c, n))`. In Stoch RVI `a = b = c = rvi`, so simplifies to `100 * (rvi - min) / (max - min)` over window. | EXACT |
| A6 | Pine `change(src)` inside Stoch RVI | `src - src[1]`; zero on bar 0. | EXACT |
| A7 | Pine `nz(x[1])` | Treated as `0` when prior value isn't set. NT8 `Series<double>` default-initialises to 0, so `series[1]` on bar 1 returns 0 — identical numeric result. | EXACT |
| A8 | ZLSMA direction | ZLSMA has no internal trigger line in Pine. Direction is derived from `zlsma[0] vs zlsma[1]` (slope-up / slope-down). Matches standard trader reading of a zero-lag LSMA. | APPROX (no Pine spec for direction) |
| A9 | SLSMA entire indicator | Original Pine source not delivered. Reconstructed as `linreg(linreg(src, L, O), L, O)` per plan's description *"linreg smoothing against linreg(lsma)"*. Cross-checked against client screenshots (`SLSMA PULLBACKS*.jpg`, `slsma pullbacks 4 setting default.jpg`) — line behavior visually consistent. | APPROX — documented |
| A10 | SLSMA direction | Rising / falling of SLSMA itself, same rule as A8. | APPROX |
| A11 | macZLSMA direction | `zlsma2 > trig2` → up; `<` → down; matches the Pine color logic (green/red). | EXACT |
| A12 | LSMA Crossover direction | `lsma > trigger` → up; mirrors the Pine "crossover" construction. | EXACT (matches indicator name & intent) |
| A13 | Stoch RVI direction | `K > D` → up. Standard reading of Stoch RVI cross. | APPROX (Pine file does not declare a "direction"; this is the established visual read) |
| A14 | Range Filter direction (used in alignment vote) | `upward > 0` → up, `downward > 0` → down. Mirrors Pine's bar-color rule. | EXACT |
| A15 | Range Filter signal bar | `longCondition = longCond AND CondIni[1] == -1` — one-shot transition, ported verbatim. | EXACT |
| A16 | Pine `cog(src, length)` | `-Σ(src[i] * (i+1)) / Σ(src[i])` for i=0..length-1, where i=0 is the most recent bar. Standard Center of Gravity formula. Manual loop in `ComputeChain` Stage 0. | EXACT |
| A17 | Pine `alma(src, window, offset, sigma)` | `m = offset*(window-1)`, `s = window/sigma`, `weight[i] = exp(-(i-m)² / (2s²))`, result = `Σ(weight[i] * src[window-1-i]) / Σ(weight[i])`. Standard Arnaud Legoux MA formula; `src[window-1-i]` indexing matches Pine's so the heaviest weight lands on the most recent bar when offset ≈ 0.85. | EXACT |
| A18 | COG smoothing variant | Pine source offers `NONE` or `RMA`. Sean's spec calls for `NONE` or `SMA`. Implemented as Sean specified — toggle is `COGSmoothingEnabled` (off = NONE, on = SMA over `COGSmoothingLength`). RMA path from Pine is **not** ported. | EXACT-of-spec, **DEVIATION-from-Pine** (intentional, per client) |
| A19 | COG direction (used in alignment vote) | `raw_cog > trigger` → +1, `raw_cog < trigger` → −1. Mirrors Pine's `enter = crossover(COG1, trigger)` — note Pine compares the *raw* COG to the trigger, not the smoothed `COG`. | EXACT |
| A20 | COG visual-only Pine inputs (Prev High/Low Length, LSMA Length, Fib Length) | NOT exposed on the strategy panel — they have no effect on signal logic. Belong on a standalone `TV_COG.cs` chart indicator if added later. | EXACT (signal-irrelevant) |
| A21 | COG → macZLSMA chain insertion | When `UseCOGInChain = true` (default), macZLSMA's source switches from `Close` to `s_cog_plot` (the smoothed-or-not COG output). When false, macZLSMA reads `Close` as in the original spec — making COG-vs-no-COG a one-click A/B test. Every downstream stage automatically inherits the change because they each read from their immediate upstream stage. | EXACT-of-spec |

## B. Alignment semantics

| # | Assumption | Label |
|---|------------|-------|
| B1 | "Checked indicators align" = every indicator with `UseAsFilter = true` must have direction equal to the signal direction. Unchecked indicators contribute nothing. | EXACT (matches client wording: "Unchecked indicators should not block trading logic") |
| B2 | The Range Filter itself is the signal source, not a filter vote — it is not in the alignment list (its direction is implicit in its signal). | EXACT |
| B3 | A neutral (0) direction from a checked indicator is treated as **not aligned** (blocks the signal). Rationale: "participate in confirmation" means confirm, not abstain. | APPROX |

## C. Reversal / order handling

| # | Assumption | Label |
|---|------------|-------|
| C1 | `ReversalEnabled = true` → opposite signal closes AND re-enters on the same bar (ExitShort + EnterLong submitted in sequence). NT8's sequencing makes the net effect a single bar reversal. The "sell 2" mental model from chat is equivalent to this NT8 pattern. | EXACT-of-intent |
| C2 | `ReversalEnabled = false` → opposite signal exits only, no new entry that bar. | EXACT |
| C3 | `FlattenFirst = true` (optional, not default) → exit on signal bar, track a `pendingReentryDir`, re-enter only on the next bar that produces a same-direction confirming signal AND position is flat. | EXACT-of-intent |
| C4 | `EntriesPerDirection = 1` — no scaling in. | EXACT |
| C5 | Same-direction repeat signal while already in position → NT8 ignores (via `EntriesPerDirection`). | EXACT |

## D. Session

| # | Assumption | Label |
|---|------------|-------|
| D1 | Session times are **NYC local time** (Eastern time with DST handled via `TimeZoneInfo`). | EXACT |
| D2 | Default window 09:33–12:00; primary focus 09:33–10:30 (client's stated scalp window). Default window is the tradable band; focus window is noted in docs but not separately enforced. | EXACT |
| D3 | `Time[i]` is in `Core.Globals.GeneralOptions.TimeZoneInfo`; we convert to NY explicitly. | EXACT (NT8 API) |
| D4 | Force-flat at session end fires on the **first bar** whose NY time ≥ SessionEnd AND the prior bar's NY time was < SessionEnd. Uses edge-detection to avoid re-submitting exits on subsequent bars. | EXACT |

## E. Chart type

| # | Assumption | Label |
|---|------------|-------|
| E1 | **CORRECTED 2026-04-22** — TradingView's "Traditional Renko" box size is expressed in **price points**, but NT8's built-in "Renko" bar type `Value` is expressed in **ticks**. For MNQ (tick 0.25), TV box 6 = 6 points = **NT8 `Value = 24`**. The initial setup used `Value = 6` which is 1.5 points per brick — 4× smaller than TV's 6-point bricks — which produced ~4× the signal count. Fix: always set `Value = 24` for MNQ to match TV box 6. For other instruments, `Value = box_size_in_points / tick_size`. | EXACT once the unit conversion is applied |
| E1a | Even with brick size matched, NT8's Renko formation rule and TV's Traditional Renko formation rule are not byte-identical. This is the residual cross-platform difference after the E1 unit fix. A community "TradingView-style Renko" NT8 bar type can close it further if exact parity is required; otherwise acceptable per client's priority of signal behavior over chart naming. | APPROX — small, documented |
| E2 | 1-second underlying data series on both platforms. NT8 is configured with "Price based on = Last" per the client's data-base page notes. | EXACT (mirror of TV setup) |

## F. Technical Ratings

| # | Assumption | Label |
|---|------------|-------|
| F1 | `TradingView/TechnicalRating/3` library is closed. Exact parity impossible from Pine source alone. | EXACT (stated fact) |
| F2 | Delivered `TV_TechnicalRatingsApprox` implements the publicly-documented rating construction (12 MAs + 7 oscillators mapped to buy/neutral/sell and averaged). Directional agreement with TV is expected most of the time; bar-identical values are NOT expected. | APPROX — labelled everywhere |
| F3 | Default `UseTechnicalRatings = false` and indicator is **not created** in State.DataLoaded unless the user opts in. This prevents any signal influence when unused. | EXACT |

## G. Instrument

| # | Assumption | Label |
|---|------------|-------|
| G1 | Signal and execution instrument are the same for v1. Plan acknowledges an "alternate later" mode for NQ but does not require it in v1. Not implemented. | EXACT (per spec) |
| G2 | TradingView `MNQ1!` ≈ NT8 front-month MNQ contract (user-picked at strategy load). The continuous-contract semantics differ at roll dates. | APPROX (standard CME / TV difference) |

## H. Open items

| # | Item |
|---|------|
| H1 | 3–5 marked trade validation examples still pending from client. |
| H2 | Whether to expose a "separate execution instrument" (NQ) option — deliberately deferred to v2 per spec. |
| H3 | Whether to add a Calculate.OnEachTick earlier-entry mode — also deferred to v2 per client's *"start OnBarClose, improve later if possible"*. |
| H4 | Client originally said *"i sent the txt"* for the SLSMA Pine source, but the file is not in `files/`. May have been lost in transit. Visually cross-checked the reconstruction against `slsma 1.jpg` (simple SLSMA indicator with matching Source/Length/Offset fields) and confirmed the reconstruction matches the client's accepted simpler variant, not the full "SLSMA Pullbacks" (which has pb1/pb2 signal logic my reconstruction does not include — but per chat the client accepted the simpler SLSMA as the working reference). |
| H5 | Chart screenshots (`range and rvi settings.jpg`) show Stoch RVI source = LSMA Crossover, while XTBUILDER2.8.4 locks it to SLSMA. The locked spec (XTBUILDER) wins per truth priority; the implementation follows the locked spec. Worth confirming with the client on a future session that the locked chain is still what they want. |
