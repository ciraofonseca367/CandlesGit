using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Midas.Core.Binance;
using MongoDB.Bson;

namespace Midas.Core.Broker
{
    public class BrokerOrder
    {
        public ObjectId Id
        {
            get;
            set;
        }

        private OrderType _type;
        private OrderDirection _direction;
        private string _orderId;
        private Broker _broker;

        private DateTime _creationDate;

        private List<TradeStreamItem> _trades;

        public BrokerOrder(Broker broker, OrderDirection direction, OrderType type, string orderId, DateTime creationDate)
        {
            _type = type;
            _orderId = orderId;
            _broker = broker;
            _direction = direction;
            InError = false;
            _creationDate = creationDate;
            HasTimeouted = false;
            _trades = new List<TradeStreamItem>();            
        }


        public BrokerOrder(Broker broker, string orderId, DateTime creationDate) : this(broker, OrderDirection.NONE, OrderType.NONE, orderId, creationDate)
        {
        }

        public TimeSpan WaitDuration(DateTime relativeNow)
        {
            return (relativeNow - CreationDate);
        }

        public double DesiredPrice
        {
            get;
            set;
        }

        public bool HasTimeouted
        {
            get;
            set;
        }

        public double AverageValue
        {
            get;
            set;
        }

        internal void AddTrade(TradeStreamItem tradeItem)
        {
            _trades.Add(tradeItem);
        }

        /*
            The quantity of the asset sent to the broker. The original amount intended to be sold or bought
        */
        public double AskedQuantity
        {
            get; set;
        }

        /*
            Use this method to get the executed Quantity.
            Here we return preferably the ExecutedQty from the last status check. If we don't have one we get it from trades history
        */
        public double CalculatedExecutedQuantity
        {
            get
            {
                double ret = 0;
                if(_trades.Count() > 0)
                    ret = _trades.Sum(t => t.Qty);
                return ret;
            }
        }

        public double ExecutedRatio
        {
            get
            {
                return (CalculatedExecutedQuantity/AskedQuantity) * 100;
            }
        }

        public string ErrorMsg { get; internal set; }
        public string ErrorCode { get; internal set; }

        public virtual string RawStatus
        {
            get;
            set;
        }

        public BrokerOrderStatus Status
        {
            get;
            set;
        }

        public BrokerOrderStatus CalculatedStatus
        {
            get
            {
                var executedQdy = _trades.Sum(t => t.Qty);
                BrokerOrderStatus ret = BrokerOrderStatus.None;
                if(executedQdy == 0)
                    ret = BrokerOrderStatus.NEW;
                else if(executedQdy < AskedQuantity && executedQdy > 0)
                    ret = BrokerOrderStatus.PARTIALLY_FILLED;
                else if(executedQdy.ToString("0.0000") == AskedQuantity.ToString("0.0000"))
                    ret = BrokerOrderStatus.FILLED;

                return ret;
            }
        }

        public double CalculatedAverageValue
        {
            get
            {
                double basedOnTheTrades = 0;
                if(_trades.Count() > 0)
                    basedOnTheTrades = _trades.Sum(t => t.Qty * t.Price) / _trades.Sum(t => t.Qty);
                    
                return basedOnTheTrades == 0 ? AverageValue : basedOnTheTrades;
            }
        }

        public bool IsPending
        {
            get
            {
                return CalculatedStatus == BrokerOrderStatus.NEW || CalculatedStatus == BrokerOrderStatus.None || CalculatedStatus == BrokerOrderStatus.PARTIALLY_FILLED;
            }
        }

        public bool IsFilled(double currentPrice)
        {
            return (
                (currentPrice > this.DesiredPrice && this.Direction == OrderDirection.SELL) ||
                currentPrice < this.DesiredPrice && this.Direction == OrderDirection.BUY);
       
        }

        public bool InError
        {
            get;
            set;
        }


        public string BrokerOrderId
        {
            get;set;
        }
        public OrderDirection Direction
        {
            get
            {
                return _direction;
            }
        }


        public OrderType Type
        {
            get
            {
                return _type;
            }
            set
            {
                _type = value;
            }
        }

        public string OrderId { get => _orderId; set => _orderId = value; }
        public DateTime CreationDate { get => _creationDate; }

        public override string ToString()
        {
            return $"{Direction}({Type}) {AskedQuantity:0.0000}/{CalculatedExecutedQuantity:0.0000}({ExecutedRatio:0.00}%) by ${CalculatedAverageValue:0.00}= {Status}:{CalculatedStatus} ({((CalculatedAverageValue-DesiredPrice)/DesiredPrice)*100:0.0000}%)";
        }

        public BrokerOrderDto GetMyDto()
        {
            return new BrokerOrderDto()
            {
                Type = this._type,
                Direction = this._direction,
                OrderId = this._orderId,
                CreationDate = this._creationDate,
                AverageValue = this.AverageValue
            };
        }
    }

    /*
Status	Description
NEW	The order has been accepted by the engine.
PARTIALLY_FILLED	A part of the order has been filled.
FILLED	The order has been completed.
CANCELED	The order has been canceled by the user.
PENDING_CANCEL	Currently unused
REJECTED	The order was not accepted by the engine and not processed.
EXPIRED	The order was canceled according to the order type's rules (e.g. LIMIT FOK orders with no fill, LIMIT IOC or MARKET orders that partially fill) or by the exchange, (e.g. orders canceled during liquidation, orders canceled during maintenance)
    */

    public enum BrokerOrderStatus
    {
        None,
        NEW,

        FILLED,

        EXPIRED,

        ERROR,

        CANCELED,

        PENDING_CANCEL,
        REJECTED,

        PARTIALLY_FILLED
    }

    public class BrokerOrderDto
    {

        public ObjectId Id
        {
            get;
            set;
        }

        public OrderType Type
        {
            get;
            set;
        }

        public OrderDirection Direction
        {
            get;
            set;
        }
        public string OrderId
        {
            get;
            set;
        }
        public DateTime CreationDate
        {
            get;
            set;
        }

        public double AverageValue
        {
            get;
            set;
        }
        public int Quantity { get; set; }
    }

}