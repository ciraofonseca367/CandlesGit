using System;
using System.Collections.Generic;
using System.Threading;
using Midas.Core;
using Midas.Core.Common;
using Midas.FeedStream;
using System.Linq;
using Midas.Util;
using MongoDB.Driver;
using MongoDB.Bson;
using Midas.Core.Chart;
using System.Drawing;
using System.IO;
using Midas.Core.Encoder;
using Midas.Core.Indicators;
using System.Threading.Tasks;
using Midas.Trading;
using Midas.Core.Telegram;
using Midas.Core.Broker;
using Midas.Core.Util;
using Telegram.Bot;
using Telegram.Bot.Extensions.Polling;
using System.Text;
using Midas.Core.Trade;
using DateTimeUtil;

namespace Midas.Core.Services
{
    public class InvestorService
    {
        private Thread _runner;
        private bool _running;
        private RunParameters _params;

        private MongoClient _mongoClient;

        private CandleBot _candleBot;

        private Dictionary<string, AssetTrader> _traders;

        public InvestorService(RunParameters parans)
        {
            TelegramBot.SetApiCode(parans.TelegramBotCode);

            _params = parans;
            _runner = new Thread(new ThreadStart(this.Runner));

            _mongoClient = new MongoClient(parans.DbConString);

        }

        public string GetAssetsConfig()
        {
            StringBuilder configs = new StringBuilder();

            foreach (var pair in _traders)
            {
                configs.Append(pair.Value.GetParametersReport());
                configs.AppendLine();
                configs.AppendLine();
            }

            return configs.ToString();
        }

        public void SendMessage(string thread, string message)
        {
            if (thread == null)
                TelegramBot.SendMessage(message);
            else
                TelegramBot.SendMessageBuffered(thread, message);
        }

        public AssetTrader GetAssetTrader(string asset)
        {
            AssetTrader trader = null;

            _traders.TryGetValue(asset, out trader);

            return trader;
        }

        public void Start()
        {
            _running = true;

            LoadTraders();

            Console.WriteLine("Starting with traders:");
            foreach(var t in _traders)
                Console.WriteLine(t.ToString());

            this._runner.Start();

            //Set up the Bot
            _candleBot = new CandleBot(this, _params, _traders);
            _candleBot.Start();

            TelegramBot.SendMessage("Iniciando Investor...");
        }



        internal string GetAllReport()
        {
            StringBuilder sb = new StringBuilder();
            List<double> days = new List<double>();
            List<double> weeks = new List<double>();
            List<double> months = new List<double>();

            if (_traders.Count() > 0)
            {
                sb.Append("<code>");

                sb.Append(String.Format("ASSET    D     W      M   \n"));
                sb.Append(String.Format("---------------------------\n"));
                foreach (var pair in _traders)
                {
                    var allOps = SearchOperations(pair.Value.Asset, pair.Value.CandleType, DateTime.UtcNow.AddDays(-30));
                    var lastDay = allOps.Where(op => op.EntryDate > DateTime.Now.AddDays(-1));
                    var lastWeek = allOps.Where(op => op.EntryDate > DateTime.Now.AddDays(-7));

                    var resultAll = allOps.Sum(op => (op.PriceExitReal - op.PriceEntryReal) / op.PriceEntryReal);
                    resultAll *= 100;
                    var resultWeek = lastWeek.Sum(op => (op.PriceExitReal - op.PriceEntryReal) / op.PriceEntryReal);
                    resultWeek *= 100;
                    var resultDay = lastDay.Sum(op => (op.PriceExitReal - op.PriceEntryReal) / op.PriceEntryReal);
                    resultDay *= 100;

                    days.Add(resultDay);
                    weeks.Add(resultWeek);
                    months.Add(resultAll);

                    sb.Append(String.Format("{0,5}{1,6:0.00}%{2,6:0.00}%{3,6:0.00}%",
                        pair.Value.GetShortIdentifier(), resultDay, resultWeek, resultAll));
                    sb.AppendLine();
                }

                sb.Append(String.Format("---------------------------\n"));
                sb.Append(String.Format("{0,5}{1,6:0.00}%{2,6:0.00}%{3,6:0.00}%",
                String.Empty, days.Average(), weeks.Average(), months.Average()
                ));
                sb.Append("</code>");
            }
            else
                sb.Append("NO TRANDERS ON");


            return sb.ToString();
        }

