// =============================================================================
// TV_LSMACrossover.cs
//
// Port of Pine Script "Least Squares Moving Average Crossover" — INDICATOR 2/7.
//
// Pine reference:
//     lsma       = linreg(src, length, offset)
//     d          = sma(lsma, length2)            // trigger line
//     lsmalong   = linreg(src, 200, 0)
//     lsmaxlong  = linreg(src, 1000, 0)
//
// Default client settings (from XTBUILDER2.8.4):
//     length 2, offset 0, trigger length 4, source = ZLSMA
//
// Direction convention (matches crossover reading):
//     +1 when LSMA > Trigger
//     -1 when LSMA < Trigger
//      0 when equal
//
// Long / Extra Long plots are informational only; they need 200 / 1000 bars
// of history and are not used by the strategy's alignment logic.
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
    public class TV_LSMACrossover : Indicator
    {
        private Series<double> lsmaSeries;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "LSMA Crossover port — linreg(src, L) and SMA trigger plus long/extra-long refs.";
                Name                     = "TV_LSMACrossover";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = true;
                DisplayInDataBox         = true;
                DrawOnPricePanel         = true;
                PaintPriceMarkers        = true;
                ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                Length         = 2;
                Offset         = 0;
                TriggerLength  = 4;
                LongLength     = 200;
                ExtraLongLength= 1000;

                AddPlot(new Stroke(Brushes.DodgerBlue, 3), PlotStyle.Line, "LSMA");
                AddPlot(new Stroke(Brushes.Yellow,     3), PlotStyle.Line, "Trigger");
                AddPlot(new Stroke(Brushes.White,      2), PlotStyle.Line, "Long");
                AddPlot(new Stroke(Brushes.SteelBlue,  2), PlotStyle.Line, "ExtraLong");
                AddPlot(new Stroke(Brushes.Gray,       1), PlotStyle.Dot,  "Direction");
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
            LSMAPlot[0]   = lsma;

            // Stage 2: need TriggerLength bars of lsmaSeries filled for the SMA trigger.
            if (CurrentBar < (Length - 1) + (TriggerLength - 1)) return;
            double trig = 0.0;
            for (int i = 0; i < TriggerLength; i++) trig += lsmaSeries[i];
            trig /= TriggerLength;
            TriggerPlot[0] = trig;

            if (lsma > trig)      DirectionPlot[0] = 1;
            else if (lsma < trig) DirectionPlot[0] = -1;
            else                  DirectionPlot[0] = 0;

            // Long / Extra-Long reference plots. Guard their own history lengths.
            if (CurrentBar >= LongLength - 1)
                LongPlot[0] = LinRegOn(Input, LongLength, 0, 0);
            if (CurrentBar >= ExtraLongLength - 1)
                ExtraLongPlot[0] = LinRegOn(Input, ExtraLongLength, 0, 0);
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
        [Browsable(false), XmlIgnore] public Series<double> LSMAPlot      { get { return Values[0]; } }
        [Browsable(false), XmlIgnore] public Series<double> TriggerPlot   { get { return Values[1]; } }
        [Browsable(false), XmlIgnore] public Series<double> LongPlot      { get { return Values[2]; } }
        [Browsable(false), XmlIgnore] public Series<double> ExtraLongPlot { get { return Values[3]; } }
        [Browsable(false), XmlIgnore] public Series<double> DirectionPlot { get { return Values[4]; } }
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

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Trigger Length", Description = "SMA length applied to LSMA.", Order = 3, GroupName = "Parameters")]
        public int TriggerLength { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Long Length", Description = "linreg length for the Long reference plot (Pine: 200).", Order = 4, GroupName = "Parameters")]
        public int LongLength { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Extra Long Length", Description = "linreg length for the Extra-Long reference plot (Pine: 1000).", Order = 5, GroupName = "Parameters")]
        public int ExtraLongLength { get; set; }
        #endregion
    }
}

// NT8 auto-generates the Indicator / MarketAnalyzerColumn / Strategy
// partial-class factory methods for this indicator during compile. Do not
// author them by hand — doing so collides with NT8's auto-gen.
