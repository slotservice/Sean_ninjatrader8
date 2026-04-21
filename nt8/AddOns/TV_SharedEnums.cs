// =============================================================================
// TV_SharedEnums.cs
//
// Shared enums and helpers used by the TradingView → NT8 port.
//
// Place in:  Documents\NinjaTrader 8\bin\Custom\AddOns\TV_SharedEnums.cs
//
// All indicators and the strategy reference these enums. Keeping them in one
// AddOn file avoids duplicate-type collisions when indicators are compiled
// separately.
// =============================================================================

using System.ComponentModel;

namespace NinjaTrader.NinjaScript.AddOns.TVPort
{
    // -------------------------------------------------------------------------
    // Direction of an indicator's current reading.
    // Used by the strategy when computing "checked indicators align".
    // -------------------------------------------------------------------------
    public enum TVDirection
    {
        [Description("Neutral")] Neutral = 0,
        [Description("Up")]      Up      = 1,
        [Description("Down")]    Down    = -1
    }

    // -------------------------------------------------------------------------
    // Which of a chained indicator's output plots is forwarded to the next
    // indicator in the chain. Only exposed where the source indicator has more
    // than one meaningful output (LSMA Crossover, macZLSMA, Stoch RVI).
    // -------------------------------------------------------------------------
    public enum TVPlotSelector
    {
        [Description("Default (main plot)")] Default = 0,
        [Description("Trigger")]             Trigger = 1,
        [Description("K")]                   K       = 2,
        [Description("D")]                   D       = 3
    }

    // -------------------------------------------------------------------------
    // Reversal mode used by the main strategy.
    // -------------------------------------------------------------------------
    public enum TVReversalMode
    {
        [Description("Close only (no reverse)")]        CloseOnly      = 0,
        [Description("Reverse (single reversing order)")] DirectReverse = 1,
        [Description("Flatten first, then re-enter")]   FlattenFirst   = 2
    }
}
