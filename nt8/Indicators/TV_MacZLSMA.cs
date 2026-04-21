// =============================================================================
// TV_MacZLSMA.cs
//
// Port of Pine Script "macZLSMA" (© veryfid, MPL-2.0) — INDICATOR 3/7.
//
// Pine reference:
//     lsma   = linreg(src, length, offset)
//     lsma2  = linreg(lsma, length, offset)
//     b      = lsma - lsma2
//     zlsma2 = lsma + b            // == 2*lsma - lsma2
//     trig2  = sma(zlsma2, length2)
//     color green when zlsma2 > trig2 else red
//
// Default client settings (from XTBUILDER2.8.4):
//     length 2, offset 0, trigger length 3, source = Close
//
// Parity notes:
// - Pine's linreg result at offset=0 is the endpoint of the least-squares fit
//   over the last `length` bars. Computed manually (not via NT8 LinReg) so
//   the offset argument and the x-convention match Pine exactly.
// - SMA = arithmetic mean over the last `triggerLength` bars.
//
// Install: Documents\NinjaTrader 8\bin\Custom\Indicators\TV_MacZLSMA.cs
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
    public class TV_MacZLSMA : Indicator
    {
        private Series<double> lsmaSeries;
        private Series<double> zlsma2Series;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "macZLSMA (veryfid) port — zlsma-like line plus SMA trigger.";
                Name                     = "TV_MacZLSMA";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = false;
                DisplayInDataBox         = true;
                DrawOnPricePanel         = false;
                DrawHorizontalGridLines  = true;
                DrawVerticalGridLines    = true;
                PaintPriceMarkers        = true;
                ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                Length        = 2;
                Offset        = 0;
                TriggerLength = 3;

                AddPlot(new Stroke(Brushes.Lime,      2), PlotStyle.Line, "ZLSMA2");
                AddPlot(new Stroke(Brushes.OrangeRed, 2), PlotStyle.Line, "Trigger");
                AddPlot(new Stroke(Brushes.Gray,      1), PlotStyle.Dot,  "Direction");
            }
            else if (State == State.DataLoaded)
            {
                lsmaSeries   = new Series<double>(this, MaximumBarsLookBack.Infinite);
                zlsma2Series = new Series<double>(this, MaximumBarsLookBack.Infinite);
            }
        }

        protected override void OnBarUpdate()
        {
            // Warm-up is staged so that each intermediate series is populated
            // starting the bar it first has enough history. This is necessary
            // because a later bar's second-pass linreg(lsma) reads lsmaSeries[1..],
            // which must hold *real* values, not default-0s.

            // 1) Need Length bars of Input to compute lsma.
            if (CurrentBar < Length - 1) return;
            double lsma = LinRegOn(Input, Length, Offset, 0);
            lsmaSeries[0] = lsma;

            // 2) Need Length bars of lsmaSeries filled to compute lsma2.
            if (CurrentBar < (2 * Length) - 2) return;
            double lsma2 = LinRegOn(lsmaSeries, Length, Offset, 0);

            // 3) zlsma2 = 2*lsma - lsma2 (Pine: lsma + (lsma - lsma2)).
            double zlsma2 = (2.0 * lsma) - lsma2;
            ZLSMA2Plot[0] = zlsma2;
            zlsma2Series[0] = zlsma2;

            // 4) Need TriggerLength bars of zlsma2 for the SMA trigger.
            if (CurrentBar < (2 * Length) - 2 + (TriggerLength - 1)) return;
            double trig = 0.0;
            for (int i = 0; i < TriggerLength; i++) trig += zlsma2Series[i];
            trig /= TriggerLength;
            TriggerPlot[0] = trig;

            if (zlsma2 > trig)      DirectionPlot[0] = 1;
            else if (zlsma2 < trig) DirectionPlot[0] = -1;
            else                    DirectionPlot[0] = 0;
        }

        // Pine linreg: with x=0 at oldest bar, x=length-1 at newest, offset shifts
        // the endpoint back by `offset` x-units. Result = intercept + slope*(L-1-offset).
        private double LinRegOn(ISeries<double> src, int length, int offset, int barsAgo)
        {
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            for (int i = 0; i < length; i++)
            {
                double x = i;
                double y = src[barsAgo + (length - 1 - i)];
                sumX  += x;
                sumY  += y;
                sumXY += x * y;
                sumX2 += x * x;
            }
            double denom = length * sumX2 - sumX * sumX;
            if (denom == 0.0) return sumY / length;
            double slope     = (length * sumXY - sumX * sumY) / denom;
            double intercept = (sumY - slope * sumX) / length;
            return intercept + slope * (length - 1 - offset);
        }

        #region Public accessors (for downstream indicators / strategy)
        [Browsable(false), XmlIgnore]
        public Series<double> ZLSMA2Plot    { get { return Values[0]; } }

        [Browsable(false), XmlIgnore]
        public Series<double> TriggerPlot   { get { return Values[1]; } }

        [Browsable(false), XmlIgnore]
        public Series<double> DirectionPlot { get { return Values[2]; } }
        #endregion

        #region Parameters
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Length", Description = "linreg length (Pine: length).", Order = 1, GroupName = "Parameters")]
        public int Length { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Offset", Description = "linreg offset (Pine: offset).", Order = 2, GroupName = "Parameters")]
        public int Offset { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Trigger Length", Description = "SMA length for trigger line.", Order = 3, GroupName = "Parameters")]
        public int TriggerLength { get; set; }
        #endregion
    }
}

// NT8 auto-generates the Indicator / MarketAnalyzerColumn / Strategy
// partial-class factory methods for this indicator during compile. Do not
// author them by hand — doing so collides with NT8's auto-gen.
