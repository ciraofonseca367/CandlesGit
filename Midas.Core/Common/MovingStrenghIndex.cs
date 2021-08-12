using System;
using System.Collections.Generic;
using System.Linq;
using Midas.Util;

namespace Midas.Core.Common
{
    public class MovingStrenghIndex
    {
        private List<Trade> _trades;
        private DateTime _creationDate;

        public MovingStrenghIndex(int windowsSize)
        {
            _trades = new List<Trade>(20000);
            _creationDate = DateTime.UtcNow;
        }

        public void ResetState(double lastAmount, DateTime creationDate)
        {
            _creationDate = creationDate;
            _trades.Add(new Trade()
            {
                Amount = lastAmount,
                Date = DateTime.UtcNow
            });
        }

        public void AddTrade(double amount)
        {
            lock (_trades)
            {
                _trades.Add(new Trade()
                {
                    Amount = amount,
                    Date = DateTime.UtcNow
                });
            }
        }

        public bool IsStable()
        {
            return (DateTime.UtcNow - _creationDate).TotalSeconds >= 30;
        }


        public double GetValue(double windowSize)
        {
            lock (_trades)
            {
                var lastTopTrades = _trades
                .Where(t => t.Date >= DateTime.UtcNow.AddSeconds(windowSize * -1))
                .OrderBy(t => t.Date);

                double ret = 0;

                if (lastTopTrades.Count() > 0)
                    ret = lastTopTrades.Average(t => t.Amount);

                return ret;
            }
        }

        public double GetMovingAverage(double windowSize)
        {
            if (_trades.Count > 0)
            {
                double avgValue;

                lock (_trades)
                {
                    var lastTopTrades = _trades
                    .Where(t => t.Date >= DateTime.UtcNow.AddSeconds(windowSize * -1))
                    .OrderBy(t => t.Date);

                    avgValue = lastTopTrades.Average(t => t.Amount);
                }

                return avgValue;
            }
            else
                return 0;
        }

        internal void ResetState(object lastValue)
        {
            throw new NotImplementedException();
        }
    }

    public class Trade
    {
        public DateTime Date
        {
            get;
            set;
        }

        public double Amount
        {
            get;
            set;
        }
    }
}