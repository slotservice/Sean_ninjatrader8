// =============================================================================
// TV_RangeFilter.cs
//
// Port of Pine v5 "Range Filter Buy and Sell 5min" — INDICATOR 5/7.
// Original @ DonovanWall, adapted @ guikroth, v5 by @tvenn.
//
// Pine reference (the pieces that matter for signals):
//     src   = input(close)
//     per   = SamplingPeriod   (client default 240)
//     mult  = Range Multiplier (client default 0.1)
//
//     smoothrng(x, t, m):
//         wper   = t * 2 - 1
//         avrng  = ta.ema(abs(x - x[1]), t)
//         return ta.ema(avrng, wper) * m
//
//     rngfilt(x, r):
//         rngfilt := x > nz(rngfilt[1])
//             ? (x - r < nz(rngfilt[1]) ? nz(rngfilt[1]) : x - r)
//             : (x + r > nz(rngfilt[1]) ? nz(rngfilt[1]) : x + r)
//
//     smrng = smoothrng(src, per, mult)
//     filt  = rngfilt(src, smrng)
//
//     upward   := filt > filt[1] ? nz(upward[1]) + 1   : filt < filt[1] ? 0 : nz(upward[1])
//     downward := filt < filt[1] ? nz(downward[1]) + 1 : filt > filt[1] ? 0 : nz(downward[1])
//
//     hband = filt + smrng
//     lband = filt - smrng
//
//     longCond  := (src > filt and src > src[1] and upward > 0)   or (src > filt and src < src[1] and upward > 0)
//     shortCond := (src < filt and src < src[1] and downward > 0) or (src < filt and src > src[1] and downward > 0)
//
//     CondIni       := longCond ? 1 : shortCond ? -1 : CondIni[1]
//     longCondition  = longCond  and CondIni[1] == -1
//     shortCondition = shortCond and CondIni[1] ==  1
//
// Client default settings (XTBUILDER2.8.4):
//     SamplingPeriod = 240, RangeMultiplier = 0.1, source = Stoch RVI K
//
// Parity notes:
// - EMA is implemented manually with Pine seeding (first value = first input,
//   alpha = 2/(len+1)) so the two nested EMAs in smoothrng match Pine byte-for-byte.
// - `nz(x[1])` — Pine treats `na` as 0 for `nz`. On NT8 we seed recursive series
//   with 0 on CurrentBar == 0 which yields identical numeric behavior.
// - Output series exposed for strategy consumption:
//     FiltPlot, HBandPlot, LBandPlot  — for chart overlay
//     LongCondSeries, ShortCondSeries — one-shot transition flags (0/1)
//     UpwardSeries, DownwardSeries    — direction counters
//     SignalSeries                    — +1 long, -1 short, 0 otherwise (on signal bar only)
//     DirectionSeries                 — +1/-1/0 continuous direction read
//       (sign of upward − downward — "checked indicators align" contributor)
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

namespace NinjaTrader.NinjaScript.Indicators.TVPort
{
    public class TV_RangeFilter : Indicator
    {
        // Internal series
        private Series<double> absChange;
        private Series<double> avrng;       // ema(abs(x - x[1]), t)           — first EMA
        private Series<double> avrngEma;    // ema(avrng, t*2-1)               — second EMA (pre-multiplier)
        private Series<double> smrng;       // avrngEma * RangeMultiplier
        private Series<double> filt;
        private Series<double> upward;
        private Series<double> downward;
        private Series<double> condIni;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Range Filter (DonovanWall / guikroth / tvenn) — Pine v5 port.";
                Name                     = "TV_RangeFilter";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = false; // displayed alongside the source series
                DisplayInDataBox         = true;
                DrawOnPricePanel         = false;
                DrawHorizontalGridLines  = true;
                DrawVerticalGridLines    = true;
                PaintPriceMarkers        = true;
                ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                SamplingPeriod  = 240;
                RangeMultiplier = 0.1;

