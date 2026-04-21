# Sean — TradingView → NinjaTrader 8 Strategy Port

NinjaScript port of a seven-indicator TradingView strategy, built for
live automation on a 1-second 6-box Renko chart of MNQ during the NYC
morning session (09:33–12:00 ET).

## What this is

The original strategy lives in TradingView as a chain of Pine Script
indicators feeding a final Range Filter signal generator. This repo
contains the NinjaTrader 8 port: each indicator re-implemented in
C# NinjaScript, wired into the same chain, with session enforcement and
three-mode reversal handling.

## Default source chain

```
Close → macZLSMA → ZLSMA → LSMA Crossover.Trigger
      → SLSMA    → Stoch RVI.K → Range Filter
                                 (+ optional Technical Ratings)
```

Range Filter is the final signal generator. Upstream checked indicators
both feed the chain as sources and contribute to an "all aligned" vote
before any entry.

## What's in the repo

```
nt8/
  AddOns/TV_SharedEnums.cs
  Indicators/
    TV_MacZLSMA.cs
    TV_ZLSMA.cs
    TV_LSMACrossover.cs
    TV_SLSMA.cs            # reconstructed — see ASSUMPTIONS_LOG §A9
    TV_StochRVI.cs
    TV_RangeFilter.cs
    TV_TechnicalRatingsApprox.cs   # labelled approximation — off by default
  Strategies/
    TV_RenkoRangeStrategy.cs

SPEC_SUMMARY.md           — interpreted spec in plain English
INSTALL_NOTES.md          — NT8 import, chart, and session setup
VALIDATION_CHECKLIST.md   — 8-section side-by-side TV vs NT8 verification
ASSUMPTIONS_LOG.md        — every assumption labelled EXACT / APPROX / OPEN
PROGRESS_LOG.md           — timestamped build + compile-fix history
```

## Quick start

1. Install NinjaTrader 8 (free for sim / backtest).
2. Copy the `nt8/AddOns`, `nt8/Indicators`, and `nt8/Strategies` file
   contents into the matching subfolders of
   `Documents\NinjaTrader 8\bin\Custom\`.
3. In NT8, open **NinjaScript Editor** and press **F5** to compile.
4. Follow the rest of `INSTALL_NOTES.md` for chart setup.

## Version 1 status

- Default execution: `Calculate.OnBarClose` (matches the client's current
  TV alert-builder flow).
- Default reversal: ON (opposite signal directly reverses the position).
  Flatten-first is available as an optional mode.
- Force-flat at session end: ON.
- Technical Ratings: OFF by default (library-dependent, labelled as a
  named approximation — see `ASSUMPTIONS_LOG.md`).

See `PROGRESS_LOG.md` for the build history and `VALIDATION_CHECKLIST.md`
for the side-by-side verification plan.
