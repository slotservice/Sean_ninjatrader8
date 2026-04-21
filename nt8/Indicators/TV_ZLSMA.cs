// =============================================================================
// TV_ZLSMA.cs
//
// Port of Pine Script "ZLSMA — Zero Lag LSMA" (© veryfid, MPL-2.0) — INDICATOR 1/7.
//
// Pine reference:
//     lsma  = linreg(src, length, offset)
//     lsma2 = linreg(lsma, length, offset)
//     eq    = lsma - lsma2
//     zlsma = lsma + eq             // == 2*lsma - lsma2
//
// Default client settings (from XTBUILDER2.8.4):
//     length 2, offset 0, source = macZLSMA plot
//
// Direction convention:
//     +1 when ZLSMA is rising (zlsma[0] > zlsma[1])
//     -1 when ZLSMA is falling
//      0 when flat
// ZLSMA has no internal trigger line, so direction is derived from its own slope,
// which matches how traders read a zero-lag LSMA visually.
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
    public class TV_ZLSMA : Indicator
    {
        private Series<double> lsmaSeries;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "ZLSMA (veryfid) — Zero-Lag LSMA port.";
                Name                     = "TV_ZLSMA";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = true;
                DisplayInDataBox         = true;
                DrawOnPricePanel         = true;
                DrawHorizontalGridLines  = true;
                DrawVerticalGridLines    = true;
                PaintPriceMarkers        = true;
                ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                Length = 2;
                Offset = 0;

                AddPlot(new Stroke(Brushes.Yellow, 3), PlotStyle.Line, "ZLSMA");
                AddPlot(new Stroke(Brushes.Gray,   1), PlotStyle.Dot,  "Direction");
            }
            else if (State == State.DataLoaded)
            {
                lsmaSeries = new Series<double>(this, MaximumBarsLookBack.Infinite);
            }
        }

        protected override void OnBarUpdate()
        {
            // Stage 1: populate lsmaSeries as soon as Length bars of Input are available.
            if (CurrentBar < Length - 1) return;
            double lsma   = LinRegOn(Input, Length, Offset, 0);
            lsmaSeries[0] = lsma;

            // Stage 2: need Length bars of lsmaSeries filled before the 2nd linreg.
            if (CurrentBar < (2 * Length) - 2) return;
            double lsma2 = LinRegOn(lsmaSeries, Length, Offset, 0);
            double zlsma = (2.0 * lsma) - lsma2;
            ZLSMAPlot[0] = zlsma;

            // Direction = slope of ZLSMA itself — needs one prior ZLSMA.
            if (CurrentBar < (2 * Length) - 1)
            {
                DirectionPlot[0] = 0;
                return;
            }
            double prev = ZLSMAPlot[1];
            if (zlsma > prev)      DirectionPlot[0] = 1;
            else if (zlsma < prev) DirectionPlot[0] = -1;
            else                   DirectionPlot[0] = 0;
        }

        private double LinRegOn(ISeries<double> src, int length, int offset, int barsAgo)
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

        #region Public accessors
        [Browsable(false), XmlIgnore]
        public Series<double> ZLSMAPlot     { get { return Values[0]; } }

        [Browsable(false), XmlIgnore]
        public Series<double> DirectionPlot { get { return Values[1]; } }
        #endregion

        #region Parameters
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Length", Description = "linreg length.", Order = 1, GroupName = "Parameters")]
        public int Length { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Offset", Description = "linreg offset.", Order = 2, GroupName = "Parameters")]
        public int Offset { get; set; }
        #endregion
    }
}

// NT8 auto-generates the Indicator / MarketAnalyzerColumn / Strategy
// partial-class factory methods for this indicator during compile. Do not
// author them by hand — doing so collides with NT8's auto-gen.
