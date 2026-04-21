// =============================================================================
// TV_SLSMA.cs
//
// "SLSMA with Pullbacks" — **RECONSTRUCTED** port.
//
// The original Pine source for SLSMA was not delivered by the client (see
// chat.md and ASSUMPTIONS_LOG.md). Per the plan's explicit description:
//     "SLSMA uses linreg smoothing against linreg(lsma)"
// this is implemented as a double-linreg smoother without the zero-lag
// correction term (which is what distinguishes it from ZLSMA).
//
//     lsma  = linreg(src, length, offset)
//     slsma = linreg(lsma, length, offset)       // smoothed LSMA
//
// Compared to ZLSMA:
//     ZLSMA  = lsma + (lsma - lsma2)   // zero-lag correction
//     SLSMA  = lsma2                   // smoothing only, no correction
//
// This produces a slightly lagging, smooth trend line consistent with the
// "SLSMA Pullbacks" screenshots the client shared (SLSMA PULLBACKS.jpg etc.).
// Any residual mismatch vs. the unavailable original Pine indicator is
// documented as APPROXIMATE in ASSUMPTIONS_LOG.md.
//
// Default client settings (from XTBUILDER2.8.4):
//     length 2, offset 0, source = LSMA Crossover Trigger
//
// Direction convention:
//     +1 when SLSMA is rising
//     -1 when SLSMA is falling
//      0 when flat
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
    public class TV_SLSMA : Indicator
    {
        private Series<double> lsmaSeries;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "SLSMA reconstruction — double-linreg smoother (no zero-lag correction).";
                Name                     = "TV_SLSMA";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = true;
                DisplayInDataBox         = true;
                DrawOnPricePanel         = true;
                PaintPriceMarkers        = true;
                ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                Length = 2;
                Offset = 0;

                AddPlot(new Stroke(Brushes.Magenta, 3), PlotStyle.Line, "SLSMA");
                AddPlot(new Stroke(Brushes.Gray,    1), PlotStyle.Dot,  "Direction");
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
            double slsma = LinRegOn(lsmaSeries, Length, Offset, 0);
            SLSMAPlot[0] = slsma;

            // Direction = slope of SLSMA (no Pine trigger line).
            if (CurrentBar < (2 * Length) - 1)
            {
                DirectionPlot[0] = 0;
                return;
            }
            double prev = SLSMAPlot[1];
            if (slsma > prev)      DirectionPlot[0] = 1;
            else if (slsma < prev) DirectionPlot[0] = -1;
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
        [Browsable(false), XmlIgnore] public Series<double> SLSMAPlot     { get { return Values[0]; } }
        [Browsable(false), XmlIgnore] public Series<double> DirectionPlot { get { return Values[1]; } }
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
