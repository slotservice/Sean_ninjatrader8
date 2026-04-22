// =============================================================================
// TV_RenkoRangeStrategy.cs  —  SELF-CONTAINED (monolithic) build
//
// This version inlines every indicator's math directly inside the strategy so
// it does NOT call the TV_MacZLSMA / TV_ZLSMA / TV_LSMACrossover / TV_SLSMA /
// TV_StochRVI / TV_RangeFilter classes as methods. That removes all dependency
// on NT8's factory-method auto-generator (@Strategy.cs regeneration), which
// was failing on the client's install and could not be repaired in place.
//
// The standalone indicator files (TV_MacZLSMA.cs etc.) still live in the
// Indicators folder — unchanged — so they can be added to charts by hand for
// visual comparison. The strategy simply no longer references them as types
// or methods.
//
// Default chain (from XTBUILDER2.8.4 + Sean 2026-04-22 extensions):
//     Close → Center of Gravity → macZLSMA → ZLSMA → LSMA Crossover.Trigger
//           → SLSMA → Stoch RVI.K → Range Filter
//
// As of 2026-04-22 PM, every chain indicator has a per-stage "Source" dropdown
// on the strategy panel — Sean can re-wire the chain at runtime to any TV-style
// configuration (e.g. SLSMA can read from COG: LSMA directly, skipping macZLSMA).
// Compute order remains fixed (COG → macZ → Z → LSMAC → SLSMA → StochRVI → RF).
// Picking a source from a stage that runs AFTER the current stage produces
// one-bar-lagged data — same behaviour as TV does in that edge case.
//
// Install: Documents\NinjaTrader 8\bin\Custom\Strategies\TV_RenkoRangeStrategy.cs
// =============================================================================

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    // Which component(s) of Technical Ratings contribute to the rating value.
    public enum TechRatingsUseMode
    {
        MAsOnly         = 0,
        OscillatorsOnly = 1,
        Both            = 2
    }

    // Which MA mix the Technical Ratings indicator votes with. Sean asked for the
    // long-MAs-only option to skip the short MAs that flip-flop on Renko.
    public enum TechRatingsMASetMode
    {
        Standard12 = 0,   // SMA + EMA at 10/20/30/50/100/200 (the documented TV mix subset)
        LongOnly6  = 1    // SMA + EMA at 50/100/200 only (less Renko noise)
    }

    // Available outputs that any chain indicator can use as its source.
    // Mirrors TradingView's "Source" dropdown — pick any other indicator's output.
    public enum TVChainSource
    {
        Close          = 0,
        COG_Plot       = 1,
        COG_LSMA       = 2,
        COG_Trigger    = 3,
        MacZ_Plot      = 4,
        MacZ_Trigger   = 5,
        ZLSMA_Plot     = 6,
        LSMAC_LSMA     = 7,
        LSMAC_Trigger  = 8,
        SLSMA_Plot     = 9,
        StochRVI_K     = 10,
        StochRVI_D     = 11
    }

    public class TV_RenkoRangeStrategy : Strategy
    {
        // ------------------------------------------------------------------
        // Intermediate series — allocated in State.DataLoaded.
        // ------------------------------------------------------------------
        // Stage 0: Center of Gravity (added 2026-04-22)
        private Series<double> s_cog_raw;       // raw cog(src, length)
        private Series<double> s_cog_plot;      // smoothed-or-not (Pine "COG")
        private Series<double> s_cog_trigger;   // ALMA trigger line (Pine "Trigger")
        private Series<double> s_cog_lsma;      // linreg of cog plot over LsmaLength (Pine "LSMA") — Sean's preferred chain source
        private Series<double> s_cog_dir;       // +1 if raw > trigger, -1 if raw < trigger, 0 otherwise

        // Stage 1: macZLSMA
        private Series<double> s_mz_lsma;
        private Series<double> s_mz_zlsma2;
        private Series<double> s_mz_plot;       // main plot (== zlsma2)
        private Series<double> s_mz_trigger;
        private Series<double> s_mz_dir;

        // Stage 2: ZLSMA
        private Series<double> s_z_lsma;
        private Series<double> s_z_plot;
        private Series<double> s_z_dir;

        // Stage 3: LSMA Crossover
        private Series<double> s_lc_lsma;       // also its main plot
        private Series<double> s_lc_trigger;
        private Series<double> s_lc_dir;

        // Stage 4: SLSMA (reconstructed — double-linreg smoother)
        private Series<double> s_sl_lsma;
        private Series<double> s_sl_plot;
        private Series<double> s_sl_dir;

        // Stage 5: Stochastic RVI
        private Series<double> s_sr_stddev;
        private Series<double> s_sr_upperRaw;
        private Series<double> s_sr_lowerRaw;
        private Series<double> s_sr_upperEma;
        private Series<double> s_sr_lowerEma;
        private Series<double> s_sr_rvi;
        private Series<double> s_sr_stochRaw;
        private Series<double> s_sr_k;
        private Series<double> s_sr_d;
        private Series<double> s_sr_dir;

        // Stage 7: Technical Ratings (rebuilt monolithic 2026-04-22 PM, Sean spec)
        // 12 MAs + 7 oscillators voting, combined per MA-weight, direction set at Long/Short levels.
        // *** APPROXIMATION *** — TV's exact implementation pulls from the closed
        // TradingView/TechnicalRating/3 library; this matches publicly-documented voting rules.
        private Indicators.SMA  tr_sma10, tr_sma20, tr_sma30, tr_sma50, tr_sma100, tr_sma200;
        private Indicators.EMA  tr_ema10, tr_ema20, tr_ema30, tr_ema50, tr_ema100, tr_ema200;
        private Indicators.RSI  tr_rsi14;
        private Indicators.CCI  tr_cci20;
        private Indicators.MACD tr_macd;
        private Indicators.ADX  tr_adx14;
        private Indicators.StochasticsFast tr_stoch;
        private Indicators.WilliamsR tr_wr14;
        private Indicators.Momentum tr_mom10;
        private Series<double> s_tr_maRating;
        private Series<double> s_tr_oscRating;
        private Series<double> s_tr_total;
        private Series<double> s_tr_dir;

        // Stage 6: Range Filter
        private Series<double> s_rf_absChange;
        private Series<double> s_rf_avrng;
        private Series<double> s_rf_avrngEma;
        private Series<double> s_rf_smrng;
        private Series<double> s_rf_filt;
        private Series<double> s_rf_upward;
        private Series<double> s_rf_downward;
        private Series<double> s_rf_condIni;
        private Series<double> s_rf_signal;     // +1 on long trigger bar, -1 on short, 0 otherwise
        private Series<double> s_rf_dir;

        // ------------------------------------------------------------------
        // Session + order bookkeeping
        // ------------------------------------------------------------------
        private TimeZoneInfo nyTz;
        private const int RviEmaLen = 14;               // Pine fixed internal EMA length inside Stoch RVI

        private const string SIG_LONG        = "Long";
        private const string SIG_SHORT       = "Short";
        private const string SIG_EXIT_LONG   = "ExitLong";
        private const string SIG_EXIT_SHORT  = "ExitShort";
        private const string SIG_SESSION_END = "SessionEnd";

        private int pendingReentryDir;
        private int pendingReentryBar;

        // SignalConfirmationBars (delayed-entry) bookkeeping — added 2026-04-23
        // When a signal fires, queue here for N bars. Promote to actual entry only
        // if Range Filter direction still matches signalDir at the end of the wait.
        private int pendingSignalDir = 0;   // 0 = none, +1 = pending long, -1 = pending short
        private int pendingSignalBar = 0;   // CurrentBar when the original signal fired

        // Anti-chop bookkeeping (used by group "05 Anti-chop filters" params)
        private int lastTradeBar = -1;   // CurrentBar of most recent entry OR exit
        private int entryBar     = -1;   // CurrentBar at which the current open position was entered

        // Risk management bookkeeping (used by group "06 Risk management" params)
        private double dailyStartCumProfit = 0.0;   // SystemPerformance baseline at session start
        private bool   dailyResetDone      = false; // edge-detection so reset fires once per session entry
        private bool   dailyLimitHit       = false; // true once today's PnL hits -DailyLossLimit
        private int    tradesToday         = 0;     // count of new entries fired this session

        // ==================================================================
        // State lifecycle
        // ==================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "TradingView → NT8 port (monolithic): Center of Gravity → macZLSMA → ZLSMA → LSMA Crossover → SLSMA → Stoch RVI → Range Filter. NYC-session automated Renko strategy.";
                Name        = "TV_RenkoRangeStrategy";
                Calculate   = Calculate.OnBarClose;

                EntriesPerDirection              = 1;
                EntryHandling                    = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy     = false;
                ExitOnSessionCloseSeconds        = 30;
                IsFillLimitOnTouch               = false;
                MaximumBarsLookBack              = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution              = OrderFillResolution.Standard;
                Slippage                         = 0;
                StartBehavior                    = StartBehavior.WaitUntilFlat;
                TimeInForce                      = TimeInForce.Gtc;
                TraceOrders                      = false;
                RealtimeErrorHandling            = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling               = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade              = 1000;
                IsInstantiatedOnEachOptimizationIteration = true;

                Quantity              = 1;
                ReversalEnabled       = true;
                FlattenFirst          = false;
                ForceFlatAtSessionEnd = true;

                SessionStart = new TimeSpan(9, 33, 0);
                SessionEnd   = new TimeSpan(12, 0, 0);

                UseCOGFilter         = true;
                UseMacZLSMAFilter    = true;
                UseZLSMAFilter       = true;
                UseLSMACFilter       = true;
                UseSLSMAFilter       = true;
                UseStochRVIFilter    = true;
                UseTechRatingsFilter = false;   // off by default — Sean opts in when he wants the scalp helper

                // Technical Ratings (rebuilt 2026-04-22 PM, Sean spec).
                // Defaults updated 2026-04-22 PM after Sean's live test: Oscillators Only +
                // 0.1/-0.1 thresholds were the sweet spot. MAs Only / Both at his original
                // TV-derived 0.5 levels were too restrictive (approximation distribution
                // differs from TV's closed-library version — see ASSUMPTIONS §F4–F7).
                TechRatingsUses        = TechRatingsUseMode.OscillatorsOnly;
                TechRatingsMAWeight    = 30;
                TechRatingsLongLevel   = 0.1;
                TechRatingsShortLevel  = -0.1;
                TechRatingsMASet       = TechRatingsMASetMode.Standard12;

                // Center of Gravity (Sean spec 2026-04-22 PM, defaults tuned 2026-04-22 evening)
                COGLength            = 8;
                COGSmoothingEnabled  = false;   // NONE by default; SMA is the alternative
                COGSmoothingLength   = 3;
                COGLsmaLength        = 202;     // tuned by Sean for closer TV-chart parity (was 200)
                COGPrevHiLoLength    = 20;      // visual-only on standalone, exposed for transparency
                COGFibLength         = 1000;    // visual-only on standalone, exposed for transparency
                COGTriggerWindow     = 3;       // Pine ALMA default
                COGTriggerOffset     = 0.85;    // Pine ALMA default
                COGTriggerSigma      = 5.0;     // tuned by Sean for closer TV-chart parity (was 6)

                // Per-stage Source dropdowns (TV-style flex). Defaults reproduce the
                // canonical chain: COG: LSMA → macZLSMA → ZLSMA → LSMAC → SLSMA → StochRVI → RF.
                MacZSource     = TVChainSource.COG_LSMA;
                ZLSMASource    = TVChainSource.MacZ_Plot;
                LSMACSource    = TVChainSource.ZLSMA_Plot;
                SLSMASource    = TVChainSource.LSMAC_Trigger;
                StochRVISource = TVChainSource.SLSMA_Plot;
                RangeFilterSource = TVChainSource.StochRVI_K;

                MacZLength        = 2;
                MacZOffset        = 0;
                MacZTriggerLength = 3;

                ZLSMALength = 2;
                ZLSMAOffset = 0;

                LSMACLength        = 2;
                LSMACOffset        = 0;
                LSMACTriggerLength = 4;

                SLSMALength = 2;
                SLSMAOffset = 0;

                RVILength   = 6;
                StochK      = 2;
                StochD      = 2;
                StochLength = 14;

                SamplingPeriod  = 240;
                RangeMultiplier = 0.1;

                // Anti-chop filters — all default to 0 (disabled). Existing behavior is unchanged unless enabled.
                MinBarsBetweenEntries  = 0;
                MinHoldBars            = 0;
                SignalConfirmationBars = 0;

                // Risk management — all default to 0 (disabled). Existing behaviour unchanged unless enabled.
                StopLossTicks     = 0;     // hard stop in ticks (NT8 native unit). 0 disables.
                ProfitTargetTicks = 0;     // hard target in ticks. 0 disables.
                MaxTradesPerDay   = 0;     // max entries per session. 0 disables.
                DailyLossLimit    = 0.0;   // session loss limit in account currency (USD). 0 disables.
            }
            else if (State == State.Configure)
            {
                // Wire NT8's auto-attached stop/target orders. Called once per strategy
                // load — user must restart strategy to apply changes to these params.
                if (StopLossTicks > 0)
                    SetStopLoss(CalculationMode.Ticks, StopLossTicks);
                if (ProfitTargetTicks > 0)
                    SetProfitTarget(CalculationMode.Ticks, ProfitTargetTicks);
            }
            else if (State == State.DataLoaded)
            {
                try   { nyTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
                catch { nyTz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }

                s_cog_raw     = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_cog_plot    = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_cog_trigger = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_cog_lsma    = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_cog_dir     = new Series<double>(this, MaximumBarsLookBack.Infinite);

                s_mz_lsma     = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_mz_zlsma2   = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_mz_plot     = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_mz_trigger  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_mz_dir      = new Series<double>(this, MaximumBarsLookBack.Infinite);

                s_z_lsma      = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_z_plot      = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_z_dir       = new Series<double>(this, MaximumBarsLookBack.Infinite);

                s_lc_lsma     = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_lc_trigger  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_lc_dir      = new Series<double>(this, MaximumBarsLookBack.Infinite);

                s_sl_lsma     = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_sl_plot     = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_sl_dir      = new Series<double>(this, MaximumBarsLookBack.Infinite);

                s_sr_stddev   = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_sr_upperRaw = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_sr_lowerRaw = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_sr_upperEma = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_sr_lowerEma = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_sr_rvi      = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_sr_stochRaw = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_sr_k        = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_sr_d        = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_sr_dir      = new Series<double>(this, MaximumBarsLookBack.Infinite);

                // Technical Ratings — NT8 built-in indicator instances (safe, framework, not auto-gen).
                tr_sma10  = SMA(10);   tr_sma20  = SMA(20);   tr_sma30  = SMA(30);
                tr_sma50  = SMA(50);   tr_sma100 = SMA(100);  tr_sma200 = SMA(200);
                tr_ema10  = EMA(10);   tr_ema20  = EMA(20);   tr_ema30  = EMA(30);
                tr_ema50  = EMA(50);   tr_ema100 = EMA(100);  tr_ema200 = EMA(200);
                tr_rsi14  = RSI(14, 3);
                tr_cci20  = CCI(20);
                tr_macd   = MACD(12, 26, 9);
                tr_adx14  = ADX(14);
                tr_stoch  = StochasticsFast(3, 14);
                tr_wr14   = WilliamsR(14);
                tr_mom10  = Momentum(10);

                s_tr_maRating  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_tr_oscRating = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_tr_total     = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_tr_dir       = new Series<double>(this, MaximumBarsLookBack.Infinite);

                s_rf_absChange= new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_rf_avrng    = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_rf_avrngEma = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_rf_smrng    = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_rf_filt     = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_rf_upward   = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_rf_downward = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_rf_condIni  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_rf_signal   = new Series<double>(this, MaximumBarsLookBack.Infinite);
                s_rf_dir      = new Series<double>(this, MaximumBarsLookBack.Infinite);
            }
        }

        // ==================================================================
        // OnBarUpdate — compute chain, then apply strategy logic
        // ==================================================================
        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;

            // Always compute the chain every bar (even before BarsRequiredToTrade)
            // so series are well-populated by the time trading starts.
            ComputeChain();

            if (CurrentBar < BarsRequiredToTrade) return;

            // ---- Session gating (NY time) ----
            TimeZoneInfo sourceTz = NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo;
            DateTime nyNow        = TimeZoneInfo.ConvertTime(Time[0], sourceTz, nyTz);
            TimeSpan t            = nyNow.TimeOfDay;

            bool inSession   = (t >= SessionStart) && (t <  SessionEnd);
            bool atOrPastEnd = t >= SessionEnd;

            bool prevInside = false;
            if (CurrentBar > 0)
            {
                DateTime nyPrev = TimeZoneInfo.ConvertTime(Time[1], sourceTz, nyTz);
                TimeSpan tp     = nyPrev.TimeOfDay;
                prevInside      = (tp >= SessionStart) && (tp < SessionEnd);
            }

            if (ForceFlatAtSessionEnd && atOrPastEnd && prevInside)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong(Convert.ToInt32(Position.Quantity), SIG_SESSION_END, SIG_LONG);
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort(Convert.ToInt32(Position.Quantity), SIG_SESSION_END, SIG_SHORT);

                lastTradeBar      = CurrentBar;
                entryBar          = -1;
                pendingReentryDir = 0;
                pendingSignalDir  = 0;
                return;
            }

            if (!inSession)
            {
                // Outside the session window — reset the daily-counter edge so the next
                // session-entry triggers a fresh baseline. Also drop any stale pending
                // signal so it doesn't carry into the next session.
                dailyResetDone   = false;
                pendingSignalDir = 0;
                return;
            }

            // ---- Session-start reset for daily counters (group "06 Risk management") ----
            // Edge-fires once per session entry. Snapshots the cumulative profit baseline
            // so today's PnL can be computed by subtraction later.
            if (!dailyResetDone)
            {
                dailyStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                tradesToday    = 0;
                dailyLimitHit  = false;
                dailyResetDone = true;
            }

            // ---- Daily loss limit check ----
            // Computes today's PnL = (cum profit since session start) + (open position unrealized).
            // When breached, force-flat any open position and block entries for the rest of the session.
            if (DailyLossLimit > 0 && !dailyLimitHit)
            {
                double realized   = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - dailyStartCumProfit;
                double unrealized = Position.MarketPosition != MarketPosition.Flat
                    ? Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0])
                    : 0.0;
                double dailyPnL = realized + unrealized;

                if (dailyPnL <= -DailyLossLimit)
                {
                    dailyLimitHit = true;
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong(Convert.ToInt32(Position.Quantity), "DailyLossLimit", SIG_LONG);
                    else if (Position.MarketPosition == MarketPosition.Short)
                        ExitShort(Convert.ToInt32(Position.Quantity), "DailyLossLimit", SIG_SHORT);

                    lastTradeBar      = CurrentBar;
                    entryBar          = -1;
                    pendingReentryDir = 0;
                    pendingSignalDir  = 0;
                    return;
                }
            }

            // If daily loss limit was hit earlier in this session, block all further entries.
            if (dailyLimitHit) return;

            // ---- Read Range Filter final signal ----
            double rfSignal = s_rf_signal[0];
            if (rfSignal == 0 && pendingReentryDir == 0 && pendingSignalDir == 0) return;

            int signalDir = (int)Math.Sign(rfSignal);
            bool aligned  = CheckAlignment(signalDir);

            // ---- SignalConfirmationBars (delayed-entry pattern, option a) ----
            // Sat outside the anti-chop block because it has to handle BOTH a fresh
            // signal (queue + skip) and a pending signal check (no fresh signal this bar).
            // The previous lookback implementation was structurally broken: Range Filter
            // direction by definition flips on a signal bar, so checking s_rf_dir[0..N-1]
            // could never pass at N≥2. This delayed-entry version waits N bars after the
            // signal and only fires if Range Filter direction still matches.
            if (SignalConfirmationBars > 0)
            {
                // Fresh signal this bar — queue as pending, skip immediate entry.
                if (signalDir != 0)
                {
                    pendingSignalDir = signalDir;
                    pendingSignalBar = CurrentBar;
                    return;
                }

                // No fresh signal — check the pending queue.
                if (pendingSignalDir != 0)
                {
                    int rfDir = (int)s_rf_dir[0];

                    // Drop pending if Range Filter direction reversed during the wait.
                    if (rfDir != 0 && rfDir != pendingSignalDir)
                    {
                        pendingSignalDir = 0;
                        return;
                    }

                    int barsWaited = CurrentBar - pendingSignalBar;
                    if (barsWaited >= SignalConfirmationBars && rfDir == pendingSignalDir)
                    {
                        // Promote: synthesize as if it were a fresh signal this bar.
                        signalDir = pendingSignalDir;
                        rfSignal  = pendingSignalDir;
                        aligned   = CheckAlignment(signalDir);
                        pendingSignalDir = 0;
                        // Fall through to the rest of the logic (anti-chop, entry).
                    }
                    else
                    {
                        return;  // still waiting
                    }
                }
            }

            // ---- Anti-chop filters (group "05 Anti-chop filters") ----
            // Only relevant when there is an active signal this bar (signalDir != 0).
            // Each filter early-returns to skip both pending re-entry AND new entry/exit
            // attempts for this bar.
            if (signalDir != 0)
            {
                // Filter 0 (group 06 Risk management): MaxTradesPerDay — hard cap on
                // entries per session. Counts new entries (incl. reversals + pending re-entries),
                // resets at session start.
                if (MaxTradesPerDay > 0 && tradesToday >= MaxTradesPerDay) return;

                // Filter 1: MinBarsBetweenEntries — cooldown after any prior trade.
                if (MinBarsBetweenEntries > 0 && lastTradeBar >= 0
                    && CurrentBar - lastTradeBar < MinBarsBetweenEntries) return;

                // Filter 2: MinHoldBars — block opposite-direction signal until current
                // position has been open at least N bars. Same-direction signals are
                // unaffected (they're already filtered by EntriesPerDirection = 1).
                if (MinHoldBars > 0 && entryBar >= 0
                    && CurrentBar - entryBar < MinHoldBars)
                {
                    bool oppositeOfPosition =
                        (Position.MarketPosition == MarketPosition.Long  && signalDir < 0) ||
                        (Position.MarketPosition == MarketPosition.Short && signalDir > 0);
                    if (oppositeOfPosition) return;
                }
            }

            // ---- Pending re-entry (FlattenFirst mode) ----
            if (pendingReentryDir != 0 && CurrentBar > pendingReentryBar)
            {
                if (Position.MarketPosition == MarketPosition.Flat
                    && signalDir == pendingReentryDir
                    && aligned)
                {
                    if (pendingReentryDir > 0) EnterLong (Quantity, SIG_LONG);
                    else                       EnterShort(Quantity, SIG_SHORT);
                    pendingReentryDir = 0;
                    entryBar          = CurrentBar;
                    lastTradeBar      = CurrentBar;
                    tradesToday++;
                    return;
                }
                if (signalDir != 0 && signalDir != pendingReentryDir)
                    pendingReentryDir = 0;
            }

            if (rfSignal == 0 || !aligned) return;

            // ---- Visible signal markers (mirror Pine plotshape) ----
            if (signalDir > 0)
            {
                Draw.ArrowUp(this, "buy_" + CurrentBar, false, 0, Low[0]  - (TickSize * 4), Brushes.Lime);
                Draw.Text   (this, "buytxt_" + CurrentBar, "Buy",  0, Low[0]  - (TickSize * 8), Brushes.Lime);
            }
            else
            {
                Draw.ArrowDown(this, "sell_" + CurrentBar, false, 0, High[0] + (TickSize * 4), Brushes.Red);
                Draw.Text     (this, "selltxt_" + CurrentBar, "Sell", 0, High[0] + (TickSize * 8), Brushes.Red);
            }

            // ---- Entry / Exit / Reversal ----
            if (signalDir > 0)
            {
                if (Position.MarketPosition == MarketPosition.Short)
                {
                    ExitShort(Convert.ToInt32(Position.Quantity), SIG_EXIT_SHORT, SIG_SHORT);
                    lastTradeBar = CurrentBar;
                    entryBar     = -1;

                    if (ReversalEnabled && !FlattenFirst)
                    {
                        EnterLong(Quantity, SIG_LONG);
                        entryBar = CurrentBar;
                        tradesToday++;
                    }
                    else if (ReversalEnabled && FlattenFirst)
                    {
                        pendingReentryDir = +1;
                        pendingReentryBar = CurrentBar;
                    }
                }
                else if (Position.MarketPosition == MarketPosition.Flat)
                {
                    EnterLong(Quantity, SIG_LONG);
                    entryBar     = CurrentBar;
                    lastTradeBar = CurrentBar;
                    tradesToday++;
                }
            }
            else if (signalDir < 0)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    ExitLong(Convert.ToInt32(Position.Quantity), SIG_EXIT_LONG, SIG_LONG);
                    lastTradeBar = CurrentBar;
                    entryBar     = -1;

                    if (ReversalEnabled && !FlattenFirst)
                    {
                        EnterShort(Quantity, SIG_SHORT);
                        entryBar = CurrentBar;
                        tradesToday++;
                    }
                    else if (ReversalEnabled && FlattenFirst)
                    {
                        pendingReentryDir = -1;
                        pendingReentryBar = CurrentBar;
                    }
                }
                else if (Position.MarketPosition == MarketPosition.Flat)
                {
                    EnterShort(Quantity, SIG_SHORT);
                    entryBar     = CurrentBar;
                    lastTradeBar = CurrentBar;
                    tradesToday++;
                }
            }
        }

        // ==================================================================
        // Chain computation (runs every bar, before session gating)
        // ==================================================================
        private void ComputeChain()
        {
            // ---------------- Stage 0: Center of Gravity (input = Close) ----------------
            // raw_cog = -Σ(src[i] * (i+1)) / Σ(src[i]) over length bars (Pine cog() formula).
            // plot     = sma(raw_cog, SmoothingLength) if COGSmoothingEnabled, else raw_cog.
            // lsma     = linreg(plot, COGLsmaLength, 0)      — Sean's preferred chain source.
            // trigger  = ALMA(plot, window, offset, sigma).
            // direction = +1 if raw > trigger else -1.
            if (CurrentBar >= COGLength - 1)
            {
                double sumNum = 0.0, sumDen = 0.0;
                for (int i = 0; i < COGLength; i++)
                {
                    double v = Close[i];
                    sumNum += v * (i + 1);
                    sumDen += v;
                }
                s_cog_raw[0] = sumDen == 0.0 ? 0.0 : -sumNum / sumDen;
            }

            // Plot = smoothed-or-not (always set so downstream stages always have a valid source).
            if (COGSmoothingEnabled && CurrentBar >= (COGLength - 1) + (COGSmoothingLength - 1))
            {
                double sm = 0.0;
                for (int i = 0; i < COGSmoothingLength; i++) sm += s_cog_raw[i];
                s_cog_plot[0] = sm / COGSmoothingLength;
            }
            else
            {
                s_cog_plot[0] = s_cog_raw[0];
            }

            // COG: LSMA — linreg of plot over COGLsmaLength. Pine: lsma = linreg(COG, length3, 0).
            if (CurrentBar >= COGLsmaLength - 1)
                s_cog_lsma[0] = LinRegOnSeries(s_cog_plot, COGLsmaLength, 0, 0);

            // ALMA trigger on the plot value; direction vote uses raw vs trigger.
            if (CurrentBar >= COGTriggerWindow - 1)
            {
                double m     = COGTriggerOffset * (COGTriggerWindow - 1);
                double sigma = COGTriggerWindow / COGTriggerSigma;
                double sumW  = 0.0, sumWX = 0.0;
                for (int i = 0; i < COGTriggerWindow; i++)
                {
                    double dx = i - m;
                    double w  = Math.Exp(-(dx * dx) / (2.0 * sigma * sigma));
                    sumW  += w;
                    sumWX += w * s_cog_plot[COGTriggerWindow - 1 - i];
                }
                s_cog_trigger[0] = sumW == 0.0 ? 0.0 : sumWX / sumW;

                double cogR = s_cog_raw[0];
                double cogT = s_cog_trigger[0];
                s_cog_dir[0] = cogR > cogT ? 1 : cogR < cogT ? -1 : 0;
            }

            // ---------------- Stage 1: macZLSMA (input = MacZSource) ----------------
            // lsma = linreg(src, L, O); lsma2 = linreg(lsma, L, O); zlsma2 = 2*lsma - lsma2
            // trigger = sma(zlsma2, L2); direction = +1 if zlsma2 > trigger else -1
            ISeries<double> macZSrc = ResolveSource(MacZSource);
            if (CurrentBar >= MacZLength - 1)
                s_mz_lsma[0] = LinReg(macZSrc, MacZLength, MacZOffset, 0);
            if (CurrentBar >= (2 * MacZLength) - 2)
            {
                double lsma2 = LinRegOnSeries(s_mz_lsma, MacZLength, MacZOffset, 0);
                double z2    = 2.0 * s_mz_lsma[0] - lsma2;
                s_mz_zlsma2[0] = z2;
                s_mz_plot[0]   = z2;
            }
            if (CurrentBar >= (2 * MacZLength) - 2 + (MacZTriggerLength - 1))
            {
                double trig = 0.0;
                for (int i = 0; i < MacZTriggerLength; i++) trig += s_mz_zlsma2[i];
                trig /= MacZTriggerLength;
                s_mz_trigger[0] = trig;

                double plot = s_mz_plot[0];
                s_mz_dir[0] = plot > trig ? 1 : plot < trig ? -1 : 0;
            }

            // ---------------- Stage 2: ZLSMA (input = ZLSMASource) ----------------
            ISeries<double> zSrc = ResolveSource(ZLSMASource);
            if (CurrentBar >= ZLSMALength - 1)
                s_z_lsma[0] = LinReg(zSrc, ZLSMALength, ZLSMAOffset, 0);
            if (CurrentBar >= (2 * ZLSMALength) - 2)
            {
                double lsma2 = LinRegOnSeries(s_z_lsma, ZLSMALength, ZLSMAOffset, 0);
                s_z_plot[0]  = 2.0 * s_z_lsma[0] - lsma2;

                if (CurrentBar > 0)
                {
                    double zPrev = s_z_plot[1];
                    double zCur  = s_z_plot[0];
                    s_z_dir[0]   = zCur > zPrev ? 1 : zCur < zPrev ? -1 : 0;
                }
            }

            // ---------------- Stage 3: LSMA Crossover (input = LSMACSource) ----------------
            ISeries<double> lcSrc = ResolveSource(LSMACSource);
            if (CurrentBar >= LSMACLength - 1)
                s_lc_lsma[0] = LinReg(lcSrc, LSMACLength, LSMACOffset, 0);
            if (CurrentBar >= LSMACLength - 1 + (LSMACTriggerLength - 1))
            {
                double trig = 0.0;
                for (int i = 0; i < LSMACTriggerLength; i++) trig += s_lc_lsma[i];
                trig /= LSMACTriggerLength;
                s_lc_trigger[0] = trig;

                double cur = s_lc_lsma[0];
                s_lc_dir[0] = cur > trig ? 1 : cur < trig ? -1 : 0;
            }

            // ---------------- Stage 4: SLSMA (input = SLSMASource) ----------------
            // Reconstructed double-linreg smoother (no zero-lag correction).
            ISeries<double> slSrc = ResolveSource(SLSMASource);
            if (CurrentBar >= SLSMALength - 1)
                s_sl_lsma[0] = LinReg(slSrc, SLSMALength, SLSMAOffset, 0);
            if (CurrentBar >= (2 * SLSMALength) - 2)
            {
                s_sl_plot[0] = LinRegOnSeries(s_sl_lsma, SLSMALength, SLSMAOffset, 0);

                if (CurrentBar > 0)
                {
                    double slPrev = s_sl_plot[1];
                    double slCur  = s_sl_plot[0];
                    s_sl_dir[0]   = slCur > slPrev ? 1 : slCur < slPrev ? -1 : 0;
                }
            }

            // ---------------- Stage 5: Stochastic RVI (input = StochRVISource) ----------------
            // stddev(src, RviLength); upperRaw = change<=0?0:stddev; ema len=14; rvi = up/(up+lo)*100
            // stoch(rvi, ..., StochLength); k = sma(stochRaw, SmoothK); d = sma(k, SmoothD)
            // Pine stdev = population.
            ISeries<double> srSrc = ResolveSource(StochRVISource);
            if (CurrentBar >= RVILength - 1)
            {
                double mean = 0.0;
                for (int i = 0; i < RVILength; i++) mean += srSrc[i];
                mean /= RVILength;
                double ssq = 0.0;
                for (int i = 0; i < RVILength; i++)
                {
                    double diff = srSrc[i] - mean;
                    ssq += diff * diff;
                }
                s_sr_stddev[0] = Math.Sqrt(ssq / RVILength);
            }

            double change = (CurrentBar > 0) ? (srSrc[0] - srSrc[1]) : 0.0;
            s_sr_upperRaw[0] = (change <= 0) ? 0.0             : s_sr_stddev[0];
            s_sr_lowerRaw[0] = (change  > 0) ? 0.0             : s_sr_stddev[0];

            double alphaRvi = 2.0 / (RviEmaLen + 1.0);
            if (CurrentBar == 0)
            {
                s_sr_upperEma[0] = s_sr_upperRaw[0];
                s_sr_lowerEma[0] = s_sr_lowerRaw[0];
            }
            else
            {
                s_sr_upperEma[0] = alphaRvi * s_sr_upperRaw[0] + (1 - alphaRvi) * s_sr_upperEma[1];
                s_sr_lowerEma[0] = alphaRvi * s_sr_lowerRaw[0] + (1 - alphaRvi) * s_sr_lowerEma[1];
            }

            double denomRvi = s_sr_upperEma[0] + s_sr_lowerEma[0];
            s_sr_rvi[0]     = denomRvi == 0.0 ? 0.0 : s_sr_upperEma[0] / denomRvi * 100.0;

            if (CurrentBar >= StochLength - 1)
            {
                double hh = double.MinValue, ll = double.MaxValue;
                for (int i = 0; i < StochLength; i++)
                {
                    double v = s_sr_rvi[i];
                    if (v > hh) hh = v;
                    if (v < ll) ll = v;
                }
                s_sr_stochRaw[0] = (hh - ll) == 0.0 ? 0.0 : 100.0 * (s_sr_rvi[0] - ll) / (hh - ll);
            }

            if (CurrentBar >= StochK - 1)
            {
                double s = 0.0;
                for (int i = 0; i < StochK; i++) s += s_sr_stochRaw[i];
                s_sr_k[0] = s / StochK;
            }
            if (CurrentBar >= StochD - 1)
            {
                double s = 0.0;
                for (int i = 0; i < StochD; i++) s += s_sr_k[i];
                s_sr_d[0] = s / StochD;
            }
            s_sr_dir[0] = s_sr_k[0] > s_sr_d[0] ? 1 : s_sr_k[0] < s_sr_d[0] ? -1 : 0;

            // ---------------- Stage 6: Range Filter (input = RangeFilterSource) ----------------
            ISeries<double> rfSrc = ResolveSource(RangeFilterSource);
            double x = rfSrc[0];
            if (CurrentBar == 0)
            {
                s_rf_absChange[0] = 0;
                s_rf_avrng[0]     = 0;
                s_rf_avrngEma[0]  = 0;
                s_rf_smrng[0]     = 0;
                s_rf_filt[0]      = x;
                s_rf_upward[0]    = 0;
                s_rf_downward[0]  = 0;
                s_rf_condIni[0]   = 0;
                s_rf_signal[0]    = 0;
                s_rf_dir[0]       = 0;
                return;
            }

            double xPrev = rfSrc[1];
            s_rf_absChange[0] = Math.Abs(x - xPrev);

            int wper = SamplingPeriod * 2 - 1;
            double alphaT = 2.0 / (SamplingPeriod + 1.0);
            double alphaW = 2.0 / (wper + 1.0);

            if (CurrentBar == 1)
            {
                s_rf_avrng[0]    = s_rf_absChange[0];
                s_rf_avrngEma[0] = s_rf_avrng[0];
            }
            else
            {
                s_rf_avrng[0]    = alphaT * s_rf_absChange[0] + (1 - alphaT) * s_rf_avrng[1];
                s_rf_avrngEma[0] = alphaW * s_rf_avrng[0]     + (1 - alphaW) * s_rf_avrngEma[1];
            }
            s_rf_smrng[0] = s_rf_avrngEma[0] * RangeMultiplier;

            double r    = s_rf_smrng[0];
            double prev = s_rf_filt[1];
            double next;
            if (x > prev) next = (x - r < prev) ? prev : x - r;
            else          next = (x + r > prev) ? prev : x + r;
            s_rf_filt[0] = next;

            double upPrev = s_rf_upward[1], dnPrev = s_rf_downward[1];
            if (s_rf_filt[0] > s_rf_filt[1])      { s_rf_upward[0] = upPrev + 1; s_rf_downward[0] = 0; }
            else if (s_rf_filt[0] < s_rf_filt[1]) { s_rf_upward[0] = 0;          s_rf_downward[0] = dnPrev + 1; }
            else                                   { s_rf_upward[0] = upPrev;    s_rf_downward[0] = dnPrev; }

            bool longCond  = (x > s_rf_filt[0] && x > xPrev && s_rf_upward[0]   > 0) ||
                             (x > s_rf_filt[0] && x < xPrev && s_rf_upward[0]   > 0);
            bool shortCond = (x < s_rf_filt[0] && x < xPrev && s_rf_downward[0] > 0) ||
                             (x < s_rf_filt[0] && x > xPrev && s_rf_downward[0] > 0);

            double prevIni = s_rf_condIni[1];
            s_rf_condIni[0] = longCond ? 1 : shortCond ? -1 : prevIni;

            bool longSignal  = longCond  && prevIni == -1;
            bool shortSignal = shortCond && prevIni ==  1;

            s_rf_signal[0] = longSignal ? 1 : shortSignal ? -1 : 0;
            s_rf_dir[0]    = s_rf_upward[0] > 0 ? 1 : s_rf_downward[0] > 0 ? -1 : 0;

            // ---------------- Stage 7: Technical Ratings (parallel — not in chain) ----------------
            // 12 MAs + 7 oscillators each vote +1 / 0 / -1. Combined with MA weight per Sean's spec.
            // Direction set when total crosses Long/Short levels.
            // Skip computation if not in use AND not used as filter — saves cycles on every bar.
            if (UseTechRatingsFilter && CurrentBar >= 200)
            {
                int maBuy = 0, maSell = 0;
                double c = Close[0];
                int maTotal;
                if (TechRatingsMASet == TechRatingsMASetMode.LongOnly6)
                {
                    // Long MAs only — skips the short MAs that flip-flop on Renko.
                    if (c > tr_sma50[0])  maBuy++; else if (c < tr_sma50[0])  maSell++;
                    if (c > tr_sma100[0]) maBuy++; else if (c < tr_sma100[0]) maSell++;
                    if (c > tr_sma200[0]) maBuy++; else if (c < tr_sma200[0]) maSell++;
                    if (c > tr_ema50[0])  maBuy++; else if (c < tr_ema50[0])  maSell++;
                    if (c > tr_ema100[0]) maBuy++; else if (c < tr_ema100[0]) maSell++;
                    if (c > tr_ema200[0]) maBuy++; else if (c < tr_ema200[0]) maSell++;
                    maTotal = 6;
                }
                else
                {
                    // Standard 12 — full TV-spec MA subset.
                    if (c > tr_sma10[0])  maBuy++; else if (c < tr_sma10[0])  maSell++;
                    if (c > tr_sma20[0])  maBuy++; else if (c < tr_sma20[0])  maSell++;
                    if (c > tr_sma30[0])  maBuy++; else if (c < tr_sma30[0])  maSell++;
                    if (c > tr_sma50[0])  maBuy++; else if (c < tr_sma50[0])  maSell++;
                    if (c > tr_sma100[0]) maBuy++; else if (c < tr_sma100[0]) maSell++;
                    if (c > tr_sma200[0]) maBuy++; else if (c < tr_sma200[0]) maSell++;
                    if (c > tr_ema10[0])  maBuy++; else if (c < tr_ema10[0])  maSell++;
                    if (c > tr_ema20[0])  maBuy++; else if (c < tr_ema20[0])  maSell++;
                    if (c > tr_ema30[0])  maBuy++; else if (c < tr_ema30[0])  maSell++;
                    if (c > tr_ema50[0])  maBuy++; else if (c < tr_ema50[0])  maSell++;
                    if (c > tr_ema100[0]) maBuy++; else if (c < tr_ema100[0]) maSell++;
                    if (c > tr_ema200[0]) maBuy++; else if (c < tr_ema200[0]) maSell++;
                    maTotal = 12;
                }
                s_tr_maRating[0] = ((double)(maBuy - maSell)) / maTotal;

                int oscBuy = 0, oscSell = 0;
                if      (tr_rsi14[0] < 30)  oscBuy++;
                else if (tr_rsi14[0] > 70)  oscSell++;
                if      (tr_cci20[0] < -100) oscBuy++;
                else if (tr_cci20[0] >  100) oscSell++;
                if      (tr_macd.Diff[0] > 0) oscBuy++;
                else if (tr_macd.Diff[0] < 0) oscSell++;
                if (tr_adx14[0] > 25)
                {
                    if      (Close[0] > Close[1]) oscBuy++;
                    else if (Close[0] < Close[1]) oscSell++;
                }
                if      (tr_stoch.K[0] < 20) oscBuy++;
                else if (tr_stoch.K[0] > 80) oscSell++;
                if      (tr_wr14[0] < -80) oscBuy++;
                else if (tr_wr14[0] > -20) oscSell++;
                if      (tr_mom10[0] > 0) oscBuy++;
                else if (tr_mom10[0] < 0) oscSell++;
                s_tr_oscRating[0] = ((double)(oscBuy - oscSell)) / 7.0;

                double total;
                switch (TechRatingsUses)
                {
                    case TechRatingsUseMode.MAsOnly:         total = s_tr_maRating[0]; break;
                    case TechRatingsUseMode.OscillatorsOnly: total = s_tr_oscRating[0]; break;
                    default: // Both
                        double w = TechRatingsMAWeight / 100.0;
                        total = w * s_tr_maRating[0] + (1.0 - w) * s_tr_oscRating[0];
                        break;
                }
                s_tr_total[0] = total;

                if      (total >= TechRatingsLongLevel)  s_tr_dir[0] = 1;
                else if (total <= TechRatingsShortLevel) s_tr_dir[0] = -1;
                else                                     s_tr_dir[0] = 0;
            }
        }

        // ==================================================================
        // Helpers (Pine parity)
        // ==================================================================

        // Pine linreg on ISeries<double> (supports Close, High, etc.)
        private double LinReg(ISeries<double> src, int length, int offset, int barsAgo)
        {
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            for (int i = 0; i < length; i++)
            {
                double x = i;
                double y = src[barsAgo + (length - 1 - i)];
                sumX  += x; sumY += y; sumXY += x * y; sumX2 += x * x;
            }
            double denom = length * sumX2 - sumX * sumX;
            if (denom == 0.0) return sumY / length;
            double slope     = (length * sumXY - sumX * sumY) / denom;
            double intercept = (sumY - slope * sumX) / length;
            return intercept + slope * (length - 1 - offset);
        }

        // Pine linreg on our own Series<double>
        private double LinRegOnSeries(Series<double> src, int length, int offset, int barsAgo)
        {
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            for (int i = 0; i < length; i++)
            {
                double x = i;
                double y = src[barsAgo + (length - 1 - i)];
                sumX  += x; sumY += y; sumXY += x * y; sumX2 += x * x;
            }
            double denom = length * sumX2 - sumX * sumX;
            if (denom == 0.0) return sumY / length;
            double slope     = (length * sumXY - sumX * sumY) / denom;
            double intercept = (sumY - slope * sumX) / length;
            return intercept + slope * (length - 1 - offset);
        }

        // Maps a TVChainSource enum value to the live series. Used by every chain
        // stage to dispatch on its user-selected source. If a stage picks a source
        // that hasn't been computed yet on this bar (e.g. SLSMA reading from
        // Range Filter — reverse direction in the natural compute order), the read
        // returns the default 0.0 or last bar's value — same edge case TV exhibits.
        private ISeries<double> ResolveSource(TVChainSource src)
        {
            switch (src)
            {
                case TVChainSource.Close:         return Close;
                case TVChainSource.COG_Plot:      return s_cog_plot;
                case TVChainSource.COG_LSMA:      return s_cog_lsma;
                case TVChainSource.COG_Trigger:   return s_cog_trigger;
                case TVChainSource.MacZ_Plot:     return s_mz_plot;
                case TVChainSource.MacZ_Trigger:  return s_mz_trigger;
                case TVChainSource.ZLSMA_Plot:    return s_z_plot;
                case TVChainSource.LSMAC_LSMA:    return s_lc_lsma;
                case TVChainSource.LSMAC_Trigger: return s_lc_trigger;
                case TVChainSource.SLSMA_Plot:    return s_sl_plot;
                case TVChainSource.StochRVI_K:    return s_sr_k;
                case TVChainSource.StochRVI_D:    return s_sr_d;
                default:                          return Close;
            }
        }

        private bool CheckAlignment(int signalDir)
        {
            if (signalDir == 0) return false;
            if (UseCOGFilter         && (int)s_cog_dir[0] != signalDir) return false;
            if (UseMacZLSMAFilter    && (int)s_mz_dir[0]  != signalDir) return false;
            if (UseZLSMAFilter       && (int)s_z_dir[0]   != signalDir) return false;
            if (UseLSMACFilter       && (int)s_lc_dir[0]  != signalDir) return false;
            if (UseSLSMAFilter       && (int)s_sl_dir[0]  != signalDir) return false;
            if (UseStochRVIFilter    && (int)s_sr_dir[0]  != signalDir) return false;
            if (UseTechRatingsFilter && (int)s_tr_dir[0]  != signalDir) return false;
            return true;
        }

        // ==================================================================
        // Parameters
        // ==================================================================
        #region Strategy parameters
        [NinjaScriptProperty][Range(1, int.MaxValue)]
        [Display(Name = "Quantity", Order = 1, GroupName = "01 Order sizing")]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Reversal Enabled", Description = "Opposite signal directly reverses position when true; close-only when false.", Order = 2, GroupName = "02 Reversal mode")]
        public bool ReversalEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flatten First (optional)", Description = "When true AND Reversal Enabled is true: close on signal, wait for confirming opposite signal on next bar to re-enter.", Order = 3, GroupName = "02 Reversal mode")]
        public bool FlattenFirst { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeSpanEditorKey")]
        [Display(Name = "Session Start (NY)", Order = 1, GroupName = "03 Session")]
        public TimeSpan SessionStart { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeSpanEditorKey")]
        [Display(Name = "Session End (NY)",   Order = 2, GroupName = "03 Session")]
        public TimeSpan SessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Force Flat at Session End", Order = 3, GroupName = "03 Session")]
        public bool ForceFlatAtSessionEnd { get; set; }

        [NinjaScriptProperty][Display(Name = "Use Center of Gravity filter", Order = 0, GroupName = "04 Alignment filters", Description = "When on, COG direction must agree with the trade signal before entry. COG must also be in the chain (see group 09).")] public bool UseCOGFilter        { get; set; }
        [NinjaScriptProperty][Display(Name = "Use macZLSMA as filter",    Order = 1, GroupName = "04 Alignment filters")] public bool UseMacZLSMAFilter   { get; set; }
        [NinjaScriptProperty][Display(Name = "Use ZLSMA as filter",       Order = 2, GroupName = "04 Alignment filters")] public bool UseZLSMAFilter      { get; set; }
        [NinjaScriptProperty][Display(Name = "Use LSMA Crossover filter", Order = 3, GroupName = "04 Alignment filters")] public bool UseLSMACFilter      { get; set; }
        [NinjaScriptProperty][Display(Name = "Use SLSMA as filter",       Order = 4, GroupName = "04 Alignment filters")] public bool UseSLSMAFilter      { get; set; }
        [NinjaScriptProperty][Display(Name = "Use Stoch RVI as filter",   Order = 5, GroupName = "04 Alignment filters")] public bool UseStochRVIFilter   { get; set; }
        [NinjaScriptProperty][Display(Name = "Use Technical Ratings as filter", Order = 6, GroupName = "04 Alignment filters", Description = "When on, the Technical Ratings rating direction must agree with the trade signal before entry. Settings live in group 16.")] public bool UseTechRatingsFilter { get; set; }

        [NinjaScriptProperty][Range(1, int.MaxValue)]
        [Display(Name = "Length", Order = 1, GroupName = "09 Center of Gravity")]
        public int COGLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Smoothing (SMA)", Order = 2, GroupName = "09 Center of Gravity", Description = "Off = no smoothing (Pine 'NONE'). On = SMA over Smoothing Length. Sean's spec: NONE default, SMA alternative.")]
        public bool COGSmoothingEnabled { get; set; }

        [NinjaScriptProperty][Range(1, int.MaxValue)]
        [Display(Name = "Smoothing Length", Order = 3, GroupName = "09 Center of Gravity", Description = "Used only when Smoothing (SMA) is on.")]
        public int COGSmoothingLength { get; set; }

        [NinjaScriptProperty][Range(1, int.MaxValue)]
        [Display(Name = "LSMA Length", Order = 4, GroupName = "09 Center of Gravity", Description = "Drives the COG: LSMA output line — Sean's preferred source for downstream chain stages.")]
        public int COGLsmaLength { get; set; }

        [NinjaScriptProperty][Range(1, int.MaxValue)]
        [Display(Name = "Previous High/Low Length", Order = 5, GroupName = "09 Center of Gravity", Description = "Visual-only on the standalone COG indicator. Has no effect on signal logic in the strategy.")]
        public int COGPrevHiLoLength { get; set; }

        [NinjaScriptProperty][Range(1, int.MaxValue)]
        [Display(Name = "Fib Length", Order = 6, GroupName = "09 Center of Gravity", Description = "Visual-only on the standalone COG indicator. Has no effect on signal logic in the strategy.")]
        public int COGFibLength { get; set; }

        [NinjaScriptProperty][Range(1, int.MaxValue)]
        [Display(Name = "Trigger Window", Order = 7, GroupName = "09 Center of Gravity", Description = "ALMA window for the trigger line.")]
        public int COGTriggerWindow { get; set; }

        [NinjaScriptProperty][Range(0.0, 1.0)]
        [Display(Name = "Trigger Offset", Order = 8, GroupName = "09 Center of Gravity", Description = "ALMA offset (0..1). Pine default 0.85.")]
        public double COGTriggerOffset { get; set; }

        [NinjaScriptProperty][Range(0.01, double.MaxValue)]
        [Display(Name = "Trigger Sigma", Order = 9, GroupName = "09 Center of Gravity", Description = "ALMA sigma (controls weight curve sharpness). Pine default 6.")]
        public double COGTriggerSigma { get; set; }

        [NinjaScriptProperty][Display(Name = "Source",         Order = 0, GroupName = "10 macZLSMA",        Description = "Pick the input series for macZLSMA. Default: COG: LSMA.")] public TVChainSource MacZSource { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Length",         Order = 1, GroupName = "10 macZLSMA")] public int MacZLength { get; set; }
        [NinjaScriptProperty][Range(0, int.MaxValue)][Display(Name = "Offset",         Order = 2, GroupName = "10 macZLSMA")] public int MacZOffset { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Trigger Length", Order = 3, GroupName = "10 macZLSMA")] public int MacZTriggerLength { get; set; }

        [NinjaScriptProperty][Display(Name = "Source", Order = 0, GroupName = "11 ZLSMA", Description = "Pick the input series for ZLSMA. Default: macZLSMA: Plot.")] public TVChainSource ZLSMASource { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Length", Order = 1, GroupName = "11 ZLSMA")] public int ZLSMALength { get; set; }
        [NinjaScriptProperty][Range(0, int.MaxValue)][Display(Name = "Offset", Order = 2, GroupName = "11 ZLSMA")] public int ZLSMAOffset { get; set; }

        [NinjaScriptProperty][Display(Name = "Source",          Order = 0, GroupName = "12 LSMA Crossover", Description = "Pick the input series for LSMA Crossover. Default: ZLSMA: Plot.")] public TVChainSource LSMACSource { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Length",          Order = 1, GroupName = "12 LSMA Crossover")] public int LSMACLength { get; set; }
        [NinjaScriptProperty][Range(0, int.MaxValue)][Display(Name = "Offset",          Order = 2, GroupName = "12 LSMA Crossover")] public int LSMACOffset { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Trigger Length",  Order = 3, GroupName = "12 LSMA Crossover")] public int LSMACTriggerLength { get; set; }

        [NinjaScriptProperty][Display(Name = "Source", Order = 0, GroupName = "13 SLSMA (reconstructed)", Description = "Pick the input series for SLSMA. Default: LSMA Crossover: Trigger.")] public TVChainSource SLSMASource { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Length", Order = 1, GroupName = "13 SLSMA (reconstructed)")] public int SLSMALength { get; set; }
        [NinjaScriptProperty][Range(0, int.MaxValue)][Display(Name = "Offset", Order = 2, GroupName = "13 SLSMA (reconstructed)")] public int SLSMAOffset { get; set; }

        [NinjaScriptProperty][Display(Name = "Source",            Order = 0, GroupName = "14 Stochastic RVI", Description = "Pick the input series for Stochastic RVI. Default: SLSMA: Plot.")] public TVChainSource StochRVISource { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "RVI Length",        Order = 1, GroupName = "14 Stochastic RVI")] public int RVILength { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "K Smoothing",       Order = 2, GroupName = "14 Stochastic RVI")] public int StochK { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "D Smoothing",       Order = 3, GroupName = "14 Stochastic RVI")] public int StochD { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Stochastic Length", Order = 4, GroupName = "14 Stochastic RVI")] public int StochLength { get; set; }

        [NinjaScriptProperty][Display(Name = "Source",             Order = 0, GroupName = "15 Range Filter", Description = "Pick the input series for the Range Filter signal generator. Default: Stoch RVI: K.")] public TVChainSource RangeFilterSource { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)]      [Display(Name = "Sampling Period",  Order = 1, GroupName = "15 Range Filter")] public int    SamplingPeriod  { get; set; }
        [NinjaScriptProperty][Range(0.01, double.MaxValue)][Display(Name = "Range Multiplier", Order = 2, GroupName = "15 Range Filter")] public double RangeMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Rating Uses", Order = 1, GroupName = "16 Technical Ratings", Description = "Which votes contribute to the rating: MAs only, Oscillators only, or Both (weighted by MA Weight %). Default: Oscillators Only (live-tested 2026-04-22 PM).")]
        public TechRatingsUseMode TechRatingsUses { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Set", Order = 2, GroupName = "16 Technical Ratings", Description = "Which MAs to vote with. Standard12 = SMA + EMA at 10/20/30/50/100/200 (full TV-spec subset). LongOnly6 = SMA + EMA at 50/100/200 (skips short MAs that flip-flop on Renko).")]
        public TechRatingsMASetMode TechRatingsMASet { get; set; }

        [NinjaScriptProperty][Range(0, 100)]
        [Display(Name = "MA Weight (%)", Order = 3, GroupName = "16 Technical Ratings", Description = "When Rating Uses = Both, weight given to the MA rating (rest goes to oscillator rating). Default 30.")]
        public int TechRatingsMAWeight { get; set; }

        [NinjaScriptProperty][Range(-1.0, 1.0)]
        [Display(Name = "Longs Level", Order = 4, GroupName = "16 Technical Ratings", Description = "Rating must be ≥ this value to vote Long. Default 0.1 (live-tested).")]
        public double TechRatingsLongLevel { get; set; }

        [NinjaScriptProperty][Range(-1.0, 1.0)]
        [Display(Name = "Shorts Level", Order = 5, GroupName = "16 Technical Ratings", Description = "Rating must be ≤ this value to vote Short. Default -0.1 (live-tested).")]
        public double TechRatingsShortLevel { get; set; }

        [NinjaScriptProperty][Range(0, int.MaxValue)]
        [Display(Name = "Stop Loss (ticks)", Description = "Hard stop-loss in NT8 ticks. For MNQ at this chart's Value=24 setting: 1 brick = 24 ticks = $12. 2 bricks = 48 ticks. 0 disables.", Order = 1, GroupName = "06 Risk management")]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty][Range(0, int.MaxValue)]
        [Display(Name = "Profit Target (ticks)", Description = "Hard profit-target in NT8 ticks. Same scale as Stop Loss. 0 disables.", Order = 2, GroupName = "06 Risk management")]
        public int ProfitTargetTicks { get; set; }

        [NinjaScriptProperty][Range(0, int.MaxValue)]
        [Display(Name = "Max Trades Per Day", Description = "Hard cap on entries per session window. Resets at session start. 0 disables.", Order = 3, GroupName = "06 Risk management")]
        public int MaxTradesPerDay { get; set; }

        [NinjaScriptProperty][Range(0.0, double.MaxValue)]
        [Display(Name = "Daily Loss Limit ($)", Description = "Force-flat any open position and block new entries when today's session PnL drops to -$X. Resets at session start. 0 disables.", Order = 4, GroupName = "06 Risk management")]
        public double DailyLossLimit { get; set; }

        [NinjaScriptProperty][Range(0, int.MaxValue)]
        [Display(Name = "Min Bars Between Entries", Description = "Skip new entries for N bars after any prior entry/exit. 0 disables.", Order = 1, GroupName = "05 Anti-chop filters")]
        public int MinBarsBetweenEntries { get; set; }

        [NinjaScriptProperty][Range(0, int.MaxValue)]
        [Display(Name = "Min Hold Bars", Description = "Block opposite-direction (exit/reverse) signals until current position has been open at least N bars. 0 disables.", Order = 2, GroupName = "05 Anti-chop filters")]
        public int MinHoldBars { get; set; }

        [NinjaScriptProperty][Range(0, int.MaxValue)]
        [Display(Name = "Signal Confirmation Bars", Description = "Delayed-entry: when a signal fires, wait N bars and only enter if Range Filter direction still matches. If direction reverses during the wait, the signal is dropped. 0 disables (immediate entry).", Order = 3, GroupName = "05 Anti-chop filters")]
        public int SignalConfirmationBars { get; set; }
        #endregion
    }
}
