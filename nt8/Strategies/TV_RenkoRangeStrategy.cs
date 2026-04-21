// =============================================================================
// TV_RenkoRangeStrategy.cs
//
// Main strategy file for the TradingView → NinjaTrader 8 port.
//
// Wires the seven-indicator chain, performs "checked indicators align" voting,
// enforces NYC session window with forced session-end flat, and implements the
// three reversal modes (close-only, direct reverse, flatten-first) requested
// in BUY SELL ARTICULATION NT8.txt.
//
// Chain (defaults from XTBUILDER2.8.4):
//     Close → TV_MacZLSMA → TV_ZLSMA → TV_LSMACrossover.Trigger
//           → TV_SLSMA    → TV_StochRVI.K → TV_RangeFilter
//     (optional) TV_TechnicalRatingsApprox on Close — off by default.
//
// Execution:
//   - Calculate.OnBarClose (v1 baseline, per client).
//   - ReversalEnabled = true (default), FlattenFirst = false.
//   - Session 09:33–12:00 NY, ForceFlatAtSessionEnd = true.
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
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class TV_RenkoRangeStrategy : Strategy
    {
        // ---- Chain instances (wired in State.DataLoaded) ----
        private TV_MacZLSMA      macZ;
        private TV_ZLSMA         zlsma;
        private TV_LSMACrossover lsmac;
        private TV_SLSMA         slsma;
        private TV_StochRVI      stochRvi;
        private TV_RangeFilter   rangeFilter;
        private TV_TechnicalRatingsApprox techRatings;

        // ---- Session state ----
        private TimeZoneInfo nyTz;

        // ---- Order signal names (for readable audit trails) ----
        private const string SIG_LONG        = "Long";
        private const string SIG_SHORT       = "Short";
        private const string SIG_EXIT_LONG   = "ExitLong";
        private const string SIG_EXIT_SHORT  = "ExitShort";
        private const string SIG_SESSION_END = "SessionEnd";

        // ---- Pending-reentry state for FlattenFirst mode ----
        private int pendingReentryDir;  // +1 want long after flat, -1 want short after flat, 0 none
        private int pendingReentryBar;  // bar on which flatten was issued

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "TradingView → NT8 port: macZLSMA → ZLSMA → LSMA Crossover → SLSMA → Stoch RVI → Range Filter, with optional Technical Ratings. NYC-session automated Renko strategy.";
                Name        = "TV_RenkoRangeStrategy";
                Calculate   = Calculate.OnBarClose;

                EntriesPerDirection              = 1;
                EntryHandling                    = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy     = false; // we handle session close ourselves
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
                // Range Filter's second EMA has effective period `SamplingPeriod*2 - 1` ≈ 479 at defaults.
                // EMAs stabilise at ~4x period; we pick 1000 as a safe-but-not-painful warm-up so
                // early-session bars read fully-converged filter values.
                BarsRequiredToTrade              = 1000;
                IsInstantiatedOnEachOptimizationIteration = true;

                Quantity            = 1;
                ReversalEnabled     = true;
                FlattenFirst        = false;
                UseTechnicalRatings = false;
                ForceFlatAtSessionEnd = true;

                // Session defaults — 09:33 and 12:00 NYC.
                SessionStart = new TimeSpan(9, 33, 0);
                SessionEnd   = new TimeSpan(12, 0, 0);

                // --- Alignment participation (client defaults: all six checked, ratings unchecked). ---
                UseMacZLSMAFilter   = true;
                UseZLSMAFilter      = true;
                UseLSMACFilter      = true;
                UseSLSMAFilter      = true;
                UseStochRVIFilter   = true;
                UseTechRatingsFilter= false;

                // --- Per-indicator defaults (XTBUILDER2.8.4). ---
                MacZLength        = 2;
                MacZOffset        = 0;
                MacZTriggerLength = 3;

                ZLSMALength   = 2;
                ZLSMAOffset   = 0;

                LSMACLength        = 2;
                LSMACOffset        = 0;
                LSMACTriggerLength = 4;
                LSMACLongLength    = 200;
                LSMACExtraLongLen  = 1000;

                SLSMALength = 2;
                SLSMAOffset = 0;

                RVILength   = 6;
                StochK      = 2;
                StochD      = 2;
                StochLength = 14;

                SamplingPeriod  = 240;
                RangeMultiplier = 0.1;

                TechRatingsMAWeight = 30;
            }
            else if (State == State.Configure)
            {
                // Primary data series only for v1 — execution instrument = signal instrument.
                // NT8 will use whatever instrument/series the user selects at strategy load time.
            }
            else if (State == State.DataLoaded)
            {
                // Resolve NY time zone (handles DST correctly).
                try   { nyTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
                catch { nyTz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }

                // Wire the source chain — each indicator is fed by the upstream plot it should read.
                macZ      = TV_MacZLSMA(Close,              MacZLength, MacZOffset, MacZTriggerLength);
                zlsma     = TV_ZLSMA   (macZ.ZLSMA2Plot,    ZLSMALength, ZLSMAOffset);
                lsmac     = TV_LSMACrossover(zlsma.ZLSMAPlot, LSMACLength, LSMACOffset, LSMACTriggerLength, LSMACLongLength, LSMACExtraLongLen);
                slsma     = TV_SLSMA   (lsmac.TriggerPlot,  SLSMALength, SLSMAOffset);
                stochRvi  = TV_StochRVI(slsma.SLSMAPlot,    RVILength, StochK, StochD, StochLength);
                rangeFilter = TV_RangeFilter(stochRvi.KPlot, SamplingPeriod, RangeMultiplier);

                if (UseTechnicalRatings)
                    techRatings = TV_TechnicalRatingsApprox(Close, TechRatingsMAWeight);

                // Show the full chain on the chart so you can eyeball each stage visually
                // against the TradingView reference during side-by-side validation.
                AddChartIndicator(macZ);
                AddChartIndicator(zlsma);
                AddChartIndicator(lsmac);
                AddChartIndicator(slsma);
                AddChartIndicator(stochRvi);
                AddChartIndicator(rangeFilter);
                if (UseTechnicalRatings && techRatings != null)
                    AddChartIndicator(techRatings);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;
            if (CurrentBar < BarsRequiredToTrade) return;

            // --- Determine current NY time and session state ---
            // NT8 stores `Time[i]` in the user's configured display time zone
            // (Core.Globals.GeneralOptions.TimeZoneInfo). We convert that to NY.
            TimeZoneInfo sourceTz = NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo;
            DateTime nyNow        = TimeZoneInfo.ConvertTime(Time[0], sourceTz, nyTz);
            TimeSpan t            = nyNow.TimeOfDay;

            bool inSession  = (t >= SessionStart) && (t <  SessionEnd);
            bool atOrPastEnd= t >= SessionEnd;

            // Previous bar's NY time, for edge detection.
            bool prevInside = false;
            if (CurrentBar > 0)
            {
                DateTime nyPrev = TimeZoneInfo.ConvertTime(Time[1], sourceTz, nyTz);
                TimeSpan tp     = nyPrev.TimeOfDay;
                prevInside      = (tp >= SessionStart) && (tp < SessionEnd);
            }

            // --- Force session-end flat (one-shot on the first bar at/after SessionEnd after having been inside). ---
            if (ForceFlatAtSessionEnd && atOrPastEnd && prevInside)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong(Convert.ToInt32(Position.Quantity), SIG_SESSION_END, SIG_LONG);
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort(Convert.ToInt32(Position.Quantity), SIG_SESSION_END, SIG_SHORT);

                pendingReentryDir = 0;
                return;
            }

            if (!inSession) return;

            // --- Read Range Filter final signal for this bar ---
            // SignalSeries is +1 on the bar a long triggers, -1 on short trigger, 0 otherwise.
            double rfSignal = rangeFilter.SignalSeries[0];
            if (rfSignal == 0 && pendingReentryDir == 0) return;

            int signalDir = (int)Math.Sign(rfSignal);

            // --- "Checked indicators align" vote ---
            bool aligned = CheckAlignment(signalDir);

            // --- Handle pending re-entry (FlattenFirst mode) ---
            if (pendingReentryDir != 0 && CurrentBar > pendingReentryBar)
            {
                // Re-enter in the pending direction only if flat and current signal confirms it.
                if (Position.MarketPosition == MarketPosition.Flat
                    && signalDir == pendingReentryDir
                    && aligned)
                {
                    if (pendingReentryDir > 0) EnterLong (Quantity, SIG_LONG);
                    else                       EnterShort(Quantity, SIG_SHORT);
                    pendingReentryDir = 0;
                    return;
                }
                // If the opposite signal fires before we re-entered, cancel the pending intent
                // (keeps behavior deterministic — no stale re-entries).
                if (signalDir != 0 && signalDir != pendingReentryDir)
                    pendingReentryDir = 0;
            }

            if (rfSignal == 0 || !aligned) return;

            // ---------------------- Visible signal markers (mirror Pine plotshape) ----------------------
            // Only draw when the signal actually fires on this bar (not during pending re-entry waits).
            if (signalDir > 0)
            {
                Draw.ArrowUp(this, "buy_" + CurrentBar, false, 0, Low[0] - (TickSize * 4), Brushes.Lime);
                Draw.Text    (this, "buytxt_" + CurrentBar, "Buy",  0, Low[0] - (TickSize * 8),  Brushes.Lime);
            }
            else if (signalDir < 0)
            {
                Draw.ArrowDown(this, "sell_" + CurrentBar, false, 0, High[0] + (TickSize * 4), Brushes.Red);
                Draw.Text     (this, "selltxt_" + CurrentBar, "Sell", 0, High[0] + (TickSize * 8),  Brushes.Red);
            }

            // ---------------------- Entry / Exit / Reversal logic ----------------------
            if (signalDir > 0) // LONG signal
            {
                if (Position.MarketPosition == MarketPosition.Short)
                {
                    // Opposite signal while short — close.
                    ExitShort(Convert.ToInt32(Position.Quantity), SIG_EXIT_SHORT, SIG_SHORT);

                    if (ReversalEnabled && !FlattenFirst)
                    {
                        // Direct reverse: submit long entry on the same bar.
                        EnterLong(Quantity, SIG_LONG);
                    }
                    else if (ReversalEnabled && FlattenFirst)
                    {
                        // Flatten first, then re-enter on a subsequent confirming bar.
                        pendingReentryDir = +1;
                        pendingReentryBar = CurrentBar;
                    }
                    // else: ReversalEnabled == false → close only, no re-entry.
                }
                else if (Position.MarketPosition == MarketPosition.Flat)
                {
                    EnterLong(Quantity, SIG_LONG);
                }
                // Already long — NT8 will ignore a duplicate EnterLong on same signal; no action.
            }
            else if (signalDir < 0) // SHORT signal
            {
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    ExitLong(Convert.ToInt32(Position.Quantity), SIG_EXIT_LONG, SIG_LONG);

                    if (ReversalEnabled && !FlattenFirst)
                    {
                        EnterShort(Quantity, SIG_SHORT);
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
                }
            }
        }

        // "Checked indicators align" — every enabled filter must match signalDir.
        // Unchecked filters are ignored (neither block nor confirm), matching the
        // client's wording: "Unchecked indicators should not block trading logic".
        private bool CheckAlignment(int signalDir)
        {
            if (signalDir == 0) return false;

            if (UseMacZLSMAFilter   && (int)macZ    .DirectionPlot[0] != signalDir) return false;
            if (UseZLSMAFilter      && (int)zlsma   .DirectionPlot[0] != signalDir) return false;
            if (UseLSMACFilter      && (int)lsmac   .DirectionPlot[0] != signalDir) return false;
            if (UseSLSMAFilter      && (int)slsma   .DirectionPlot[0] != signalDir) return false;
            if (UseStochRVIFilter   && (int)stochRvi.DirectionPlot[0] != signalDir) return false;
            if (UseTechnicalRatings && UseTechRatingsFilter && techRatings != null
                                   && (int)techRatings.DirectionPlot[0] != signalDir) return false;

            return true;
        }

        #region Strategy parameters
        // ---- Sizing & mode ----
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Quantity", Order = 1, GroupName = "01 Order sizing")]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Reversal Enabled", Description = "Opposite signal directly reverses position when true; close-only when false.", Order = 2, GroupName = "02 Reversal mode")]
        public bool ReversalEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flatten First (optional)", Description = "When true AND Reversal Enabled is true: close on signal, wait for confirming opposite signal on next bar to re-enter.", Order = 3, GroupName = "02 Reversal mode")]
        public bool FlattenFirst { get; set; }

        // ---- Session ----
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

        // ---- Filter toggles (the "checked indicators align" vote) ----
        [NinjaScriptProperty]
        [Display(Name = "Use macZLSMA as filter",   Order = 1, GroupName = "04 Alignment filters")]
        public bool UseMacZLSMAFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ZLSMA as filter",      Order = 2, GroupName = "04 Alignment filters")]
        public bool UseZLSMAFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use LSMA Crossover as filter", Order = 3, GroupName = "04 Alignment filters")]
        public bool UseLSMACFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use SLSMA as filter",      Order = 4, GroupName = "04 Alignment filters")]
        public bool UseSLSMAFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Stochastic RVI as filter", Order = 5, GroupName = "04 Alignment filters")]
        public bool UseStochRVIFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Technical Ratings (approx) filter", Description = "Only effective if 'Use Technical Ratings' is also true.", Order = 6, GroupName = "04 Alignment filters")]
        public bool UseTechRatingsFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Technical Ratings (approx)", Description = "Approximation only — library-backed TV indicator cannot be reproduced bit-for-bit.", Order = 7, GroupName = "04 Alignment filters")]
        public bool UseTechnicalRatings { get; set; }

        // ---- macZLSMA ----
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Length",         Order = 1, GroupName = "10 macZLSMA")] public int MacZLength { get; set; }
        [NinjaScriptProperty][Range(0, int.MaxValue)][Display(Name = "Offset",         Order = 2, GroupName = "10 macZLSMA")] public int MacZOffset { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Trigger Length", Order = 3, GroupName = "10 macZLSMA")] public int MacZTriggerLength { get; set; }

        // ---- ZLSMA ----
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Length", Order = 1, GroupName = "11 ZLSMA")] public int ZLSMALength { get; set; }
        [NinjaScriptProperty][Range(0, int.MaxValue)][Display(Name = "Offset", Order = 2, GroupName = "11 ZLSMA")] public int ZLSMAOffset { get; set; }

        // ---- LSMA Crossover ----
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Length",          Order = 1, GroupName = "12 LSMA Crossover")] public int LSMACLength { get; set; }
        [NinjaScriptProperty][Range(0, int.MaxValue)][Display(Name = "Offset",          Order = 2, GroupName = "12 LSMA Crossover")] public int LSMACOffset { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Trigger Length",  Order = 3, GroupName = "12 LSMA Crossover")] public int LSMACTriggerLength { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Long Length",     Order = 4, GroupName = "12 LSMA Crossover")] public int LSMACLongLength { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Extra Long Len",  Order = 5, GroupName = "12 LSMA Crossover")] public int LSMACExtraLongLen { get; set; }

        // ---- SLSMA ----
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Length", Order = 1, GroupName = "13 SLSMA (reconstructed)")] public int SLSMALength { get; set; }
        [NinjaScriptProperty][Range(0, int.MaxValue)][Display(Name = "Offset", Order = 2, GroupName = "13 SLSMA (reconstructed)")] public int SLSMAOffset { get; set; }

        // ---- Stoch RVI ----
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "RVI Length",        Order = 1, GroupName = "14 Stochastic RVI")] public int RVILength { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "K Smoothing",       Order = 2, GroupName = "14 Stochastic RVI")] public int StochK { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "D Smoothing",       Order = 3, GroupName = "14 Stochastic RVI")] public int StochD { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Stochastic Length", Order = 4, GroupName = "14 Stochastic RVI")] public int StochLength { get; set; }

        // ---- Range Filter ----
        [NinjaScriptProperty][Range(1, int.MaxValue)]     [Display(Name = "Sampling Period",  Order = 1, GroupName = "15 Range Filter")] public int    SamplingPeriod  { get; set; }
        [NinjaScriptProperty][Range(0.01, double.MaxValue)][Display(Name = "Range Multiplier", Order = 2, GroupName = "15 Range Filter")] public double RangeMultiplier { get; set; }

        // ---- Technical Ratings (approx) ----
        [NinjaScriptProperty][Range(0, 100)][Display(Name = "MA Weight (%)", Order = 1, GroupName = "16 Technical Ratings (approx)")] public int TechRatingsMAWeight { get; set; }
        #endregion
    }
}
