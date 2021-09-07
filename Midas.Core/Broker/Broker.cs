using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Dynamic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Midas.Core.Util;

namespace Midas.Core.Broker
{
    public abstract class Broker
    {
        protected string _baseUrl;

        public static Broker GetBroker(string identification, dynamic config)
        {
            Broker ret = null;
            if (identification == "Binance")
            {
                if (RunParameters.GetInstance().ScoreThreshold >= -1)
                    ret = new BinanceBroker();
                else
                    ret = new TestBroker();

                ret.SetParameters(config);

            }
            else
                throw new ArgumentException("No such broker - " + identification);

            return ret;
        }

        public abstract void SetParameters(dynamic config);


        internal virtual dynamic Post(string url, string queryString, string body, int timeOut)
        {
            return Post(url, queryString, body, timeOut);
        }

        private HttpClient GetHttpClient(Dictionary<string, string> headers)
        {
            var httpClient = new HttpClient();

            httpClient.BaseAddress = new Uri(_baseUrl);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (headers != null)
            {
                foreach (var header in headers)
                    httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
            }

            return httpClient;
        }

        private dynamic ProcessResponse(Task<HttpResponseMessage> res, HttpClient httpClient, int timeOut, string completeUrl, string action)
        {
            object parsedResponse;
            if (res.Wait(timeOut))
            {
                var jsonResponse = res.Result.Content.ReadAsStringAsync();
                if (jsonResponse.Wait(timeOut))
                {
                    parsedResponse = JsonConvert.DeserializeObject(jsonResponse.Result);
                    TraceAndLog.GetInstance().LogTraceHttpAction("Broker", action, httpClient.DefaultRequestHeaders, res.Result.Headers, completeUrl, jsonResponse.Result);
                }
                else
                {
                    throw new ArgumentException("Timeout sending Post Request to - " + completeUrl);
                }
            }
            else
            {
                throw new ArgumentException("Timeout sending Post Request to - " + completeUrl);
            }

            return parsedResponse;
        }

        internal virtual dynamic Post(string url, string queryString, string body, Dictionary<string, string> headers, int timeOut)
        {
            var httpClient = GetHttpClient(headers);

            var content = new StringContent(body);

            var res = httpClient.PostAsync(url + queryString, content);
            var parsedResponse = ProcessResponse(res, httpClient, timeOut, url + queryString, "Post");

            return parsedResponse;
        }


        internal virtual dynamic Get(string url, string queryString, int timeOut)
        {
            return Get(url, queryString, timeOut, true);
        }
        internal virtual dynamic Get(string url, string queryString, int timeOut, bool secure)
        {
            return Get(url, queryString, null, timeOut);
        }
        internal virtual dynamic Get(string url, string queryString, Dictionary<string, string> headers, int timeOut)
        {
            var httpClient = GetHttpClient(headers);

            var res = httpClient.GetAsync(url + queryString);

            return ProcessResponse(res, httpClient, timeOut, url + queryString, "GET");
        }

        internal virtual dynamic Delete(string url, string queryString, int timeOut)
        {
            return Delete(url, queryString, null, timeOut);
        }

        internal virtual dynamic Delete(string url, string queryString, Dictionary<string, string> headers, int timeOut)
        {
            var httpClient = GetHttpClient(headers);

            var res = httpClient.DeleteAsync(url + queryString);

            return ProcessResponse(res, httpClient, timeOut, url + queryString, "Delete");

        }

        public abstract double GetPriceQuote(string asset);

        public abstract BrokerOrder MarketOrder(string orderId, string asset, OrderDirection direction, double qty, int timeOut);

        public abstract BrokerOrder LimitOrder(string orderId, string asset, OrderDirection direction, double qty, int timeOut, double price);

        public abstract BrokerOrder OrderStatus(string orderId, string asset, int timeOut);

        public abstract List<BalanceRecord> AccountBalance(int timeOut);

        public abstract bool CancelOrder(string orderId, string asset, int timeOut);

        public abstract void CancelAllOpenOrdersAsync(string asset, int timeOut);

        public abstract void CancelAllOpenOrders(string asset, int timeOut);

        public abstract Task<BrokerOrder> SmartOrderAsync(string orderId, string asset, OrderDirection direction, double qty, int timeOut, double price, PriceBias bias);
    }