        internal string GetReport(string asset, CandleType candleType)
        {
            var ops = SearchOperations(asset, candleType, DateTime.Now.AddDays(-7));

            var lastDayOps = ops.Where(op => op.EntryDate > DateTime.Now.AddDays(-1));

            var resultLastDay = lastDayOps.Sum(op => (op.PriceExitReal - op.PriceEntryReal) / op.PriceEntryReal);
            resultLastDay *= 100;
            var taxesLastDay = lastDayOps.Count() * -0.0006;
            var resultWeek = ops.Sum(op => (op.PriceExitReal - op.PriceEntryReal) / op.PriceEntryReal);
            resultWeek *= 100;
            var taxesLastWeek = ops.Count() * -0.0006;

            string message;

            message = $"Last 24 hrs P&L\n{resultLastDay.ToString("0.000")}% + {taxesLastDay.ToString("0.000")}% = {(resultLastDay + taxesLastDay).ToString("0.000")}% em {lastDayOps.Count()} entradas\n\n";
            message += $"Last 7 days P&L\n{resultWeek.ToString("0.000")}% + {taxesLastWeek.ToString("0.000")}% = {(resultWeek + taxesLastWeek).ToString("0.000")}% em {ops.Count()} entradas\n";

            return message;
        }

        public List<TradeOperationDto> SearchOperations(string asset, CandleType candle, DateTime min)
        {

            return InvestorService.SearchOperations(_params.DbConString, null, asset, candle, min);
        }

        public static List<TradeOperationDto> SearchOperations(string conString, string experiment, string asset, CandleType candle, DateTime min)
        {
            return SearchOperations(conString,experiment,asset,candle,min, DateTime.MinValue, TradeOperationState.None);
        }


        public static List<TradeOperationDto> SearchOperations(string conString, string experiment, string asset, CandleType candle, DateTime min,
            DateTime lastUpdateMin, TradeOperationState state, bool noPriceExit=true)
        {
            var client = new MongoClient(conString);
            var database = client.GetDatabase("CandlesFaces");
            var dbCol = database.GetCollection<TradeOperationDto>("TradeOperations");

            var filterBuilder1 = Builders<TradeOperationDto>.Filter;
            var filterDefinition = new List<FilterDefinition<TradeOperationDto>>();
            filterDefinition.Add(filterBuilder1.Gte(item => item.EntryDate, min));
            
            if(noPriceExit)
                filterDefinition.Add(filterBuilder1.Ne(item => item.PriceExitReal, 0));

            if (experiment != null)
                filterDefinition.Add(filterBuilder1.Eq(item => item.Experiment, experiment));
            if (asset != null)
                filterDefinition.Add(filterBuilder1.Eq(item => item.Asset, asset));
            if (candle != CandleType.None)
                filterDefinition.Add(filterBuilder1.Eq(item => item.CandleType, candle));
            if (lastUpdateMin != DateTime.MinValue)
                filterDefinition.Add(filterBuilder1.Gt(item => item.LastUpdate, lastUpdateMin));
            if (state != TradeOperationState.None)
                filterDefinition.Add(filterBuilder1.Eq(item => item.State, state));

            var fieldsBuilder = Builders<TradeOperationDto>.Projection;
            var fields = fieldsBuilder.Exclude(o => o.Logs);

            var filter = filterBuilder1.And(filterDefinition.ToArray());

            var query = dbCol.Find(filter).Project<TradeOperationDto>(fields).ToList();

            return query;
        }

