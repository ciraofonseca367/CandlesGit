using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Midas.Core.Broker
{
    public abstract class BrokerOrder
    {
        protected OrderType _type;
        private OrderDirection _direction;
        protected string _orderId;
        protected Broker _broker;

        protected string _asset;

        public BrokerOrder(Broker broker, OrderDirection direction, OrderType type, string orderId, string asset)
        {
            _type = type;
            _orderId = orderId;
            _broker = broker;
            _asset = asset;
            _direction = direction;
            InError = false;
        }

        public BrokerOrder(Broker broker, string orderId, string asset) : this(broker, OrderDirection.NONE, OrderType.NONE, orderId, asset)
        {
        }

        public virtual double AverageValue
        {
            get;
            set;
        }

        public virtual double Price
        {
            get;
            set;
        }
        public string ErrorMsg { get; internal set; }
        public string ErrorCode { get; internal set; }         

        public virtual string Status
        {
            get;
            set;
        }

        public virtual bool InError
        {
            get;
            set;
        }


        public virtual string BrokerOrderId
        {
            get;
            set;
        }
        protected virtual OrderDirection Direction
        {
            get;
            set;
        }


        protected virtual OrderType Type
        {
            get;
            set;
        }

        internal bool WaitForConfimation(int v)
        {
            throw new NotImplementedException();
        }
    }

    public class BinanceBrokerOrder : BrokerOrder
    {
        public BinanceBrokerOrder(Broker broker,OrderDirection direction, OrderType type, string orderId, string asset) : base(broker, direction, type, orderId, asset)
        {
        }
        public BinanceBrokerOrder(Broker broker, string orderId, string asset) : base(broker, orderId, asset)
        {
        }
    }
}