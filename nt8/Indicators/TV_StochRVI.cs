// =============================================================================
// TV_StochRVI.cs
//
// Port of Pine Script "Stochastic RVI" — INDICATOR 4/7.
//
// Pine reference:
//     length    = input(10, minval=1)            // RVI length
//     src       = input(close, title="RVI Source")
//     len       = 14                             // fixed EMA length
//     smoothK   = input(3, "K", minval=1)
//     smoothD   = input(3, "D", minval=1)
//     lengthStoch = input(14, "Stochastic Length", minval=1)
//
//     stddev = stdev(src, length)
//     upper  = ema(change(src) <= 0 ? 0 : stddev, len)
//     lower  = ema(change(src) >  0 ? 0 : stddev, len)
//     rvi    = upper / (upper + lower) * 100
//
//     k = sma( stoch(rvi, rvi, rvi, lengthStoch), smoothK )
//     d = sma(k, smoothD)
//
// Default client settings (from XTBUILDER2.8.4):
//     RVI length 6, K 2, D 2, stoch length 14, source = SLSMA
//     (Note: Pine `len` = 14 is NOT the RVI length — it's the fixed EMA
//     window inside the RVI. Kept exactly per Pine source.)
//
// Parity notes:
// - Pine `stdev(src, length)` is **population** stdev (divides by N). Computed
//   manually so this is exact.
// - Pine `ema` seeds with the first available input value and uses
//   alpha = 2 / (len + 1). Computed manually so the conditional zeroing in the
//   upper / lower streams happens *before* smoothing.
// - Pine `stoch(a,b,c,n) = 100 * (a - lowest(c,n)) / (highest(b,n) - lowest(c,n))`.
//   Since upper arg = lower arg = close arg = rvi, this simplifies to
//   100 * (rvi - min) / (max - min).
//
// Direction convention:
//     +1 when K > D
//     -1 when K < D
//      0 when equal
// (Matches the standard Stoch RVI crossover read.)
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
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class TV_StochRVI : Indicator
    {
        // Intermediate series required for correct Pine ordering.
        private Series<double> stddevSeries;
        private Series<double> upperRawSeries;
        private Series<double> lowerRawSeries;
        private Series<double> upperEmaSeries;
        private Series<double> lowerEmaSeries;
        private Series<double> rviSeries;
        private Series<double> stochRawSeries;
        private Series<double> kSeries;

        private const int RviEmaLen = 14; // Pine `len = 14` — fixed internal EMA length.

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Stochastic RVI port — exact Pine parity for stdev, ema, stoch, smoothing.";
                Name                     = "TV_StochRVI";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = false;
                DisplayInDataBox         = true;
                DrawOnPricePanel         = false;
                DrawHorizontalGridLines  = true;
                DrawVerticalGridLines    = true;
                PaintPriceMarkers        = true;
                ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                RviLength     = 6;
                SmoothK       = 2;
                SmoothD       = 2;
                StochLength   = 14;

                AddPlot(new Stroke(Brushes.DeepSkyBlue, 2), PlotStyle.Line, "K");
                AddPlot(new Stroke(Brushes.Orange,      2), PlotStyle.Line, "D");
                AddPlot(new Stroke(Brushes.Gray,        1), PlotStyle.Dot,  "Direction");

                AddLine(Brushes.LightGray, 80, "Overbought");
                AddLine(Brushes.LightGray, 20, "Oversold");
                AddLine(Brushes.LightGray, 50, "Midline");
            }
            else if (State == State.DataLoaded)
            {
                stddevSeries    = new Series<double>(this, MaximumBarsLookBack.Infinite);
                upperRawSeries  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                lowerRawSeries  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                upperEmaSeries  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                lowerEmaSeries  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                rviSeries       = new Series<double>(this, MaximumBarsLookBack.Infinite);
                stochRawSeries  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                kSeries         = new Series<double>(this, MaximumBarsLookBack.Infinite);
            }
        }

        protected override void OnBarUpdate()
        {
            // --- 1. Pine stdev(src, RviLength) ------------------------------------
            if (CurrentBar < RviLength - 1)
            {
                stddevSeries[0] = 0;
            }
            else
            {
                double mean = 0;
                for (int i = 0; i < RviLength; i++) mean += Input[i];
                mean /= RviLength;
                double ssq = 0;
                for (int i = 0; i < RviLength; i++)
                {
                    double diff = Input[i] - mean;
                    ssq += diff * diff;
                }
                stddevSeries[0] = Math.Sqrt(ssq / RviLength); // population stdev
            }

            // --- 2. change(src) <=0 / >0 gating -----------------------------------
            double change = (CurrentBar > 0) ? Input[0] - Input[1] : 0.0;
            upperRawSeries[0] = (change <= 0) ? 0.0             : stddevSeries[0];
            lowerRawSeries[0] = (change  > 0) ? 0.0             : stddevSeries[0];

            // --- 3. ema(upperRaw, 14) and ema(lowerRaw, 14) -----------------------
            // Pine seeds EMA with the first input; alpha = 2 / (len+1).
            double alpha = 2.0 / (RviEmaLen + 1.0);
            if (CurrentBar == 0)
            {
                upperEmaSeries[0] = upperRawSeries[0];
                lowerEmaSeries[0] = lowerRawSeries[0];
            }
            else
            {
                upperEmaSeries[0] = alpha * upperRawSeries[0] + (1 - alpha) * upperEmaSeries[1];
                lowerEmaSeries[0] = alpha * lowerRawSeries[0] + (1 - alpha) * lowerEmaSeries[1];
            }

            // --- 4. rvi = upper / (upper + lower) * 100 ---------------------------
            double denomRvi = upperEmaSeries[0] + lowerEmaSeries[0];
            rviSeries[0]    = denomRvi == 0.0 ? 0.0 : upperEmaSeries[0] / denomRvi * 100.0;

            // --- 5. stoch(rvi, rvi, rvi, StochLength) -----------------------------
            if (CurrentBar < StochLength - 1)
            {
                stochRawSeries[0] = 0;
            }
            else
            {
                double hh = double.MinValue, ll = double.MaxValue;
                for (int i = 0; i < StochLength; i++)
                {
                    double v = rviSeries[i];
                    if (v > hh) hh = v;
                    if (v < ll) ll = v;
                }
                stochRawSeries[0] = (hh - ll) == 0.0
                    ? 0.0
                    : 100.0 * (rviSeries[0] - ll) / (hh - ll);
            }

            // --- 6. K = sma(stochRaw, SmoothK) ------------------------------------
            if (CurrentBar < SmoothK - 1)
            {
                kSeries[0] = 0;
                KPlot[0]   = 0;
            }
            else
            {
                double s = 0;
                for (int i = 0; i < SmoothK; i++) s += stochRawSeries[i];
                double k = s / SmoothK;
                kSeries[0] = k;
                KPlot[0]   = k;
            }

            // --- 7. D = sma(K, SmoothD) -------------------------------------------
            if (CurrentBar < SmoothD - 1)
            {
                DPlot[0] = 0;
            }
            else
            {
                double s = 0;
                for (int i = 0; i < SmoothD; i++) s += kSeries[i];
                DPlot[0] = s / SmoothD;
            }

            // --- Direction (K vs D) ----------------------------------------------
            double kv = KPlot[0], dv = DPlot[0];
            if (kv > dv)      DirectionPlot[0] = 1;
            else if (kv < dv) DirectionPlot[0] = -1;
            else              DirectionPlot[0] = 0;
        }

        #region Public accessors
        [Browsable(false), XmlIgnore] public Series<double> KPlot         { get { return Values[0]; } }
        [Browsable(false), XmlIgnore] public Series<double> DPlot         { get { return Values[1]; } }
        [Browsable(false), XmlIgnore] public Series<double> DirectionPlot { get { return Values[2]; } }
        #endregion

        #region Parameters
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "RVI Length", Description = "stdev window (Pine: length).", Order = 1, GroupName = "Parameters")]
        public int RviLength { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "K Smoothing", Description = "SMA applied to raw stoch value (Pine: smoothK).", Order = 2, GroupName = "Parameters")]
        public int SmoothK { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "D Smoothing", Description = "SMA applied to K (Pine: smoothD).", Order = 3, GroupName = "Parameters")]
        public int SmoothD { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stochastic Length", Description = "Stoch window on RVI (Pine: lengthStoch).", Order = 4, GroupName = "Parameters")]
        public int StochLength { get; set; }
        #endregion
    }
}

// NT8 auto-generates the Indicator / MarketAnalyzerColumn / Strategy
// partial-class factory methods for this indicator during compile. Do not
// author them by hand — doing so collides with NT8's auto-gen.