        public static List<OperationsSummary> SummariseResult(List<TradeOperationDto> allOperations)
        {
            var summary = allOperations
            .GroupBy(op => op.Asset + ":" + op.CandleType)
            .Select(group => new OperationsSummary
            {
                Asset = group.Max(i => i.Asset),
                CandleType = group.Max(i => i.CandleType),
                OperationsCount = group.Count(),
                PAndL = group.Sum(op => ((op.PriceExitReal - op.PriceEntryReal) / op.PriceEntryReal) * 100),
                OperationAvg = group.Sum(i => i.Gain)  / Convert.ToDouble(group.Count()),
                SuccessRate = Convert.ToDouble(group.Count(i => i.State == TradeOperationState.Profit)) /
                       Convert.ToDouble(group.Count())
            });

            double successOperationCost = 0.06;
            double failedOperationCost = 0.13;
            foreach(var s in summary)
            {
                var pairOpCost = ((s.SuccessRate * successOperationCost) + ((100-s.SuccessRate) * failedOperationCost))/100;
                s.OperationsCost = pairOpCost*s.OperationsCount;
                s.PAndL_AfterCosts = s.PAndL - s.OperationsCost;
            }

            return summary.ToList();
        }
 
        public string GetOperationsSummary(int days)
        {
            var allOperations = SearchOperations(this._params.DbConString, null, null, CandleType.MIN15, DateTime.UtcNow.AddDays(days * -1), DateTime.MinValue, TradeOperationState.None);
            var allOperationsReverse = allOperations.OrderByDescending(op => op.EntryDate).ToList();
            StringBuilder sb = new StringBuilder(500);

            var summary = SummariseResult(allOperations);

            int allCount = 0;
            double allAvgPAndL = 0, allAvgPerTrans = 0, allRate = 0;

            sb.Append("<code>");

            sb.Append(String.Format("{0,6}{1,5}{2,7}{3,6}{4,4}\n", "ASSET", "E", "P&L", "Avg", "R"));
            sb.Append(String.Format("-------------------------------\n"));
            summary.ForEach(s =>
            {
                sb.Append(String.Format("{0,6}{1,5}{2,7:0.000}%{3,6:0.00}%{4,4:00%}\n",
                    AssetTrader.GetShortIdentifier(s.Asset, s.CandleType),
                    s.OperationsCount,
                    s.PAndL_AfterCosts,
                    s.OperationAvg,
                    s.SuccessRate
                ));
            });

            allCount = summary.Sum(s => s.OperationsCount);
            allAvgPAndL = summary.Average(s => s.PAndL_AfterCosts);
            allAvgPerTrans = summary.Average(s => s.OperationAvg);
            allRate = summary.Average(s => s.SuccessRate);
            sb.Append(String.Format("-------------------------------\n"));
            sb.Append(String.Format("{0,6}{1,5}{2,7:0.000}%{3,6:0.00}%{4,4:00%}\n", "", allCount, allAvgPAndL, allAvgPerTrans, allRate));

            sb.Append("</code>");

            return sb.ToString();
        }