                AddPlot(new Stroke(Brushes.White,        2), PlotStyle.Line, "Filt");
                AddPlot(new Stroke(Brushes.Lime,         1), PlotStyle.Line, "HBand");
                AddPlot(new Stroke(Brushes.Red,          1), PlotStyle.Line, "LBand");
                AddPlot(new Stroke(Brushes.Gray,         1), PlotStyle.Dot,  "LongCond");
                AddPlot(new Stroke(Brushes.Gray,         1), PlotStyle.Dot,  "ShortCond");
                AddPlot(new Stroke(Brushes.Gray,         1), PlotStyle.Dot,  "Signal");
                AddPlot(new Stroke(Brushes.Gray,         1), PlotStyle.Dot,  "Direction");
            }
            else if (State == State.DataLoaded)
            {
                absChange = new Series<double>(this, MaximumBarsLookBack.Infinite);
                avrng     = new Series<double>(this, MaximumBarsLookBack.Infinite);
                avrngEma  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                smrng     = new Series<double>(this, MaximumBarsLookBack.Infinite);
                filt      = new Series<double>(this, MaximumBarsLookBack.Infinite);
                upward    = new Series<double>(this, MaximumBarsLookBack.Infinite);
                downward  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                condIni   = new Series<double>(this, MaximumBarsLookBack.Infinite);
            }
        }

        protected override void OnBarUpdate()
        {
            double x = Input[0];

            // --- 1. absChange[0] = abs(x - x[1]), 0 on first bar ---
            if (CurrentBar == 0)
            {
                absChange[0] = 0;
                avrng[0]     = 0;
                avrngEma[0]  = 0;
                smrng[0]     = 0;
                filt[0]      = x;
                upward[0]    = 0;
                downward[0]  = 0;
                condIni[0]   = 0;

                FiltPlot[0]         = x;
                HBandPlot[0]        = x;
                LBandPlot[0]        = x;
                LongCondSeries[0]   = 0;
                ShortCondSeries[0]  = 0;
                SignalSeries[0]     = 0;
                DirectionSeries[0]  = 0;
                return;
            }

            double xPrev = Input[1];
            absChange[0] = Math.Abs(x - xPrev);

            // --- 2. avrng = ta.ema(absChange, SamplingPeriod), Pine seeding ---
            int t = SamplingPeriod;
            double alphaT = 2.0 / (t + 1.0);
            if (CurrentBar == 1)
                avrng[0] = absChange[0];
            else
                avrng[0] = alphaT * absChange[0] + (1 - alphaT) * avrng[1];

            // --- 3. avrngEma = ta.ema(avrng, t*2-1); smrng = avrngEma * mult ---
            int wper = t * 2 - 1;
            double alphaW = 2.0 / (wper + 1.0);
            if (CurrentBar == 1)
                avrngEma[0] = avrng[0];
            else
                avrngEma[0] = alphaW * avrng[0] + (1 - alphaW) * avrngEma[1];
            smrng[0] = avrngEma[0] * RangeMultiplier;

            // --- 4. filt = rngfilt(x, smrng) recursive ---
            double r    = smrng[0];
            double prev = filt[1]; // safe because CurrentBar >= 1 here
            double next;
            if (x > prev)
                next = (x - r < prev) ? prev : x - r;
            else
                next = (x + r > prev) ? prev : x + r;
            filt[0] = next;

            // --- 5. upward / downward counters ---
            double upPrev = upward[1], dnPrev = downward[1];
            if (filt[0] > filt[1])
            {
                upward[0]   = upPrev + 1;
                downward[0] = 0;
            }
            else if (filt[0] < filt[1])
            {
                upward[0]   = 0;
                downward[0] = dnPrev + 1;
            }
            else
            {
                upward[0]   = upPrev;
                downward[0] = dnPrev;
            }

            // --- 6. bands + plots ---
            FiltPlot[0]  = filt[0];
            HBandPlot[0] = filt[0] + smrng[0];
            LBandPlot[0] = filt[0] - smrng[0];

            // --- 7. longCond / shortCond ---
            bool longCond  = (x > filt[0] && x > xPrev && upward[0]   > 0) ||
                             (x > filt[0] && x < xPrev && upward[0]   > 0);
            bool shortCond = (x < filt[0] && x < xPrev && downward[0] > 0) ||
                             (x < filt[0] && x > xPrev && downward[0] > 0);

            LongCondSeries[0]  = longCond  ? 1 : 0;
            ShortCondSeries[0] = shortCond ? 1 : 0;

            // --- 8. CondIni transition state ---
            double prevIni = condIni[1];
            condIni[0] = longCond ? 1 : shortCond ? -1 : prevIni;

            bool longSignal  = longCond  && prevIni == -1;
            bool shortSignal = shortCond && prevIni ==  1;

            SignalSeries[0] = longSignal ? 1 : shortSignal ? -1 : 0;

            // Continuous direction read — used by strategy for "alignment" vote.
            if (upward[0] > 0)         DirectionSeries[0] = 1;
            else if (downward[0] > 0)  DirectionSeries[0] = -1;
            else                       DirectionSeries[0] = 0;
        }

        #region Public accessors
        [Browsable(false), XmlIgnore] public Series<double> FiltPlot        { get { return Values[0]; } }
        [Browsable(false), XmlIgnore] public Series<double> HBandPlot       { get { return Values[1]; } }
        [Browsable(false), XmlIgnore] public Series<double> LBandPlot       { get { return Values[2]; } }
        [Browsable(false), XmlIgnore] public Series<double> LongCondSeries  { get { return Values[3]; } }
        [Browsable(false), XmlIgnore] public Series<double> ShortCondSeries { get { return Values[4]; } }
        [Browsable(false), XmlIgnore] public Series<double> SignalSeries    { get { return Values[5]; } }
        [Browsable(false), XmlIgnore] public Series<double> DirectionSeries { get { return Values[6]; } }

        [Browsable(false), XmlIgnore] public Series<double> UpwardSeries    { get { return upward;   } }
        [Browsable(false), XmlIgnore] public Series<double> DownwardSeries  { get { return downward; } }
        #endregion

        #region Parameters
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Sampling Period", Description = "Pine `per` — EMA window size.", Order = 1, GroupName = "Parameters")]
        public int SamplingPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Range Multiplier", Description = "Pine `mult` — multiplier on smoothed range.", Order = 2, GroupName = "Parameters")]
        public double RangeMultiplier { get; set; }
        #endregion
    }
}

// NT8 auto-generates the Indicator / MarketAnalyzerColumn / Strategy
// partial-class factory methods for this indicator during compile. Do not
// author them by hand — doing so collides with NT8's auto-gen.
