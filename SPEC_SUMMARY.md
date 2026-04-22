# Spec Summary — Version 1

Interpreted from: `project_status&plan.md`, `BUY SELL ARTICULATION NT8.txt`,
`XTBUILDER2.8.4 PROTOTYPE.txt`, client chat, and the six Pine sources.
Truth priority: BUY SELL ARTICULATION → XTBUILDER → Pine → screenshots.

## 1. What the strategy is

Seven-indicator TradingView setup where each indicator feeds the next as a
source. The final stage, Range Filter, emits buy / sell signals. Upstream
"checked" indicators act both as **source** (their output is the next
indicator's input) and as a **confirmation filter** (all checked indicators
must agree with the signal direction).

## 2. Default source chain

```
Close → Center of Gravity → (COG: LSMA) → macZLSMA → ZLSMA → LSMA Crossover.Trigger → SLSMA → Stoch RVI.K → Range Filter
                                                                                                              (+ optional Technical Ratings)
```

Technical Ratings is last, unchecked by default, and does not participate in
the chain — it sits alongside as an optional confirmation vote.

**Note (2026-04-22 PM):** As of this date, every chain indicator has a
**Source** dropdown on the strategy panel. Defaults above reproduce the
canonical chain, but Sean can re-wire any stage's input to any other
indicator's output at runtime (TV-style flex). Available source options:
`Close`, `COG: Plot / LSMA / Trigger`, `macZLSMA: Plot / Trigger`,
`ZLSMA: Plot`, `LSMA Crossover: LSMA / Trigger`, `SLSMA: Plot`,
`Stoch RVI: K / D`. The prior `UseCOGInChain` toggle was removed —
macZLSMA's Source dropdown subsumes it (pick `Close` to bypass COG, pick
`COG: LSMA` to include it). Default for macZLSMA's source is `COG: LSMA`
per Sean's stated preference ("the COG LSMA source is the preferred source
to pull the data from").

## 3. Default settings (locked by client)

| Indicator | Settings |
| --- | --- |
| Center of Gravity *(added 2026-04-22, settings corrected PM)* | length 8, smoothing NONE (SMA alt, length 3), LSMA length 200, prev hi/lo length 20, fib length 1000, trigger ALMA window 3 / offset 0.85 / sigma 6, source Close |
| macZLSMA | length 2, offset 0, trigger 3, source **COG: LSMA** (default — user-selectable via Source dropdown) |
| ZLSMA | length 2, offset 0, source macZLSMA |
| LSMA Crossover | length 2, offset 0, trigger 4, source ZLSMA |
| SLSMA | length 2, offset 0, source LSMAC.Trigger (reconstructed indicator) |
| Stoch RVI | RVI length 6, K 2, D 2, stoch length 14, source SLSMA |
| Range Filter | sampling 240, multiplier 0.1, source Stoch RVI K |
| Technical Ratings *(rebuilt monolithic 2026-04-22 PM)* | OFF by default (`Use Technical Ratings as filter` toggles it). Defaults: Rating Uses = **Oscillators Only**, MA Weight 30%, Longs Level **0.1**, Shorts Level **−0.1** (updated 2026-04-22 PM after Sean's live test — original TV-derived 0.5 levels were too restrictive against our approximation's value distribution). 12 MAs (SMA + EMA × 10/20/30/50/100/200) + 7 oscillators (RSI 14, CCI 20, MACD diff, ADX 14, Stoch K, Williams %R 14, Momentum 10) each vote ±1/0; combined per Rating Uses + MA Weight; direction set when total crosses Long/Short levels. Used as a 7th alignment vote. |

## 4. Mechanical rules (per client's final wording)

- **Long entry** — all checked indicators agree on the up direction AND
  Range Filter fires `longCondition`.
- **Short entry** — all checked indicators agree on the down direction AND
  Range Filter fires `shortCondition`.
- **Long exit** — aligned opposite (SELL) signal appears.
- **Short exit** — aligned opposite (BUY) signal appears.

## 5. Reversal (explicitly requested)

- `ReversalEnabled = true` (default) — opposite signal **directly reverses**
  the position (no intermediate flatten step; a single reversing NT8 order
  is used — "sell 2" style as discussed in chat).
- `ReversalEnabled = false` — opposite signal **closes only**, no
  counter-entry on that bar.
- `FlattenFirst` — optional toggle, not default. When `true`, reversals go
  close → wait one bar → re-enter. Used only if the user wants maximal
  safety at the cost of missing the reversal bar.

## 6. Session

- NYC time zone.
- Default window: **09:33 → 12:00** (primary focus 09:33 → 10:30).
- Force-flat at session end (hard close, both directions).
- No new entries outside the session window.
- Session handling uses `TimeZoneInfo` so DST is correct.

## 7. Instrument & chart

- Default instrument: **MNQ** (front month, user picks). Strategy default
  `Dataseries` is the chart instrument.
- Execution instrument = signal instrument for version 1. The plan leaves
  an "alternate later" door open for NQ; that is **not** wired into
  version 1.
- Chart: **1-second, 6-box Renko**. NT8 does not have a one-to-one
  "Traditional Renko" equivalent — closest defensible mapping is
  "**Renko**" bar type with Brick Size 6 (ticks), chart trigger on
  `BarsPeriodType.Renko`. Any residual chart difference is noted in
  `ASSUMPTIONS_LOG.md`.

## 8. Execution mode

- **Version 1 = `Calculate.OnBarClose`** — matches the TradingView alert
  builder flow the client currently trusts.
- Future: optional earlier-fire mode can be layered on after parity is
  confirmed. Left out of version 1 to keep behavior deterministic.

## 9. Ambiguity resolved

- **"Checked indicators align"** — interpreted as: for each indicator the
  user has `UseAsFilter = true`, its directional read (+1/−1) must equal
  the Range Filter signal direction. Unchecked indicators contribute
  nothing — neither block nor confirm. This matches the client's
  description *"Unchecked indicators should not block trading logic"*.
- **Long/short entry wording difference** — client phrased it as
  *"if all checked indicators align a BUY signal appears"*; I phrased it
  as *"when buy signal appears and all checked indicators align"*. These
  are logically equivalent. Code uses the conjunctive form.
- **SLSMA missing source** — reconstructed as `linreg(linreg(src, L, O), L, O)`
  per plan's description (*"linreg smoothing against linreg(lsma)"*).
  This is labelled **APPROXIMATE** in `ASSUMPTIONS_LOG.md`. The reconstruction
  differs from ZLSMA by omitting the `+ (lsma − lsma2)` correction term,
  giving a slightly lagging but smooth line — consistent with the
  screenshots of "SLSMA Pullbacks" the client shared.
- **Technical Ratings** — library-dependent. Delivered as a labelled
  approximation, disabled by default. Not part of default signal path.
