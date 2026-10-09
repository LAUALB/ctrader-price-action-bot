using System;
using System.Collections.Generic;
using cAlgo.API;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class PriceActionStructureBotPRO : Robot
    {
        // ═════════════════════════════════════════════════════════════════
        // PROFESSIONAL PARAMETERS
        // ═════════════════════════════════════════════════════════════════

        #region Structure Parameters
        [Parameter("Swing Lookback (bars)", Group = "Structure Detection", DefaultValue = 24, MinValue = 10, MaxValue = 100)]
        public int SwingLookback { get; set; }

        [Parameter("Quality Threshold (min bars)", Group = "Structure Detection", DefaultValue = 3, MinValue = 1, MaxValue = 10)]
        public int QualityThreshold { get; set; }

        [Parameter("BOS Buffer (pips)", Group = "Structure Detection", DefaultValue = 3, MinValue = 1, MaxValue = 10)]
        public int BOSBufferPips { get; set; }

        [Parameter("Enable Liquidity Sweep", Group = "Structure Detection", DefaultValue = true)]
        public bool EnableLiquiditySweep { get; set; }

        [Parameter("Liquidity Sweep Threshold (pips)", Group = "Structure Detection", DefaultValue = 5, MinValue = 1, MaxValue = 20)]
        public int LiquiditySweepThreshold { get; set; }
        #endregion

        #region Session Filter Parameters
        [Parameter("Enable Session Filter", Group = "Session Filter", DefaultValue = true)]
        public bool EnableSessionFilter { get; set; }

        [Parameter("Use London Session", Group = "Session Filter", DefaultValue = true)]
        public bool UseLondonSession { get; set; }

        [Parameter("Use New York Session", Group = "Session Filter", DefaultValue = true)]
        public bool UseNewYorkSession { get; set; }

        [Parameter("London Start (UTC)", Group = "Session Filter", DefaultValue = 8, MinValue = 0, MaxValue = 23)]
        public int LondonStartHour { get; set; }

        [Parameter("London End (UTC)", Group = "Session Filter", DefaultValue = 17, MinValue = 0, MaxValue = 23)]
        public int LondonEndHour { get; set; }

        [Parameter("New York Start (UTC)", Group = "Session Filter", DefaultValue = 13, MinValue = 0, MaxValue = 23)]
        public int NewYorkStartHour { get; set; }

        [Parameter("New York End (UTC)", Group = "Session Filter", DefaultValue = 22, MinValue = 0, MaxValue = 23)]
        public int NewYorkEndHour { get; set; }
        #endregion

        #region Trading Parameters
        [Parameter("Risk % per trade", Group = "Risk Management", DefaultValue = 1.5, MinValue = 0.1, MaxValue = 5)]
        public double RiskPercent { get; set; }

        [Parameter("Max Concurrent Positions", Group = "Risk Management", DefaultValue = 2, MinValue = 1, MaxValue = 5)]
        public int MaxConcurrentPositions { get; set; }

        [Parameter("Stop Loss (pips)", Group = "Risk Management", DefaultValue = 25, MinValue = 5, MaxValue = 100)]
        public int StopLossPips { get; set; }

        [Parameter("Take Profit (pips)", Group = "Risk Management", DefaultValue = 50, MinValue = 10, MaxValue = 200)]
        public int TakeProfitPips { get; set; }

        [Parameter("Enable Trailing Stop", Group = "Risk Management", DefaultValue = true)]
        public bool EnableTrailingStop { get; set; }

        [Parameter("Trailing Stop Distance (pips)", Group = "Risk Management", DefaultValue = 15, MinValue = 5, MaxValue = 50)]
        public int TrailingStopDistance { get; set; }

        [Parameter("Breakeven Threshold (pips)", Group = "Risk Management", DefaultValue = 20, MinValue = 5, MaxValue = 50)]
        public int BreakevenThreshold { get; set; }
        #endregion

        #region Advanced Parameters
        [Parameter("Enable ChoCH Confirmation", Group = "Advanced", DefaultValue = true)]
        public bool EnableChoCHConfirmation { get; set; }

        [Parameter("ChoCH Confirmation Bars", Group = "Advanced", DefaultValue = 3, MinValue = 1, MaxValue = 10)]
        public int ChoCHConfirmationBars { get; set; }

        [Parameter("Enable Flush & Reclaim", Group = "Advanced", DefaultValue = true)]
        public bool EnableFlushAndReclaim { get; set; }

        [Parameter("Flush Threshold (pips)", Group = "Advanced", DefaultValue = 2, MinValue = 1, MaxValue = 10)]
        public int FlushThreshold { get; set; }

        [Parameter("Log Debug Messages", Group = "Advanced", DefaultValue = false)]
        public bool DebugLogging { get; set; }
        #endregion

        // ═════════════════════════════════════════════════════════════════
        // INTERNAL STATE
        // ═════════════════════════════════════════════════════════════════

        private enum MarketStructure
        {
            Bullish,      // HH + HL
            Bearish,      // LL + LH
            Neutral
        }

        private enum SessionType
        {
            London,
            NewYork,
            Off
        }

        // Structure tracking
        private MarketStructure currentStructure = MarketStructure.Neutral;
        private MarketStructure previousStructure = MarketStructure.Neutral;

        private SwingLevel lastBullishSwing = new SwingLevel();
        private SwingLevel lastBearishSwing = new SwingLevel();

        // BOS tracking
        private bool bullishBOSFormed = false;
        private bool bearishBOSFormed = false;
        private int barsSinceBullishBOS = 0;
        private int barsSinceBearishBOS = 0;

        // ChoCH tracking
        private bool bullishChoCHConfirmed = false;
        private bool bearishChoCHConfirmed = false;
        private int choCHConfirmationCounter = 0;

        // Liquidity sweep tracking
        private bool bullishLiquiditySweepExecuted = false;
        private bool bearishLiquiditySweepExecuted = false;

        // Flush & Reclaim
        private double bullishFlushLevel = double.NaN;
        private double bearishFlushLevel = double.NaN;

        // ═════════════════════════════════════════════════════════════════
        // SWING LEVEL CLASS
        // ═════════════════════════════════════════════════════════════════

        private class SwingLevel
        {
            public double Price { get; set; } = double.NaN;
            public int BarIndex { get; set; } = -1;
            public int Quality { get; set; } = 0; // How many bars confirmed this swing

            public bool IsValid()
            {
                return !double.IsNaN(Price) && BarIndex >= 0;
            }

            public void Reset()
            {
                Price = double.NaN;
                BarIndex = -1;
                Quality = 0;
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═════════════════════════════════════════════════════════════════

        protected override void OnStart()
        {
            Print("╔════════════════════════════════════════════════════════╗");
            Print("║  PRICE ACTION STRUCTURE BOT - PRO VERSION              ║");
            Print("║  Pure Price Action | Market Structure | BOS | ChoCH    ║");
            Print("╚════════════════════════════════════════════════════════╝");
            Print($"Symbol: {SymbolName} | Timeframe: {TimeFrame}");
            Print($"Risk: {RiskPercent}% | SL: {StopLossPips}pips | TP: {TakeProfitPips}pips");
        }

        protected override void OnBar()
        {
            if (Bars.Count < SwingLookback + 10)
                return;

            // Session filter
            if (!IsActiveSession())
            {
                if (DebugLogging)
                    Print($"[{Time}] Outside active session");
                return;
            }

            // Update market structure
            UpdateMarketStructure();

            // Check for BOS
            CheckBreakOfStructure();

            // Check for ChoCH
            CheckChangeOfCharacter();

            // Check for Liquidity Sweep
            if (EnableLiquiditySweep)
                CheckLiquiditySweep();

            // Check for Flush & Reclaim
            if (EnableFlushAndReclaim)
                CheckFlushAndReclaim();

            // Manage open positions
            ManageOpenPositions();

            // Execute trades if conditions met
            if (Positions.Count < MaxConcurrentPositions)
            {
                ExecuteTradesIfReady();
            }
        }

        protected override void OnStop()
        {
            Print("Bot stopped.");
        }

        // ═════════════════════════════════════════════════════════════════
        // SESSION FILTER
        // ═════════════════════════════════════════════════════════════════

        private bool IsActiveSession()
        {
            if (!EnableSessionFilter)
                return true;

            int currentHour = Time.Hour;

            if (UseLondonSession && currentHour >= LondonStartHour && currentHour < LondonEndHour)
                return true;

            if (UseNewYorkSession && currentHour >= NewYorkStartHour && currentHour < NewYorkEndHour)
                return true;

            return false;
        }

        private SessionType GetCurrentSession()
        {
            int currentHour = Time.Hour;

            if (UseLondonSession && currentHour >= LondonStartHour && currentHour < LondonEndHour)
                return SessionType.London;

            if (UseNewYorkSession && currentHour >= NewYorkStartHour && currentHour < NewYorkEndHour)
                return SessionType.NewYork;

            return SessionType.Off;
        }

        // ═════════════════════════════════════════════════════════════════
        // MARKET STRUCTURE DETECTION
        // ═════════════════════════════════════════════════════════════════

        private void UpdateMarketStructure()
        {
            int lastBarIndex = Bars.Count - 1;

            // Find recent swing high and low
            SwingLevel recentHigh = FindRecentSwingHigh(lastBarIndex);
            SwingLevel recentLow = FindRecentSwingLow(lastBarIndex);

            if (!recentHigh.IsValid() || !recentLow.IsValid())
                return;

            // Evaluate structure
            previousStructure = currentStructure;

            if (recentHigh.Price > lastBullishSwing.Price && recentLow.Price > lastBearishSwing.Price)
            {
                // Higher High + Higher Low = Bullish Structure
                currentStructure = MarketStructure.Bullish;
                lastBullishSwing = recentHigh;
                lastBearishSwing = recentLow;

                if (DebugLogging && previousStructure != MarketStructure.Bullish)
                    Print($"[{Time}] STRUCTURE: Bullish (HH: {recentHigh.Price}, HL: {recentLow.Price})");
            }
            else if (recentHigh.Price < lastBullishSwing.Price && recentLow.Price < lastBearishSwing.Price)
            {
                // Lower Low + Lower High = Bearish Structure
                currentStructure = MarketStructure.Bearish;
                lastBullishSwing = recentHigh;
                lastBearishSwing = recentLow;

                if (DebugLogging && previousStructure != MarketStructure.Bearish)
                    Print($"[{Time}] STRUCTURE: Bearish (LH: {recentHigh.Price}, LL: {recentLow.Price})");
            }
            else
            {
                currentStructure = MarketStructure.Neutral;
            }
        }

        private SwingLevel FindRecentSwingHigh(int centerIndex)
        {
            int start = Math.Max(0, centerIndex - SwingLookback);
            int end = centerIndex;

            SwingLevel bestSwing = new SwingLevel();

            for (int i = end; i >= start; i--)
            {
                if (i == 0 || i == Bars.Count - 1)
                    continue;

                double high = Bars.HighPrices[i];
                bool isSwingHigh = true;
                int quality = 0;

                // Check left side
                for (int j = i - 1; j >= Math.Max(0, i - QualityThreshold); j--)
                {
                    if (Bars.HighPrices[j] >= high)
                    {
                        isSwingHigh = false;
                        break;
                    }
                    quality++;
                }

                // Check right side
                if (isSwingHigh)
                {
                    for (int j = i + 1; j <= Math.Min(Bars.Count - 1, i + QualityThreshold); j++)
                    {
                        if (Bars.HighPrices[j] > high)
                        {
                            isSwingHigh = false;
                            break;
                        }
                        quality++;
                    }
                }

                if (isSwingHigh && quality >= QualityThreshold)
                {
                    bestSwing.Price = high;
                    bestSwing.BarIndex = i;
                    bestSwing.Quality = quality;
                    return bestSwing;
                }
            }

            return bestSwing;
        }

        private SwingLevel FindRecentSwingLow(int centerIndex)
        {
            int start = Math.Max(0, centerIndex - SwingLookback);
            int end = centerIndex;

            SwingLevel bestSwing = new SwingLevel();

            for (int i = end; i >= start; i--)
            {
                if (i == 0 || i == Bars.Count - 1)
                    continue;

                double low = Bars.LowPrices[i];
                bool isSwingLow = true;
                int quality = 0;

                // Check left side
                for (int j = i - 1; j >= Math.Max(0, i - QualityThreshold); j--)
                {
                    if (Bars.LowPrices[j] <= low)
                    {
                        isSwingLow = false;
                        break;
                    }
                    quality++;
                }

                // Check right side
                if (isSwingLow)
                {
                    for (int j = i + 1; j <= Math.Min(Bars.Count - 1, i + QualityThreshold); j++)
                    {
                        if (Bars.LowPrices[j] < low)
                        {
                            isSwingLow = false;
                            break;
                        }
                        quality++;
                    }
                }

                if (isSwingLow && quality >= QualityThreshold)
                {
                    bestSwing.Price = low;
                    bestSwing.BarIndex = i;
                    bestSwing.Quality = quality;
                    return bestSwing;
                }
            }

            return bestSwing;
        }

        // ═════════════════════════════════════════════════════════════════
        // BREAK OF STRUCTURE (BOS)
        // ═════════════════════════════════════════════════════════════════

        private void CheckBreakOfStructure()
        {
            int lastBar = Bars.Count - 1;
            double high = Bars.HighPrices[lastBar];
            double low = Bars.LowPrices[lastBar];
            double buffer = Symbol.PipSize * BOSBufferPips;

            // Bullish BOS: break above prior swing high with buffer
            if (currentStructure == MarketStructure.Bullish && lastBullishSwing.IsValid())
            {
                if (high > lastBullishSwing.Price + buffer)
                {
                    if (!bullishBOSFormed)
                    {
                        bullishBOSFormed = true;
                        barsSinceBullishBOS = 0;

                        if (DebugLogging)
                            Print($"[{Time}] ✓ BULLISH BOS at {high} (above {lastBullishSwing.Price})");
                    }
                }
            }

            // Bearish BOS: break below prior swing low with buffer
            if (currentStructure == MarketStructure.Bearish && lastBearishSwing.IsValid())
            {
                if (low < lastBearishSwing.Price - buffer)
                {
                    if (!bearishBOSFormed)
                    {
                        bearishBOSFormed = true;
                        barsSinceBearishBOS = 0;

                        if (DebugLogging)
                            Print($"[{Time}] ✓ BEARISH BOS at {low} (below {lastBearishSwing.Price})");
                    }
                }
            }

            // Track bars since BOS
            if (bullishBOSFormed)
                barsSinceBullishBOS++;
            if (bearishBOSFormed)
                barsSinceBearishBOS++;
        }

        // ═════════════════════════════════════════════════════════════════
        // CHANGE OF CHARACTER (ChoCH)
        // ═════════════════════════════════════════════════════════════════

        private void CheckChangeOfCharacter()
        {
            if (previousStructure == currentStructure)
                return;

            // Bullish to Bearish ChoCH
            if (previousStructure == MarketStructure.Bullish && currentStructure == MarketStructure.Bearish)
            {
                if (EnableChoCHConfirmation)
                {
                    choCHConfirmationCounter++;
                    if (choCHConfirmationCounter >= ChoCHConfirmationBars)
                    {
                        bearishChoCHConfirmed = true;
                        bullishChoCHConfirmed = false;

                        if (DebugLogging)
                            Print($"[{Time}] ✓ BEARISH ChoCH CONFIRMED (after {ChoCHConfirmationBars} bars)");

                        choCHConfirmationCounter = 0;
                    }
                }
                else
                {
                    bearishChoCHConfirmed = true;
                    bullishChoCHConfirmed = false;

                    if (DebugLogging)
                        Print($"[{Time}] ✓ BEARISH ChoCH DETECTED");
                }
            }

            // Bearish to Bullish ChoCH
            if (previousStructure == MarketStructure.Bearish && currentStructure == MarketStructure.Bullish)
            {
                if (EnableChoCHConfirmation)
                {
                    choCHConfirmationCounter++;
                    if (choCHConfirmationCounter >= ChoCHConfirmationBars)
                    {
                        bullishChoCHConfirmed = true;
                        bearishChoCHConfirmed = false;

                        if (DebugLogging)
                            Print($"[{Time}] ✓ BULLISH ChoCH CONFIRMED (after {ChoCHConfirmationBars} bars)");

                        choCHConfirmationCounter = 0;
                    }
                }
                else
                {
                    bullishChoCHConfirmed = true;
                    bearishChoCHConfirmed = false;

                    if (DebugLogging)
                        Print($"[{Time}] ✓ BULLISH ChoCH DETECTED");
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // LIQUIDITY SWEEP
        // ═════════════════════════════════════════════════════════════════

        private void CheckLiquiditySweep()
        {
            int lastBar = Bars.Count - 1;
            double high = Bars.HighPrices[lastBar];
            double low = Bars.LowPrices[lastBar];
            double sweepBuffer = Symbol.PipSize * LiquiditySweepThreshold;

            // Bullish sweep: above recent swing high + threshold
            if (currentStructure == MarketStructure.Bullish && lastBullishSwing.IsValid())
            {
                if (high > lastBullishSwing.Price + sweepBuffer)
                {
                    if (!bullishLiquiditySweepExecuted)
                    {
                        bullishLiquiditySweepExecuted = true;
                        if (DebugLogging)
                            Print($"[{Time}] ⚡ BULLISH LIQUIDITY SWEEP at {high}");
                    }
                }
            }

            // Bearish sweep: below recent swing low - threshold
            if (currentStructure == MarketStructure.Bearish && lastBearishSwing.IsValid())
            {
                if (low < lastBearishSwing.Price - sweepBuffer)
                {
                    if (!bearishLiquiditySweepExecuted)
                    {
                        bearishLiquiditySweepExecuted = true;
                        if (DebugLogging)
                            Print($"[{Time}] ⚡ BEARISH LIQUIDITY SWEEP at {low}");
                    }
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // FLUSH & RECLAIM
        // ═════════════════════════════════════════════════════════════════

        private void CheckFlushAndReclaim()
        {
            int lastBar = Bars.Count - 1;
            double close = Bars.ClosePrices[lastBar];
            double flushBuffer = Symbol.PipSize * FlushThreshold;

            // Bullish flush: price touches below last bearish swing then reclaims
            if (currentStructure == MarketStructure.Bullish && lastBearishSwing.IsValid())
            {
                if (close < lastBearishSwing.Price + flushBuffer)
                {
                    bullishFlushLevel = lastBearishSwing.Price;
                }
                // Reclaim check
                if (!double.IsNaN(bullishFlushLevel) && close > bullishFlushLevel + flushBuffer)
                {
                    if (DebugLogging)
                        Print($"[{Time}] 🔄 BULLISH FLUSH & RECLAIM at {close}");
                    bullishFlushLevel = double.NaN;
                }
            }

            // Bearish flush: price touches above last bullish swing then reclaims down
            if (currentStructure == MarketStructure.Bearish && lastBullishSwing.IsValid())
            {
                if (close > lastBullishSwing.Price - flushBuffer)
                {
                    bearishFlushLevel = lastBullishSwing.Price;
                }
                // Reclaim check
                if (!double.IsNaN(bearishFlushLevel) && close < bearishFlushLevel - flushBuffer)
                {
                    if (DebugLogging)
                        Print($"[{Time}] 🔄 BEARISH FLUSH & RECLAIM at {close}");
                    bearishFlushLevel = double.NaN;
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // TRADE EXECUTION
        // ═════════════════════════════════════════════════════════════════

        private void ExecuteTradesIfReady()
        {
            // Bullish setup: BOS + ChoCH confirmation
            if (bullishBOSFormed && currentStructure == MarketStructure.Bullish)
            {
                if (EnableChoCHConfirmation && !bullishChoCHConfirmed)
                    return;

                double entry = Symbol.Ask;
                double sl = entry - (StopLossPips * Symbol.PipSize);
                double tp = entry + (TakeProfitPips * Symbol.PipSize);

                double volume = CalculateVolume(entry, sl);
                if (volume <= 0)
                    return;

                string label = $"BUY_BOS_{Bars.Count}";
                if (DebugLogging)
                    Print($"[{Time}] → EXECUTING BUY: {volume} @ {entry} SL:{sl} TP:{tp}");

                ExecuteMarketOrder(TradeType.Buy, SymbolName, volume, label, sl, tp);
                bullishBOSFormed = false;
                bullishChoCHConfirmed = false;
                return;
            }

            // Bearish setup: BOS + ChoCH confirmation
            if (bearishBOSFormed && currentStructure == MarketStructure.Bearish)
            {
                if (EnableChoCHConfirmation && !bearishChoCHConfirmed)
                    return;

                double entry = Symbol.Bid;
                double sl = entry + (StopLossPips * Symbol.PipSize);
                double tp = entry - (TakeProfitPips * Symbol.PipSize);

                double volume = CalculateVolume(entry, sl);
                if (volume <= 0)
                    return;

                string label = $"SELL_BOS_{Bars.Count}";
                if (DebugLogging)
                    Print($"[{Time}] → EXECUTING SELL: {volume} @ {entry} SL:{sl} TP:{tp}");

                ExecuteMarketOrder(TradeType.Sell, SymbolName, volume, label, sl, tp);
                bearishBOSFormed = false;
                bearishChoCHConfirmed = false;
                return;
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // POSITION MANAGEMENT
        // ═════════════════════════════════════════════════════════════════

        private void ManageOpenPositions()
        {
            foreach (var position in Positions)
            {
                if (position.SymbolName != SymbolName)
                    continue;

                // Move to breakeven
                if (EnableTrailingStop)
                {
                    ApplyTrailingStop(position);
                }

                // Breakeven protection
                ApplyBreakevenStop(position);
            }
        }

        private void ApplyTrailingStop(Position position)
        {
            double currentSL = position.StopLoss ?? 0;

            if (position.TradeType == TradeType.Buy)
            {
                double profit = Bars.ClosePrices[Bars.Count - 1] - position.EntryPrice;
                double profitPips = profit / Symbol.PipSize;

                if (profitPips > 0)
                {
                    double newSL = position.EntryPrice + ((profitPips - TrailingStopDistance) * Symbol.PipSize);
                    if (newSL > currentSL)
                    {
                        ModifyPosition(position, newSL, position.TakeProfit);
                    }
                }
            }
            else if (position.TradeType == TradeType.Sell)
            {
                double profit = position.EntryPrice - Bars.ClosePrices[Bars.Count - 1];
                double profitPips = profit / Symbol.PipSize;

                if (profitPips > 0)
                {
                    double newSL = position.EntryPrice - ((profitPips - TrailingStopDistance) * Symbol.PipSize);
                    if (newSL < currentSL)
                    {
                        ModifyPosition(position, newSL, position.TakeProfit);
                    }
                }
            }
        }

        private void ApplyBreakevenStop(Position position)
        {
            double currentSL = position.StopLoss ?? 0;
            double breakeven = position.EntryPrice;

            if (position.TradeType == TradeType.Buy)
            {
                double profitPips = (Bars.ClosePrices[Bars.Count - 1] - position.EntryPrice) / Symbol.PipSize;
                if (profitPips > BreakevenThreshold && currentSL < breakeven)
                {
                    ModifyPosition(position, breakeven, position.TakeProfit);
                }
            }
            else if (position.TradeType == TradeType.Sell)
            {
                double profitPips = (position.EntryPrice - Bars.ClosePrices[Bars.Count - 1]) / Symbol.PipSize;
                if (profitPips > BreakevenThreshold && currentSL > breakeven)
                {
                    ModifyPosition(position, breakeven, position.TakeProfit);
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // RISK CALCULATION
        // ═════════════════════════════════════════════════════════════════

        private double CalculateVolume(double entryPrice, double stopLossPrice)
        {
            double riskAmount = Account.Balance * (RiskPercent / 100.0);
            double stopDistancePips = Math.Abs(entryPrice - stopLossPrice) / Symbol.PipSize;

            if (stopDistancePips <= 0)
                return 0;

            double pipValue = Symbol.PipValue;
            if (pipValue <= 0)
                return 0;

            double volume = riskAmount / (stopDistancePips * pipValue);

            // Respect minimum/maximum volume constraints
            volume = Math.Max(Symbol.VolumeInUnitsMin, Math.Min(volume, Symbol.VolumeInUnitsMax));

            return Symbol.NormalizeVolume(volume);
        }
    }
}
