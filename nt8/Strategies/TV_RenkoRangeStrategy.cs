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
// Chain (defaults from XTBUILDER2.8.4):
//     Close → macZLSMA → ZLSMA → LSMA Crossover.Trigger
//           → SLSMA    → Stoch RVI.K → Range Filter
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
    public class TV_RenkoRangeStrategy : Strategy
    {
        // ------------------------------------------------------------------
        // Intermediate series — allocated in State.DataLoaded.
        // ------------------------------------------------------------------
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

        // ==================================================================
        // State lifecycle
        // ==================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "TradingView → NT8 port (monolithic): macZLSMA → ZLSMA → LSMA Crossover → SLSMA → Stoch RVI → Range Filter. NYC-session automated Renko strategy.";
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
                UseTechnicalRatings   = false;   // approx removed from monolithic build
                ForceFlatAtSessionEnd = true;

                SessionStart = new TimeSpan(9, 33, 0);
                SessionEnd   = new TimeSpan(12, 0, 0);

                UseMacZLSMAFilter    = true;
                UseZLSMAFilter       = true;
                UseLSMACFilter       = true;
                UseSLSMAFilter       = true;
                UseStochRVIFilter    = true;
                UseTechRatingsFilter = false;

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
            }
            else if (State == State.DataLoaded)
            {
                try   { nyTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
                catch { nyTz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }

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

                pendingReentryDir = 0;
                return;
            }

            if (!inSession) return;

            // ---- Read Range Filter final signal ----
            double rfSignal = s_rf_signal[0];
            if (rfSignal == 0 && pendingReentryDir == 0) return;

            int signalDir = (int)Math.Sign(rfSignal);
            bool aligned  = CheckAlignment(signalDir);

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

                    if (ReversalEnabled && !FlattenFirst)
                        EnterLong(Quantity, SIG_LONG);
                    else if (ReversalEnabled && FlattenFirst)
                    {
                        pendingReentryDir = +1;
                        pendingReentryBar = CurrentBar;
                    }
                }
                else if (Position.MarketPosition == MarketPosition.Flat)
                {
                    EnterLong(Quantity, SIG_LONG);
                }
            }
            else if (signalDir < 0)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    ExitLong(Convert.ToInt32(Position.Quantity), SIG_EXIT_LONG, SIG_LONG);

                    if (ReversalEnabled && !FlattenFirst)
                        EnterShort(Quantity, SIG_SHORT);
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

        // ==================================================================
        // Chain computation (runs every bar, before session gating)
        // ==================================================================
        private void ComputeChain()
        {
            // ---------------- Stage 1: macZLSMA ----------------
            // lsma = linreg(Close, L, O); lsma2 = linreg(lsma, L, O); zlsma2 = 2*lsma - lsma2
            // trigger = sma(zlsma2, L2); direction = +1 if zlsma2 > trigger else -1
            if (CurrentBar >= MacZLength - 1)
                s_mz_lsma[0] = LinReg(Close, MacZLength, MacZOffset, 0);
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

            // ---------------- Stage 2: ZLSMA (input = macZLSMA plot) ----------------
            if (CurrentBar >= (2 * MacZLength) - 2 + (ZLSMALength - 1))
            {
                s_z_lsma[0] = LinRegOnSeries(s_mz_plot, ZLSMALength, ZLSMAOffset, 0);
            }
            if (CurrentBar >= (2 * MacZLength) - 2 + (2 * ZLSMALength) - 2)
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

            // ---------------- Stage 3: LSMA Crossover (input = ZLSMA plot) ----------------
            if (CurrentBar >= (2 * MacZLength) - 2 + (2 * ZLSMALength) - 2 + (LSMACLength - 1))
            {
                s_lc_lsma[0] = LinRegOnSeries(s_z_plot, LSMACLength, LSMACOffset, 0);
            }
            if (CurrentBar >= (2 * MacZLength) - 2 + (2 * ZLSMALength) - 2 + (LSMACLength - 1) + (LSMACTriggerLength - 1))
            {
                double trig = 0.0;
                for (int i = 0; i < LSMACTriggerLength; i++) trig += s_lc_lsma[i];
                trig /= LSMACTriggerLength;
                s_lc_trigger[0] = trig;

                double cur = s_lc_lsma[0];
                s_lc_dir[0] = cur > trig ? 1 : cur < trig ? -1 : 0;
            }

            // ---------------- Stage 4: SLSMA (input = LSMA Crossover Trigger) ----------------
            // SLSMA is the reconstructed double-linreg smoother (no zero-lag correction).
            int slsmaStart = (2 * MacZLength) - 2 + (2 * ZLSMALength) - 2 + (LSMACLength - 1) + (LSMACTriggerLength - 1);
            if (CurrentBar >= slsmaStart + (SLSMALength - 1))
            {
                s_sl_lsma[0] = LinRegOnSeries(s_lc_trigger, SLSMALength, SLSMAOffset, 0);
            }
            if (CurrentBar >= slsmaStart + (2 * SLSMALength) - 2)
            {
                s_sl_plot[0] = LinRegOnSeries(s_sl_lsma, SLSMALength, SLSMAOffset, 0);

                if (CurrentBar > 0)
                {
                    double slPrev = s_sl_plot[1];
                    double slCur  = s_sl_plot[0];
                    s_sl_dir[0]   = slCur > slPrev ? 1 : slCur < slPrev ? -1 : 0;
                }
            }

            // ---------------- Stage 5: Stochastic RVI (input = SLSMA plot) ----------------
            // stddev(src, RviLength); upperRaw = change<=0?0:stddev; ema len=14; rvi = up/(up+lo)*100
            // stoch(rvi, ..., StochLength); k = sma(stochRaw, SmoothK); d = sma(k, SmoothD)
            // Pine stdev = population.
            if (CurrentBar >= RVILength - 1)
            {
                double mean = 0.0;
                for (int i = 0; i < RVILength; i++) mean += s_sl_plot[i];
                mean /= RVILength;
                double ssq = 0.0;
                for (int i = 0; i < RVILength; i++)
                {
                    double diff = s_sl_plot[i] - mean;
                    ssq += diff * diff;
                }
                s_sr_stddev[0] = Math.Sqrt(ssq / RVILength);
            }

            double change = (CurrentBar > 0) ? (s_sl_plot[0] - s_sl_plot[1]) : 0.0;
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

            // ---------------- Stage 6: Range Filter (input = Stoch RVI K) ----------------
            double x = s_sr_k[0];
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

            double xPrev = s_sr_k[1];
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

        private bool CheckAlignment(int signalDir)
        {
            if (signalDir == 0) return false;
            if (UseMacZLSMAFilter && (int)s_mz_dir[0] != signalDir) return false;
            if (UseZLSMAFilter    && (int)s_z_dir[0]  != signalDir) return false;
            if (UseLSMACFilter    && (int)s_lc_dir[0] != signalDir) return false;
            if (UseSLSMAFilter    && (int)s_sl_dir[0] != signalDir) return false;
            if (UseStochRVIFilter && (int)s_sr_dir[0] != signalDir) return false;
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

        [NinjaScriptProperty][Display(Name = "Use macZLSMA as filter",    Order = 1, GroupName = "04 Alignment filters")] public bool UseMacZLSMAFilter   { get; set; }
        [NinjaScriptProperty][Display(Name = "Use ZLSMA as filter",       Order = 2, GroupName = "04 Alignment filters")] public bool UseZLSMAFilter      { get; set; }
        [NinjaScriptProperty][Display(Name = "Use LSMA Crossover filter", Order = 3, GroupName = "04 Alignment filters")] public bool UseLSMACFilter      { get; set; }
        [NinjaScriptProperty][Display(Name = "Use SLSMA as filter",       Order = 4, GroupName = "04 Alignment filters")] public bool UseSLSMAFilter      { get; set; }
        [NinjaScriptProperty][Display(Name = "Use Stoch RVI as filter",   Order = 5, GroupName = "04 Alignment filters")] public bool UseStochRVIFilter   { get; set; }
        [NinjaScriptProperty][Display(Name = "Use Tech Ratings filter",   Order = 6, GroupName = "04 Alignment filters", Description = "Reserved — Technical Ratings approximation not included in monolithic build.")] public bool UseTechRatingsFilter { get; set; }
        [NinjaScriptProperty][Display(Name = "Use Technical Ratings",     Order = 7, GroupName = "04 Alignment filters", Description = "Reserved — not wired in monolithic build.")] public bool UseTechnicalRatings { get; set; }

        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Length",         Order = 1, GroupName = "10 macZLSMA")] public int MacZLength { get; set; }
        [NinjaScriptProperty][Range(0, int.MaxValue)][Display(Name = "Offset",         Order = 2, GroupName = "10 macZLSMA")] public int MacZOffset { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Trigger Length", Order = 3, GroupName = "10 macZLSMA")] public int MacZTriggerLength { get; set; }

        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Length", Order = 1, GroupName = "11 ZLSMA")] public int ZLSMALength { get; set; }
        [NinjaScriptProperty][Range(0, int.MaxValue)][Display(Name = "Offset", Order = 2, GroupName = "11 ZLSMA")] public int ZLSMAOffset { get; set; }

        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Length",          Order = 1, GroupName = "12 LSMA Crossover")] public int LSMACLength { get; set; }
        [NinjaScriptProperty][Range(0, int.MaxValue)][Display(Name = "Offset",          Order = 2, GroupName = "12 LSMA Crossover")] public int LSMACOffset { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Trigger Length",  Order = 3, GroupName = "12 LSMA Crossover")] public int LSMACTriggerLength { get; set; }

        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Length", Order = 1, GroupName = "13 SLSMA (reconstructed)")] public int SLSMALength { get; set; }
        [NinjaScriptProperty][Range(0, int.MaxValue)][Display(Name = "Offset", Order = 2, GroupName = "13 SLSMA (reconstructed)")] public int SLSMAOffset { get; set; }

        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "RVI Length",        Order = 1, GroupName = "14 Stochastic RVI")] public int RVILength { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "K Smoothing",       Order = 2, GroupName = "14 Stochastic RVI")] public int StochK { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "D Smoothing",       Order = 3, GroupName = "14 Stochastic RVI")] public int StochD { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "Stochastic Length", Order = 4, GroupName = "14 Stochastic RVI")] public int StochLength { get; set; }

        [NinjaScriptProperty][Range(1, int.MaxValue)]      [Display(Name = "Sampling Period",  Order = 1, GroupName = "15 Range Filter")] public int    SamplingPeriod  { get; set; }
        [NinjaScriptProperty][Range(0.01, double.MaxValue)][Display(Name = "Range Multiplier", Order = 2, GroupName = "15 Range Filter")] public double RangeMultiplier { get; set; }
        #endregion
    }
}