    public class BrokerException : ApplicationException
    {
        public BrokerException(string msg, Exception inner) : base(msg, inner) { }
    }

    public class BinanceBroker : Broker
    {

        private static double BIAS_DIFF_FORBUY = 1 - 0.0001;
        private static double BIAS_DIFF_FORSELL = 1 + 0.0001;

        private string _MarketOrderUri = "/api/v3/order?";

        private string _openOrdersUri = "/api/v3/openOrders?";

        private string _accountUri = "/api/v3/account?";

        private string _priceTickerUri = "/api/v3/ticker/price?";
        private string _createMarketOrderQueryStringTemplate = "symbol={0}&side={1}&type={2}&quantity={3}&newClientOrderId={4}";
        private string _createLimitOrderQueryStringTemplate = "symbol={0}&side={1}&type={2}&timeInForce=GTC&quantity={3}&newClientOrderId={4}&price={5}&newOrderRespType=ACK";
        private string _orderStatusQueryStringTemplate = "symbol={0}&origClientOrderId={1}";
        private string _cancelOrderQueryStringTemplate = "symbol={0}&origClientOrderId={1}";
        private string _cancelAllOrdersQueryStringTemplate = "symbol={0}";
        private string _symbolPriceTicker = "symbol={0}";

        private string _host;

        private string _apiKey, _apiSecret;

        public override void SetParameters(dynamic config)
        {
            _baseUrl = config.EndPoint;
            _apiKey = config.ApiKey;
            _apiSecret = config.ApiSecret;
            _host = config.Host;

            if (String.IsNullOrEmpty(_baseUrl)) throw new ArgumentException("No EndPoint found");
            if (String.IsNullOrEmpty(_apiKey)) throw new ArgumentException("No ApiKey found");
            if (String.IsNullOrEmpty(_apiSecret)) throw new ArgumentException("No ApiSecret found");

            BIAS_DIFF_FORBUY = Convert.ToDouble(config.BIAS_DIFF_FORBUY);
            BIAS_DIFF_FORSELL = Convert.ToDouble(config.BIAS_DIFF_FORSELL);
        }

        public override double GetPriceQuote(string asset)
        {
            var ret = Get(
                _priceTickerUri,
                String.Format(_symbolPriceTicker, asset),
                10000, false
            );

            double quote = 0;
            if (ret.price != null)
                quote = Convert.ToDouble(ret.price);

            return quote;
        }

        public override bool CancelOrder(string orderId, string asset, int timeOut)
        {
            string queryString;
            var cancelled = false;

            queryString = String.Format(
                _cancelOrderQueryStringTemplate,
                asset, orderId
            );

            var res = Delete(_MarketOrderUri, queryString, null, timeOut);

            string errorMsg = null;
            string status = null;
            if (res.status != null)
                status = Convert.ToString(res.status);

            if (res.msg != null)
                errorMsg = Convert.ToString(res.msg);

            if (!String.IsNullOrEmpty(errorMsg))
                throw new ApplicationException("Binance error - " + errorMsg);
            else
            {
                if (status == "CANCELED")
                {
                    cancelled = true;
                }
            }

            return cancelled;
        }

        public override async void CancelAllOpenOrdersAsync(string asset, int timeOut)
        {
            Task cancelTask = Task.Run(() =>
            {
                CancelAllOpenOrders(asset, timeOut);
            });

            await cancelTask;
        }

        public static bool IsPropertyExist(dynamic settings, string name)
        {
            if (settings is ExpandoObject)
                return ((IDictionary<string, object>)settings).ContainsKey(name);

            return settings.GetType().GetProperty(name) != null;
        }

        public override void CancelAllOpenOrders(string asset, int timeOut)
        {
            string queryString;

            queryString = String.Format(
                _cancelAllOrdersQueryStringTemplate,
                asset
            );

            var res = Delete(_openOrdersUri, queryString, null, timeOut);

            string errorMsg = null;
            if (IsPropertyExist(res, "msg"))
                errorMsg = Convert.ToString(res.msg);

            if (!String.IsNullOrEmpty(errorMsg))
                throw new ApplicationException("Binance error - " + errorMsg);

            if (res.Count == 0)
            {
                throw new ArgumentException("No orders were cancelled");
            }
        }