        public string GetLastOperations(int number)
        {
            var allOperations = SearchOperations(this._params.DbConString, _params.ExperimentName, null, CandleType.None, DateTime.UtcNow.AddDays(100*-1));
            var allOperationsReverse = allOperations.OrderByDescending(op => op.ExitDate).ToList();

            StringBuilder sb = new StringBuilder(500);
            double successRate = 0;

            double total = 0;
            double success = 0;
            allOperationsReverse.Take(number).ToList().ForEach(op =>
            {
                var emoji = TelegramEmojis.PERSON_SHRUGGING;
                if(op.State == TradeOperationState.Profit || op.State == TradeOperationState.Stopped)
                    emoji = op.Gain < 0 ? TelegramEmojis.RedX : TelegramEmojis.GreenCheck;

                total++;
                if(op.Gain > 0)
                    success++;

                var duration = (op.ExitDate - op.EntryDate);

                sb.Append($"{op.ExitDate:MMMdd HH:mm} <b>{op.Asset}:{op.CandleType.ToString()} {emoji} {op.Gain:0.000}%</b>\n");
                sb.Append($"IN: {op.PriceEntryReal:0.00} OUT: {op.PriceExitReal:0.00}\n");
                sb.Append($"{TimeSpanPlus.ToString(duration)}\n\n");
            });

            if(total == 0)
                sb.Append("No records in the last 10 days");
            else
            {
                successRate = (success / total) * 100;
            }

            var header = $"Sucsess Rate: <b>{successRate:0.00}%</b>\n\n";

            return header + sb.ToString();
        }


        public static List<TradeOperationDto> GetOperationToRestore(string conString, string asset, CandleType candle, string experiment, DateTime relativeNow)
        {
            var client = new MongoClient(conString);
            var database = client.GetDatabase("CandlesFaces");
            var dbCol = database.GetCollection<TradeOperationDto>("TradeOperations");

            var filterBuilder1 = Builders<TradeOperationDto>.Filter;
            var filterDefinition = new List<FilterDefinition<TradeOperationDto>>();
            filterDefinition.Add(filterBuilder1.Gte(item => item.LastUpdate, relativeNow.AddHours(-1)));
            filterDefinition.Add(filterBuilder1.Eq(item => item.State, TradeOperationState.In));
            if (asset != null)
                filterDefinition.Add(filterBuilder1.Eq(item => item.Asset, asset));
            if (candle != CandleType.None)
                filterDefinition.Add(filterBuilder1.Eq(item => item.CandleType, candle));
            if (experiment != null)
                filterDefinition.Add(filterBuilder1.Eq(item => item.Experiment, experiment));

            var filter = filterBuilder1.And(filterDefinition.ToArray());

            var query = dbCol.Find(filter).ToList();

            return query.ToList();
        }


        public static List<TradeOperationDto> SearchActiveOperations(string conString, string asset, CandleType candle, string experiment, DateTime relativeNow)
        {
            var client = new MongoClient(conString);
            var database = client.GetDatabase("CandlesFaces");
            var dbCol = database.GetCollection<TradeOperationDto>("TradeOperations");

            var filterBuilder1 = Builders<TradeOperationDto>.Filter;
            var filterDefinition = new List<FilterDefinition<TradeOperationDto>>();
            filterDefinition.Add(filterBuilder1.Gte(item => item.EntryDate, relativeNow.AddHours(-14)));
            if (asset != null)
                filterDefinition.Add(filterBuilder1.Eq(item => item.Asset, asset));
            if (candle != CandleType.None)
                filterDefinition.Add(filterBuilder1.Eq(item => item.CandleType, candle));
            if (experiment != null)
                filterDefinition.Add(filterBuilder1.Eq(item => item.Experiment, experiment));

            var filter = filterBuilder1.And(filterDefinition.ToArray());

            var query = dbCol.Find(filter).ToList();

            return query.ToList();
        }


        public bool Running
        {
            get
            {
                return _running;
            }
            set
            {
                _running = value;
            }
        }
        public string GetAllTradersStatus()
        {
            StringBuilder sb = new StringBuilder();

            foreach (var pair in _traders)
            {
                sb.Append(pair.Value.GetState());
                sb.AppendLine();
                sb.AppendLine();
            }

            return sb.ToString();
        }


