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
Close → macZLSMA → ZLSMA → LSMA Crossover.Trigger → SLSMA → Stoch RVI.K → Range Filter
                                                                           (+ optional Technical Ratings)
```

Technical Ratings is last, unchecked by default, and does not participate in
the chain — it sits alongside as an optional confirmation vote.

## 3. Default settings (locked by client)

| Indicator | Settings |
| --- | --- |
| macZLSMA | length 2, offset 0, trigger 3, source Close |
| ZLSMA | length 2, offset 0, source macZLSMA |
| LSMA Crossover | length 2, offset 0, trigger 4, source ZLSMA |
| SLSMA | length 2, offset 0, source LSMAC.Trigger (reconstructed indicator) |
| Stoch RVI | RVI length 6, K 2, D 2, stoch length 14, source SLSMA |
| Range Filter | sampling 240, multiplier 0.1, source Stoch RVI K |
| Technical Ratings | OFF by default; MAs+Oscillators, MA weight 30%, columns |

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