        public BrokerOrder NewOrder(string orderId, string asset, OrderType type, OrderDirection direction, double qty, double price, int timeOut)
        {
            string queryString;

            if (type == OrderType.MARKET)
                queryString = String.Format(
                    _createMarketOrderQueryStringTemplate,
                    asset, direction.ToString(), type.ToString(), qty.ToString("0.0000").Replace(",", "."), orderId
                );
            else
                queryString = String.Format(
                    _createLimitOrderQueryStringTemplate,
                    asset, direction.ToString(), type.ToString(), qty.ToString("0.0000").Replace(",", "."), orderId, price.ToString("0.00")
                );

            var res = Post(_MarketOrderUri, queryString, "", timeOut);

            BinanceBrokerOrder order = new BinanceBrokerOrder(this, direction, type, orderId, asset);

            string errorMsg = null;
            string status = null;
            if (res.status != null)
                status = Convert.ToString(res.status);

            if (res.msg != null)
            {
                errorMsg = Convert.ToString(res.msg);
                order.InError = true;
                order.ErrorMsg = errorMsg;
                order.ErrorCode = Convert.ToString(res.code);
            }

            List<double> amounts = new List<double>();

            if (!String.IsNullOrEmpty(errorMsg))
                throw new ApplicationException("Binance error - " + errorMsg);
            else
            {
                order.Status = status;
                order.BrokerOrderId = Convert.ToString(res.orderId);
                order.Price = Convert.ToDouble(res.price);
                if (status == "FILLED")
                {
                    foreach (var fill in res.fills)
                    {
                        amounts.Add(Convert.ToDouble(fill.price));
                    }

                    order.AverageValue = amounts.Average();
                }
            }

            return order;
        }

        public override BrokerOrder MarketOrder(string orderId, string asset, OrderDirection direction, double qty, int timeOut)
        {
            return NewOrder(orderId, asset, OrderType.MARKET, direction, qty, 0, timeOut);
        }

        public override BrokerOrder LimitOrder(string orderId, string asset, OrderDirection direction, double qty, int timeOut, double price)
        {
            return NewOrder(orderId, asset, OrderType.LIMIT, direction, qty, price, timeOut);
        }

        public override BrokerOrder OrderStatus(string orderId, string asset, int timeOut)
        {
            string queryString = String.Format(
                _orderStatusQueryStringTemplate,
                asset, orderId
            );

            var res = Get(_MarketOrderUri, queryString, timeOut);

            BinanceBrokerOrder order = new BinanceBrokerOrder(this, orderId, asset);

            string errorMsg = null;
            string status = null;
            if (res.status != null)
                status = Convert.ToString(res.status);

            if (res.msg != null)
            {
                errorMsg = Convert.ToString(res.msg);
                order.InError = true;
                order.ErrorMsg = errorMsg;
                order.ErrorCode = Convert.ToString(res.code);
            }
            else
            {
                order.Status = status;
                order.BrokerOrderId = Convert.ToString(res.orderId);
                order.Price = Convert.ToDouble(res.price);
                order.AverageValue = Convert.ToDouble(res.price);                
            }


            return order;
        }