        public string GetBalanceReport()
        {
            BinanceBroker b = new BinanceBroker();
            b.SetParameters(_params.BrokerParameters);
            var priceBTC = b.GetPriceQuote("BTCBUSD");
            var priceBNB = b.GetPriceQuote("BNBBUSD");
            var priceADA = b.GetPriceQuote("ADABUSD");
            var priceETH = b.GetPriceQuote("ETHBUSD");

            string emoticon = "\U00002705";
            string balanceReport = "BALANCE REPORT " + emoticon + "\n\n";

            var balances = b.AccountBalance(60000);
            balances.ForEach(b =>
            {
                if (b.TotalQuantity > 0.0001)
                {
                    if (b.Asset == "BTC")
                        b.TotalUSDAmount = b.TotalQuantity * priceBTC;
                    else if (b.Asset == "USDT")
                        b.TotalUSDAmount = b.TotalQuantity;
                    else if (b.Asset == "BUSD")
                        b.TotalUSDAmount = b.TotalQuantity;
                    else if (b.Asset == "BNB")
                        b.TotalUSDAmount = b.TotalQuantity * priceBNB;
                    else if (b.Asset == "ADA")
                        b.TotalUSDAmount = b.TotalQuantity * priceADA;
                    else if (b.Asset == "ETH")
                        b.TotalUSDAmount = b.TotalQuantity * priceETH;

                    balanceReport += String.Format("{0}: {1:0.0000} = ${2:0.00}\n", b.Asset, b.TotalQuantity, b.TotalUSDAmount);
                }
            });

            double inWallet = balances.Sum(b => b.TotalUSDAmount);
            balanceReport += "\n";
            balanceReport += $"Total in Wallet: ${inWallet:0.00}";

            double inOrderAmount = 0;
            foreach(var traderPair in _traders)
            {

                var openOrders = b.OpenOrders(traderPair.Value.Asset, 20000);
                foreach(var order in openOrders)
                {
                    double price = 0;
                    if(traderPair.Value.Asset == "BTCBUSD" || traderPair.Value.Asset == "BTCUSDT")
                        price = priceBTC;
                    else if(traderPair.Value.Asset == "ETHBUSD")
                        price = priceETH;
                    else if(traderPair.Value.Asset == "BNBBUSD")
                        price = priceBNB;
                    else if(traderPair.Value.Asset == "ADABUSD")
                        price = priceADA;

                    inOrderAmount += order.Quantity * price;
                }
            }

            balanceReport += "\n";
            balanceReport += $"Total in Orders: ${inOrderAmount:0.00}";

            balanceReport += "\n";
            balanceReport += $"Grand Total: <b>${(inOrderAmount+inWallet):0.00}</b>";

            return balanceReport;
        }

        public double GetAccountBalance()
        {
            BinanceBroker b = new BinanceBroker();
            b.SetParameters(_params.BrokerParameters);
            var priceBTC = b.GetPriceQuote("BTCBUSD");
            var priceBNB = b.GetPriceQuote("BNBBUSD");
            var priceADA = b.GetPriceQuote("ADABUSD");
            var priceETH = b.GetPriceQuote("ETHBUSD");

            var balances = b.AccountBalance(60000);
            balances.ForEach(b =>
            {
                if (b.TotalQuantity > 0.0001)
                {
                    if (b.Asset == "BTC")
                        b.TotalUSDAmount = b.TotalQuantity * priceBTC;
                    else if (b.Asset == "USDT")
                        b.TotalUSDAmount = b.TotalQuantity;
                    else if (b.Asset == "BUSD")
                        b.TotalUSDAmount = b.TotalQuantity;
                    else if (b.Asset == "ADA")
                        b.TotalUSDAmount = b.TotalQuantity * priceADA;
                    else if (b.Asset == "ETH")
                        b.TotalUSDAmount = b.TotalQuantity * priceETH;
                    else if (b.Asset == "BNB")
                        b.TotalUSDAmount = b.TotalQuantity * priceBNB;
                }
            });

            double inOrderAmount = 0;
            foreach(var traderPair in _traders)
            {

                var openOrders = b.OpenOrders(traderPair.Value.Asset, 20000);
                foreach(var order in openOrders)
                {
                    double price = 0;
                    if(traderPair.Value.Asset == "BTCBUSD" || traderPair.Value.Asset == "BTCUSDT")
                        price = priceBTC;
                    else if(traderPair.Value.Asset == "ETHBUSD")
                        price = priceETH;
                    else if(traderPair.Value.Asset == "BNBBUSD")
                        price = priceBNB;
                    else if(traderPair.Value.Asset == "ADABUSD")
                        price = priceADA;

                    inOrderAmount += order.Quantity * price;
                }
            }


            return balances.Sum(b => b.TotalUSDAmount) + inOrderAmount;
        }

