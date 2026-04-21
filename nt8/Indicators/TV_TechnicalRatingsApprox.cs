// =============================================================================
// TV_TechnicalRatingsApprox.cs
//
// *** LABELLED APPROXIMATION — DO NOT TREAT AS 1:1 PARITY ***
//
// TradingView's "Technical Ratings" (INDICATOR 6/7) imports the closed
// `TradingView/TechnicalRating/3` library, which is NOT open source.
// Exact one-for-one behavior from the Pine source alone is impossible.
//
// Per the plan's instructions (Section 6 / "Technical Ratings handling"):
//     - build the rest completely
//     - isolate Technical Ratings safely
//     - disable by default if exact parity is uncertain
//     - document the limitation clearly and honestly
//     - do not fabricate hidden TradingView library logic
//
// This file implements a *recognisable approximation* of TradingView's
// rating logic using the classic construction documented publicly by
// TradingView (before the library wrap): 15 MAs + 11 oscillators mapped
// to a score in [-1, +1]. Because the internals of the private library
// differ in small details (exact MA mix, smoothing, and weighting of
// tied states), output should be considered directional-only and NOT
// bar-identical to TV.
//
// DEFAULT: this indicator is NOT wired into the strategy's signal path.
// It is available only if the user explicitly checks "Use Technical Ratings".
//
// Client default settings (XTBUILDER2.8.4):
//     Repainting = On, Rating uses MAs + Oscillators, MA weight 30%,
//     Plot Style = Columns.
//
// APPROXIMATION POLICY: values of this indicator are derived from the
// open documentation of TradingView's rating method:
//   - MAs: SMA10/20/30/50/100/200, EMA10/20/30/50/100/200, VWMA20, HullMA9,
//     and Ichimoku cloud vote.
//   - Oscillators: RSI14, StochK/D, CCI20, ADX14+DI, AO, Momentum10, MACD,
//     StochRSI, WilliamsR, BullBearPower, UltimateOscillator.
// Each signal returns +1 buy / 0 neutral / -1 sell, and the rating is
// (count_buy - count_sell) / total.
//
// For this port we implement a SIMPLIFIED subset (noted below) that
// captures the directional behavior without re-implementing 20+
// indicators. If the client wants bar-matching parity, the right path
// is a separate project phase using TradingView's public rating spec.
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
    public class TV_TechnicalRatingsApprox : Indicator
    {
        // Helpers / built-ins used
        private Indicators.SMA  sma10, sma20, sma30, sma50, sma100, sma200;
        private Indicators.EMA  ema10, ema20, ema30, ema50, ema100, ema200;
        private Indicators.RSI  rsi14;
        private Indicators.CCI  cci20;
        private Indicators.MACD macd;
        private Indicators.ADX  adx14;
        private Indicators.StochasticsFast stoch;
        private Indicators.WilliamsR wr14;
        private Indicators.Momentum mom10;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Technical Ratings — *labelled approximation* (TV library not open-source).";
                Name                     = "TV_TechnicalRatingsApprox";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = false;
                DisplayInDataBox         = true;
                PaintPriceMarkers        = true;
                ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                MAWeightPercent = 30;

                AddPlot(new Stroke(Brushes.Lime, 2), PlotStyle.Bar, "TotalRating");
                AddPlot(new Stroke(Brushes.Gray, 1), PlotStyle.Dot, "MARating");
                AddPlot(new Stroke(Brushes.Gray, 1), PlotStyle.Dot, "OscRating");
                AddPlot(new Stroke(Brushes.Gray, 1), PlotStyle.Dot, "Direction");

                AddLine(Brushes.LightGray,  0.5, "Strong Buy");
                AddLine(Brushes.LightGray,  0.1, "Buy");
                AddLine(Brushes.Gray,       0.0, "Neutral");
                AddLine(Brushes.LightGray, -0.1, "Sell");
                AddLine(Brushes.LightGray, -0.5, "Strong Sell");
            }
            else if (State == State.DataLoaded)
            {
                sma10  = SMA(10);   sma20  = SMA(20);   sma30  = SMA(30);
                sma50  = SMA(50);   sma100 = SMA(100);  sma200 = SMA(200);
                ema10  = EMA(10);   ema20  = EMA(20);   ema30  = EMA(30);
                ema50  = EMA(50);   ema100 = EMA(100);  ema200 = EMA(200);
                rsi14  = RSI(14, 3);
                cci20  = CCI(20);
                macd   = MACD(12, 26, 9);
                adx14  = ADX(14);
                stoch  = StochasticsFast(3, 14);
                wr14   = WilliamsR(14);
                mom10  = Momentum(10);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 200) return; // need enough for the longest MA

            // ---------- MA votes ----------
            int maBuy = 0, maSell = 0;
            double close = Close[0];

            MAVote(close, sma10[0],  ref maBuy, ref maSell); MAVote(close, sma20[0],  ref maBuy, ref maSell);
            MAVote(close, sma30[0],  ref maBuy, ref maSell); MAVote(close, sma50[0],  ref maBuy, ref maSell);
            MAVote(close, sma100[0], ref maBuy, ref maSell); MAVote(close, sma200[0], ref maBuy, ref maSell);
            MAVote(close, ema10[0],  ref maBuy, ref maSell); MAVote(close, ema20[0],  ref maBuy, ref maSell);
            MAVote(close, ema30[0],  ref maBuy, ref maSell); MAVote(close, ema50[0],  ref maBuy, ref maSell);
            MAVote(close, ema100[0], ref maBuy, ref maSell); MAVote(close, ema200[0], ref maBuy, ref maSell);

            int maTotal = 12;
            double maRating = maTotal == 0 ? 0 : ((double)(maBuy - maSell)) / maTotal;

            // ---------- Oscillator votes ----------
            int oscBuy = 0, oscSell = 0, oscTotal = 0;

            // RSI
            oscTotal++;
            if      (rsi14[0] < 30) oscBuy++;
            else if (rsi14[0] > 70) oscSell++;

            // CCI
            oscTotal++;
            if      (cci20[0] < -100) oscBuy++;
            else if (cci20[0] >  100) oscSell++;

            // MACD histogram direction
            oscTotal++;
            if      (macd.Diff[0] > 0) oscBuy++;
            else if (macd.Diff[0] < 0) oscSell++;

            // ADX direction-agnostic — ADX > 25 means trending. Use +DI vs -DI proxy via macd.Diff slope.
            // (Simplified; kept directional-only per APPROXIMATION POLICY in header.)
            oscTotal++;
            if (adx14[0] > 25)
            {
                if (Close[0] > Close[1]) oscBuy++;
                else if (Close[0] < Close[1]) oscSell++;
            }

            // Stochastic K
            oscTotal++;
            if      (stoch.K[0] < 20) oscBuy++;
            else if (stoch.K[0] > 80) oscSell++;

            // Williams %R
            oscTotal++;
            if      (wr14[0] < -80) oscBuy++;
            else if (wr14[0] > -20) oscSell++;

            // Momentum
            oscTotal++;
            if      (mom10[0] > 0) oscBuy++;
            else if (mom10[0] < 0) oscSell++;

            double oscRating = oscTotal == 0 ? 0 : ((double)(oscBuy - oscSell)) / oscTotal;

            // ---------- Total (weighted) ----------
            double wMA  = MAWeightPercent / 100.0;
            double wOsc = 1.0 - wMA;
            double total = wMA * maRating + wOsc * oscRating;

            MARatingPlot[0]   = maRating;
            OscRatingPlot[0]  = oscRating;
            TotalRatingPlot[0]= total;

            if      (total >  0.1) DirectionPlot[0] = 1;
            else if (total < -0.1) DirectionPlot[0] = -1;
            else                   DirectionPlot[0] = 0;
        }

        // Simple private method instead of a local function (defensive for older C# compilers).
        private static void MAVote(double close, double maVal, ref int buy, ref int sell)
        {
            if      (close > maVal) buy++;
            else if (close < maVal) sell++;
        }

        #region Public accessors
        [Browsable(false), XmlIgnore] public Series<double> TotalRatingPlot { get { return Values[0]; } }
        [Browsable(false), XmlIgnore] public Series<double> MARatingPlot    { get { return Values[1]; } }
        [Browsable(false), XmlIgnore] public Series<double> OscRatingPlot   { get { return Values[2]; } }
        [Browsable(false), XmlIgnore] public Series<double> DirectionPlot   { get { return Values[3]; } }
        #endregion

        #region Parameters
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "MA Weight (%)", Description = "Percentage weight of MA rating vs oscillator rating.", Order = 1, GroupName = "Parameters")]
        public int MAWeightPercent { get; set; }
        #endregion
    }
}

// NT8 auto-generates the Indicator / MarketAnalyzerColumn / Strategy
// partial-class factory methods for this indicator during compile. Do not
// author them by hand — doing so collides with NT8's auto-gen.