        public override async Task<BrokerOrder> SmartOrderAsync(string orderId, string asset, OrderDirection direction, double qty, int timeOut, double price, PriceBias bias)
        {
            BrokerOrder smartOrder = new BinanceBrokerOrder(this, orderId, asset);

            var task = Task<BrokerOrder>.Run(() =>
            {
                if (bias == PriceBias.Urgent)
                {
                    smartOrder = MarketOrder(orderId, asset, direction, qty, timeOut);
                    if (smartOrder.InError)
                        throw new BrokerException("Error on LimitOrder first step - " + smartOrder.ErrorMsg, null);
                }
                else
                {
                    double newPrice = price;
                    if (bias == PriceBias.Optmistic)
                    {
                        var factor = (direction == OrderDirection.BUY ? BIAS_DIFF_FORBUY : BIAS_DIFF_FORSELL);
                        newPrice *= factor;
                    }

                    var lastOrder = LimitOrder(orderId, asset, direction, qty, timeOut, newPrice);
                    if (lastOrder.InError)
                        throw new BrokerException("Error on LimitOrder first step - " + lastOrder.ErrorMsg, null);
                    else
                    {
                        bool status = false;
                        DateTime startWaiting = DateTime.Now;

                        while (!status && (DateTime.Now - startWaiting).TotalMilliseconds < timeOut)
                        {
                            Thread.Sleep(100);

                            BrokerOrder statusOrder = null;

                            try
                            {
                                statusOrder = OrderStatus(orderId, asset, timeOut);
                            }
                            catch (Exception err)
                            {
                                TraceAndLog.StaticLog("Order", "Error in the status order, it will not be propagated: "+ err.Message);
                            }

                            if (statusOrder != null)
                            {
                                if (!statusOrder.InError && statusOrder.Status == "FILLED")
                                {
                                    lastOrder = statusOrder;
                                    //Uiippiiii LIMIT ORDER is OK
                                    status = true;
                                }
                            }
                        }

                        //If the LIMIT ORDER hasn't pan out and we are selling, send market order
                        if (!status)
                        {

                            TraceAndLog.StaticLog("Broker","Cancelling all orders for -" + asset);
                            //Cancel the prevous limit order
                            CancelAllOpenOrders(asset, timeOut);

                            //If we were trying to sell desperately send a market order
                            if (direction == OrderDirection.SELL)
                            {
                                TraceAndLog.StaticLog("Broker","Sending market order -" + asset);
                                lastOrder = MarketOrder(orderId + "u", asset, direction, qty, timeOut);
                                smartOrder = lastOrder;
                                if (lastOrder.InError)
                                {
                                    TraceAndLog.StaticLog("Broker","Error in the market order final: "+lastOrder.ErrorMsg);
                                }
                            }
                        }
                        else
                        {
                            smartOrder = lastOrder;
                        }
                    }
                }
            });

            await task;

            return smartOrder;
        }

        internal override dynamic Post(string url, string queryString, string body, int timeOut)
        {
            var headers = new Dictionary<string, string>();

            System.DateTime beginningOfTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            var timeSpanStamp = DateTime.UtcNow - beginningOfTime;

            queryString += "&timestamp=" + Convert.ToInt64(timeSpanStamp.TotalMilliseconds).ToString();
            queryString += "&recvWindow=20000";

            string signature = Midas.Core.Util.HmacSHA256Helper.HashHMACHex(_apiSecret, queryString);
            headers.Add("X-MBX-APIKEY", _apiKey);
            headers.Add("Host", _host);
            headers.Add("User-Agent", "CandlesFaces");

            queryString += "&signature=" + signature;
            return base.Post(url, queryString, body, headers, timeOut);
        }

        internal override dynamic Delete(string url, string queryString, Dictionary<string, string> headers, int timeOut)
        {
            return Delete(url, queryString, timeOut);
        }

        internal override dynamic Delete(string url, string queryString, int timeOut)
        {
            var headers = new Dictionary<string, string>();

            System.DateTime beginningOfTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            var timeSpanStamp = DateTime.UtcNow - beginningOfTime;

            queryString += "&timestamp=" + Convert.ToInt64(timeSpanStamp.TotalMilliseconds).ToString();
            queryString += "&recvWindow=20000";

            string signature = Midas.Core.Util.HmacSHA256Helper.HashHMACHex(_apiSecret, queryString);
            headers.Add("X-MBX-APIKEY", _apiKey);
            headers.Add("Host", _host);
            headers.Add("User-Agent", "CandlesFaces");

            queryString += "&signature=" + signature;
            return base.Delete(url, queryString, headers, timeOut);
        }

        internal override dynamic Get(string url, string queryString, int timeOut, bool secure = true)
        {
            var headers = new Dictionary<string, string>();

            System.DateTime beginningOfTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            var timeSpanStamp = DateTime.UtcNow - beginningOfTime;

            string separator = "";
            if(!String.IsNullOrEmpty(queryString))
                separator = "&";

            string addString = String.Empty;
            if (secure)
            {
                addString += "timestamp=" + Convert.ToInt64(timeSpanStamp.TotalMilliseconds).ToString();
                addString += "&recvWindow=20000";
            }

            if (secure)
                headers.Add("X-MBX-APIKEY", _apiKey);

            headers.Add("Host", _host);
            headers.Add("User-Agent", "CandlesFaces");

            var finalQueryString = queryString;
            if(addString.Length > 0)
                finalQueryString += separator + addString;

            if (secure)
            {
                string signature = Midas.Core.Util.HmacSHA256Helper.HashHMACHex(_apiSecret, finalQueryString);
                finalQueryString += "&signature=" + signature;
            }

            return base.Get(url, finalQueryString, headers, timeOut);
        }