        public void Stop()
        {
            _running = false;

            _runner.Join();

            if (!_runner.Join(1000))
                throw new ApplicationException("Timeout waiting for the runner to stop");
        }

        private void DisposeResources()
        {
            Console.WriteLine("Saindo...");

            StopTraders(false);

            _candleBot.Stop();

            TraceAndLog.GetInstance().Dispose();
        }

        private void StartTraders()
        {
            foreach (var pair in _traders)
                pair.Value.Start();
        }

        public void StopTraders(bool stopOp = true)
        {
            foreach (var pair in _traders)
                pair.Value.Stop(stopOp);

            _traders = null;
        }

        public void RestartTraders()
        {
            if (_traders != null)
            {
                foreach (var pair in _traders)
                    pair.Value.Stop();
            }

            _traders = _params.GetAssetTraders(this);

            foreach (var pair in _traders)
                pair.Value.Start();
        }

        private void LoadTraders()
        {
            _traders = _params.GetAssetTraders(this);
        }

        public void Runner()
        {
            StartTraders();

            while (_running)
            {
                Thread.Sleep(1000*60);

                try
                {
                    if(DateTime.UtcNow.Hour == 0)
                    {
                        double balance = GetAccountBalance();
                        var br = new BalanceReport(balance);
                        br.SaveOrUpdate(_params.DbConString);

                        TelegramBot.SendMessage($"Balance for the {DateTime.Now:yyyy-MM-dd} is $ {balance:0.000}");
                        TraceAndLog.StaticLog("Investor","Daily balance updated");
                    }
                }
                catch(Exception err)
                {
                    TraceAndLog.StaticLog("Investor","Error updating daily balance - "+err.ToString());
                }
            }

            DisposeResources();
        }
    }

    public class OperationsSummary
    {
        private int _operationsCount;
        private double _pAndL;

        private double _operationAvg;
        private double _sucessRate;
        private string _asset;

        public int OperationsCount { get => _operationsCount; set => _operationsCount = value; }
        public double PAndL { get => _pAndL; set => _pAndL = value; }
        public double OperationAvg { get => _operationAvg; set => _operationAvg = value; }
        public double SuccessRate { get => _sucessRate; set => _sucessRate = value; }
        public string Asset { get => _asset; set => _asset = value; }
        public CandleType CandleType { get; set; }
        public double OperationsCost { get; internal set; }
        public double PAndL_AfterCosts { get; internal set; }
    }

    public class BalanceReport
    {
        public double Balance { get; set; }
        public DateTime Date { get; set; }
        private string _dateKey;

        public BalanceReport(double balance)
        {
            Date = DateTime.UtcNow;
            _dateKey = Date.ToString("yyyy-MM-dd HH");
            Balance = balance;
        }

        public string DateKey
        {
            get
            {
                return _dateKey;
            }
            set
            {
                _dateKey = value;
            }
        }

        public void SaveOrUpdate(string conString)
        {
            var mongoClient = new MongoClient(conString);

            var database = mongoClient.GetDatabase("CandlesFaces");

            var dbCol = database.GetCollection<BalanceReport>("DailyBalances");

            var result = dbCol.ReplaceOne(
                item => item.DateKey == _dateKey,
                this,
                new ReplaceOptions { IsUpsert = true });
        }

    }

}