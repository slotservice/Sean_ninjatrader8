# Validation Checklist — NT8 vs TradingView side-by-side

Use this checklist to verify parity between the NT8 port and the original
TradingView setup. Work top-to-bottom; do not proceed until each section
shows acceptable parity.

## 0. Before you start

- [ ] NT8 chart configured per `INSTALL_NOTES.md §3`.
- [ ] Strategy parameters left at defaults (no edits yet).
- [ ] TradingView chart: https://www.tradingview.com/chart/hL52xfFL/ (client-shared)
      open in a second monitor on the same instrument and 6-box Renko.
- [ ] Client provides 3–5 marked trade examples (date, approx NY time,
      expected entry/exit direction). *Pending — tracked in PROGRESS_LOG.*

## 1. Indicator plot parity

For each indicator, add it individually to a price chart using the same
inputs as the TV equivalent. Compare values on the **last closed bar**.

### 1a. macZLSMA
- [ ] TV Source = Close, NT8 Source = Close.
- [ ] TV length = 2, offset = 0, trigger = 3 — NT8 same.
- [ ] Main plot value matches to ±0.0001 of TV.
- [ ] Trigger value matches to ±0.0001.
- [ ] Color transition points (green↔red) on the same bars within ±1 bar.

### 1b. ZLSMA
- [ ] Input = macZLSMA plot (add TV macZLSMA in TV, use its plot as source).
- [ ] Length = 2, offset = 0.
- [ ] Value matches to ±0.0001.

### 1c. LSMA Crossover
- [ ] Input = ZLSMA plot.
- [ ] Length = 2, offset = 0, trigger = 4.
- [ ] LSMA and Trigger values match to ±0.0001.
- [ ] Long (200) and Extra-Long (1000) plots match after 200 / 1000 bars.

### 1d. SLSMA (reconstructed)
- [ ] Input = LSMA Crossover Trigger.
- [ ] Length = 2, offset = 0.
- [ ] Because the original Pine is unavailable, parity here is judged
      against the client's **"SLSMA PULLBACKS"** screenshots:
      line hugs price closely, shifts direction with the pullbacks.
- [ ] Direction flips line up with the marked pullback bars in
      `SLSMA PULLBACKS.jpg`, `SLSMA PULLBACKS 2.jpg`, `SLSMA PULLBACKS 3.jpg`
      (±1 bar).

### 1e. Stochastic RVI
- [ ] Input = SLSMA plot.
- [ ] RVI length 6, K 2, D 2, stoch length 14.
- [ ] K and D values match TV to ±0.05.
- [ ] K / D crossovers happen on the same bars (±1 bar).

### 1f. Range Filter
- [ ] Input = Stoch RVI K.
- [ ] Sampling 240, multiplier 0.1.
- [ ] Filt plot matches TV to ±0.001.
- [ ] High/Low bands match to ±0.001.
- [ ] "Buy"/"Sell" label plot positions match TV bars (±0 bars — any
      off-by-one here is a concern and must be investigated).

### 1g. Technical Ratings (approx) *(optional)*
- [ ] Only run this check if client wants Technical Ratings live.
- [ ] Directional agreement with TV expected; exact values are NOT
      expected to match due to closed library. Documented in
      `ASSUMPTIONS_LOG.md`.

## 2. Signal-bar parity

Focus window: **09:33–10:30 NY** (client's primary trading window).

The NT8 strategy draws its own **Buy / Sell arrows on the chart** (mirrors
Pine's `plotshape(longCondition, "Buy", …)`). Compare these marker-by-marker
against TradingView's Range Filter labels.

- [ ] Every "Buy" label on TV Range Filter has a matching NT8 lime up-arrow
      + "Buy" text on the same bar (±0 bars ideal, ±1 bar acceptable).
- [ ] Every "Sell" label on TV has a matching NT8 red down-arrow + "Sell".
- [ ] No extra signals on NT8 not present on TV.
- [ ] No extra signals on TV not present on NT8.

If signals diverge: record the bar time + the upstream indicator values on
both platforms so we can trace where the chain drifts.

## 3. Entry timing parity

- [ ] On a long buy label bar, NT8 strategy's Strategy Performance shows a
      long entry on the next bar's open at the bar-close price of the
      signal bar (OnBarClose semantics — same as the TV alert would fire).
- [ ] Fill price = signal bar's close within ±1 tick slippage allowance.

## 4. Exit timing parity

- [ ] Long exit: opposite aligned sell signal → NT8 closes on that bar.
- [ ] Short exit: opposite aligned buy signal → NT8 closes on that bar.
- [ ] Exit fill price = signal bar's close within slippage allowance.

## 5. Reversal behaviour

With `ReversalEnabled = true` (default) and `FlattenFirst = false`:

- [ ] On a reversal signal (currently short, long signal fires), NT8
      Strategy Performance shows two tickets on the same bar: an
      "ExitShort" and a "Long" entry.
- [ ] Net position after both fills = +Quantity (long).
- [ ] No intermediate bars spent flat.

With `ReversalEnabled = false`:

- [ ] Opposite signal closes the position only. NT8 shows one
      ExitShort/ExitLong ticket and **no** subsequent entry on that bar.

With `ReversalEnabled = true` + `FlattenFirst = true`:

- [ ] Opposite signal closes on bar N.
- [ ] No entry on bar N.
- [ ] Next bar with same-direction confirming signal submits the new entry.

## 6. Session close

- [ ] On the first bar whose NY time ≥ 12:00 (SessionEnd), any open
      position is force-closed via `ExitLong("SessionEnd") /
      ExitShort("SessionEnd")`.
- [ ] No new entries submitted after 12:00 NY until next session start.
- [ ] DST verification: run the test across a DST transition (or mock NY
      time). The strategy uses `TimeZoneInfo` so this should be
      transparent.

## 7. OnBarClose behaviour

- [ ] With `Calculate = OnBarClose`, strategy executes only at each bar's
      close. No intra-bar orders, no tick-level thrashing.
- [ ] Strategy logs (NT8 Output tab) show one OnBarUpdate invocation per
      bar during playback.

## 8. Historical playback

Run NT8 **Strategy Analyzer** on the last 5 trading days, 09:33–12:00 NY
only. Compare the trade list against TradingView bar replay:

- [ ] Number of trades per day matches TV within ±1.
- [ ] Per-trade direction matches.
- [ ] Entry and exit bar indices match (±1 bar allowance).
- [ ] Net PnL direction per day matches TV.

If all 8 sections pass, the port is signed off for forward simulation.
