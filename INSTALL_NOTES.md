# NT8 Install & Setup Notes

## 1. File placement

Copy the nine files below into the listed folders under
`Documents\NinjaTrader 8\bin\Custom\`:

| Source (this repo)                                   | Destination in NT8                                   |
| ---------------------------------------------------- | ---------------------------------------------------- |
| `nt8/AddOns/TV_SharedEnums.cs`                       | `AddOns\TV_SharedEnums.cs`                           |
| `nt8/Indicators/TV_MacZLSMA.cs`                      | `Indicators\TV_MacZLSMA.cs`                          |
| `nt8/Indicators/TV_ZLSMA.cs`                         | `Indicators\TV_ZLSMA.cs`                             |
| `nt8/Indicators/TV_LSMACrossover.cs`                 | `Indicators\TV_LSMACrossover.cs`                     |
| `nt8/Indicators/TV_SLSMA.cs`                         | `Indicators\TV_SLSMA.cs`                             |
| `nt8/Indicators/TV_StochRVI.cs`                      | `Indicators\TV_StochRVI.cs`                          |
| `nt8/Indicators/TV_RangeFilter.cs`                   | `Indicators\TV_RangeFilter.cs`                       |
| `nt8/Indicators/TV_TechnicalRatingsApprox.cs`        | `Indicators\TV_TechnicalRatingsApprox.cs`            |
| `nt8/Strategies/TV_RenkoRangeStrategy.cs`            | `Strategies\TV_RenkoRangeStrategy.cs`                |

## 2. Compile

1. Open NinjaTrader 8.
2. Open the NinjaScript Editor (`Tools → Edit NinjaScript → Indicator`, any
   indicator — this opens the editor with the solution loaded).
3. Press **F5** to compile the whole solution. You should see
   *"Compile succeeded"* with no errors.
4. If NT8 reports "namespace already exists" or name collisions, make sure
   you have not left an older copy of any file in the folder.

## 3. Chart setup (default intent)

The client's TradingView chart is a **Traditional Renko, box size 6, 1-second
data**. NT8 does not have a one-to-one "Traditional Renko" type. The
closest defensible NT8 setup is:

- Instrument: **MNQ** (front-month contract; user picks at strategy load).
  The plan lists MNQ1! (TV continuous) as default and NQ1! as alternate.
- Data series type: **Renko**.
- Brick size (`Value`): **24** — see critical note below.
- Price based on: **Last**.
- Load data based on: **Days** → **Days to load = 5**.
- Bar width: 3 (cosmetic only).

### ⚠️ CRITICAL: NT8 Renko `Value` is in TICKS, not points

TradingView's "Traditional Renko" box size is expressed in **price points**.
NT8's built-in "Renko" bar type `Value` field is expressed in **ticks**. For
MNQ (tick size 0.25), 1 point = 4 ticks, so:

| Platform / setting             | Brick size in price terms |
| ------------------------------ | ------------------------- |
| TV Traditional Renko, box 6    | **6.00 points**           |
| NT8 Renko, `Value = 6`         | 1.50 points (WRONG)       |
| NT8 Renko, **`Value = 24`**    | **6.00 points** (correct) |

Using `Value = 6` on NT8 produces bricks 4× smaller than TV's, which means
~4× more bricks on the same underlying tick data and therefore ~4× more
Range Filter signals. **Always set `Value = 24`** to match the TV box
size 6 on MNQ. For other instruments, multiply the TV box size (points)
by (1 / tick size) to get the NT8 `Value`.

### Residual NT8↔TV brick-formation difference

Even with matched brick size, NT8's Renko formation rule and TradingView's
Traditional Renko formation rule are not byte-identical (NT8 computes
bricks tick-by-tick using a last-trade reference; TV uses its own
formation algorithm). This remaining difference is small and is the
expected source of any minor signal timing drift after the `Value = 24`
fix. A community "TradingView-style Renko" NT8 bar type can close this
further if exact parity is required — see the external resources in
`INSTALL_NOTES.md §8` for safe search paths. Per client, signal behavior
is the priority and chart naming parity is not required.

## 4. Trading hours template

The strategy enforces its own session window (`SessionStart` /
`SessionEnd`) in NY time, independent of NT8's trading-hours template.
No custom template is strictly required. For cleanest fills you can still
use the built-in **CME US Index Futures RTH** template.

Do not enable NT8's "Exit on session close" feature in addition —
`ForceFlatAtSessionEnd` already handles this inside the strategy. (The
strategy sets `IsExitOnSessionCloseStrategy = false` for this reason.)

## 5. Strategy parameters (defaults on load — matches client spec)

- Quantity: 1
- **Reversal mode**: ReversalEnabled = true, FlattenFirst = false.
- **Session**: 09:33 NY → 12:00 NY, Force Flat at Session End = true.
- **Alignment filters**: macZLSMA / ZLSMA / LSMA Crossover / SLSMA /
  Stoch RVI checked. Technical Ratings unchecked (and the indicator
  itself disabled).
- **macZLSMA**: length 2, offset 0, trigger 3.
- **ZLSMA**: length 2, offset 0.
- **LSMA Crossover**: length 2, offset 0, trigger 4, long 200, extra-long 1000.
- **SLSMA**: length 2, offset 0.
- **Stoch RVI**: RVI length 6, K 2, D 2, stoch length 14.
- **Range Filter**: sampling 240, multiplier 0.1.
- **Technical Ratings (approx)**: MA weight 30%.

## 6. Running the strategy

1. Open a chart with the settings in §3.
2. Right-click → **Strategies → TV_RenkoRangeStrategy**.
3. Review parameters; defaults match the client spec.
4. Set **Calculate = On bar close** (this is the default baseline for v1).
5. Click **Enable**.

The strategy draws visible **Buy / Sell arrows** on every triggered
signal bar (Lime up-arrow + "Buy" text below the bar for longs; Red
down-arrow + "Sell" above the bar for shorts) — these mirror Pine's
`plotshape(longCondition, "Buy", …)` output so side-by-side TV↔NT8
validation can be eyeballed directly.

The monolithic strategy (2026-04-22 onward) does **not** automatically
add the chain indicators to the chart because it inlines all the math
itself rather than wiring separate indicator instances. The standalone
indicator files (`TV_MacZLSMA.cs`, `TV_ZLSMA.cs`, etc.) still compile
and can be added to the chart by hand from the Indicators dialog if
you want to visually see each chain stage while validating.

## 7. TradingView → NT8 difference summary (expected)

| Concern                  | TradingView                                | NT8                                                  |
| ------------------------ | ------------------------------------------ | ---------------------------------------------------- |
| Renko brick unit         | Box size in **points**                     | `Value` in **ticks** — use `Value = 24` for MNQ to match TV box 6 |
| Renko brick formation    | Traditional                                | Renko (may still differ slightly in boundary timing even at matched size) |
| Indicator linreg         | Pine `linreg`                              | Manual implementation — numerically identical        |
| Pine `ema` seeding       | Seeds with first input                     | Manual implementation — identical                    |
| Pine `stdev`             | Population stdev                           | Manual implementation — population stdev             |
| Technical Ratings        | Closed library `TechnicalRating/3`         | Labelled approximation; off by default               |
| Reversal order pattern   | Alert-builder `reverse` message            | ExitShort + EnterLong on same bar (net reversal)     |
| Session time zone        | TradingView uses chart's configured TZ     | `TimeZoneInfo` conversion to NY; DST-safe            |
| OnBarClose semantics     | Pine v5 `barstate.isconfirmed`             | NT8 `Calculate.OnBarClose`                           |

## 8. Troubleshooting

- **Signal count is much higher on NT8 than on TV**: almost certainly the
  NT8 Renko `Value` is set in ticks but the user entered TV's box-size
  number. For MNQ: set NT8 `Value = 24` (not 6) to match TV box 6. See §3.
- **Strategy shows zero trades**: verify `BarsRequiredToTrade = 1000` has
  elapsed on the chart. The 1000-bar warm-up is chosen so Range Filter's
  long-period EMA is fully converged before any trade fires. With
  `Days to load = 5` (recommended), the strategy will already have 1000+
  pre-session bars loaded before 09:33 NY, so this is transparent in
  normal use. If you need faster warm-up for quick testing, lower it in
  the strategy parameter UI — but be aware the first ~500 bars may not
  match TV exactly due to EMA warm-up sensitivity.
- **"No such time zone 'Eastern Standard Time'"**: on Linux/Mac we fall
  back to `"America/New_York"`. On Windows this should always resolve.
- **Reversal fires on same bar but position stays flat**: check the order
  history — NT8 processed ExitShort then EnterLong but EnterLong was
  rejected. Common cause: `EntriesPerDirection` was increased elsewhere.
  The strategy sets it to 1 on SetDefaults; leave it there.
- **Technical Ratings stays at zero**: the indicator needs 200 bars for
  the longest SMA. Either wait or leave it unchecked (default).
- **Looking for a TV-style Renko bar type**: search
  `ninjatraderecosystem.com` or `futures.io` for "TradingView Renko" or
  "Traditional Renko NT8". Prefer named developers with reviews over
  anonymous uploads. With NT8 `Value = 24` already matched to TV box 6,
  an add-on is only needed if residual brick-formation differences
  remain objectionable — it is not required for functional operation.