        public override List<BalanceRecord> AccountBalance(int timeOut)
        {
            var res = Get(this._accountUri, String.Empty, timeOut, true);
            var balances = new List<BalanceRecord>();

            string errorMsg = null;
            string errorCode = null;

            if (res.msg != null)
            {
                errorMsg = Convert.ToString(res.msg);
                errorCode = Convert.ToString(res.code);

                throw new ApplicationException($"Error getting account balance - {errorCode}:{errorMsg}");
            }
            else
            {
                foreach(var balance in res.balances)
                {
                    var record = new BalanceRecord();
                    record.Asset = Convert.ToString(balance.asset);
                    record.TotalQuantity = Convert.ToDouble(balance.free);
                    record.FreeQuantity = Convert.ToDouble(balance.locked);

                    balances.Add(record);
                }
            }

            return balances;

        }
    }

    public class TestBroker : Broker
    {
        public override List<BalanceRecord> AccountBalance(int timeOut)
        {
            throw new NotImplementedException();
        }

        public override void CancelAllOpenOrders(string asset, int timeOut)
        {
            throw new NotImplementedException();
        }

        public override void CancelAllOpenOrdersAsync(string asset, int timeOut)
        {
            throw new NotImplementedException();
        }

        public override bool CancelOrder(string orderId, string asset, int timeOut)
        {
            throw new NotImplementedException();
        }

        public override double GetPriceQuote(string asset)
        {
            throw new NotImplementedException();
        }

        public override BrokerOrder LimitOrder(string orderId, string asset, OrderDirection direction, double qty, int timeOut, double price)
        {
<<<<<<< Updated upstream
            var order = new BinanceBrokerOrder(this, direction, OrderType.LIMIT, orderId, asset);
            order.Price = price;
            order.AverageValue = price;
            order.Status = "FILLED";
            order.InError = true;
=======
            BrokerOrder order = null;
            var currentPrice = (_lastCandle == null ? 0 : _lastCandle.AmountValue);
            if(currentPrice == 0)
                throw new ArgumentException("Wait for the first Candle!");
            else
            {
                order = new BinanceBrokerOrder(this, direction, OrderType.LIMIT, orderId, asset);
                order.Price = price;
                order.AverageValue = price;
                order.Status = "NEW";
                order.InError = false;

                _pendingOrders.Add(orderId, order);
                base.LogMessage("Test Broker", "Limit Order - "+price+" - "+direction);
            }
>>>>>>> Stashed changes

            return order;
        }

        public override BrokerOrder MarketOrder(string orderId, string asset, OrderDirection direction, double qty, int timeOut)
        {
            var order = new BinanceBrokerOrder(this, direction, OrderType.MARKET, orderId, asset);
            order.Price = 30000;
            order.AverageValue = 30000;
            order.Status = "FILLED";
            order.InError = true;
            order.ErrorMsg = "This is the test broker!";

            return order;
        }

        public override BrokerOrder OrderStatus(string orderId, string asset, int timeOut)
        {
            var order = new BinanceBrokerOrder(this, orderId, asset);
            order.Price = 30000;
            order.AverageValue = 30000;
            order.Status = "FILLED";
            order.InError = true;

            return order;
        }

        public override void SetParameters(dynamic config)
        {

        }

        public override async Task<BrokerOrder> SmartOrderAsync(string orderId, string asset, OrderDirection direction, double qty, int timeOut, double price, PriceBias bias)
        {
            var order = new BinanceBrokerOrder(this, orderId, asset);
            order.Price = price;
            order.AverageValue = price;
            order.Status = "FILLED";
            order.InError = false;

            if(direction == OrderDirection.SELL)
                order.InError = true;

            Random r = new Random();
            var t = Task<BrokerOrder>.Run(() =>
            {
                Thread.Sleep(r.Next(20, 40) * 1000);
            });

            await t;

            return order;
        }

    }

    public class BalanceRecord
    {
        public string Asset { get; internal set; }
        public double TotalQuantity { get; internal set; }
        public double FreeQuantity { get; internal set; }

        public double TotalUSDAmount {get; set;}
    }

    public enum OrderDirection
    {
        BUY,
        SELL,
        NONE
    }

    public enum OrderType
    {
        LIMIT,
        MARKET,

        NONE
    }

    public enum PriceBias
    {
        Urgent,
        Optmistic,
        Normal
    }
}