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

            foreach(var pair in _traders)
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
            return _traders[asset];
        }

        public void Start()
        {
            _running = true;

            LoadTraders();

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

            sb.Append("<code>");

            sb.Append(String.Format("ASSET    D     W      M   \n"));
            sb.Append(String.Format("---------------------------\n"));
            foreach(var pair in _traders)
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
                    pair.Value.GetShortIdentifier(), resultDay,resultWeek,resultAll));
                sb.AppendLine();
            }
            sb.Append(String.Format("---------------------------\n"));
            sb.Append(String.Format("{0,5}{1,6:0.00}%{2,6:0.00}%{3,6:0.00}%",
            String.Empty,days.Average(),weeks.Average(),months.Average()
            ));
            sb.Append("</code>");

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

            return InvestorService.SearchOperations(_params.DbConString,asset, candle, min);
        }  

        public static List<TradeOperationDto> SearchOperations(string conString, string asset, CandleType candle, DateTime min)
        {
            var client = new MongoClient(conString);
            var database = client.GetDatabase("CandlesFaces");
            var dbCol = database.GetCollection<TradeOperationDto>("TradeOperations");

            var filterBuilder1 = Builders<TradeOperationDto>.Filter;
            var filterDefinition = new List<FilterDefinition<TradeOperationDto>>();
            filterDefinition.Add(filterBuilder1.Gte(item => item.EntryDate, min));
            filterDefinition.Add(filterBuilder1.Ne(item => item.PriceExitReal, 0));
            if(asset != null)
                filterDefinition.Add(filterBuilder1.Eq(item => item.Asset, asset));
            if(candle != CandleType.None)
                filterDefinition.Add(filterBuilder1.Eq(item => item.CandleType, candle));

            var filter = filterBuilder1.And(filterDefinition.ToArray());

            var query = dbCol.Find(filter).ToList();

            return query.ToList();
        }      


        public static List<TradeOperationDto> SearchActiveOperations(string conString, string asset, CandleType candle,string experiment, DateTime relativeNow)
        {
            var client = new MongoClient(conString);
            var database = client.GetDatabase("CandlesFaces");
            var dbCol = database.GetCollection<TradeOperationDto>("TradeOperations");

            var filterBuilder1 = Builders<TradeOperationDto>.Filter;
            var filterDefinition = new List<FilterDefinition<TradeOperationDto>>();
            filterDefinition.Add(filterBuilder1.Gte(item => item.EntryDate, relativeNow.AddDays(-2)));
            if(asset != null)
                filterDefinition.Add(filterBuilder1.Eq(item => item.Asset, asset));
            if(candle != CandleType.None)
                filterDefinition.Add(filterBuilder1.Eq(item => item.CandleType, candle));
            if(experiment != null)
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
        }
        public string GetAllTradersStatus()
        {
            StringBuilder sb = new StringBuilder();

            foreach(var pair in _traders)
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

                    balanceReport += String.Format("{0}: {1:0.0000} = ${2:0.00}\n", b.Asset, b.TotalQuantity, b.TotalUSDAmount);
                }
            });

            balanceReport += "\n";
            balanceReport += $"Total: ${balances.Sum(b => b.TotalUSDAmount).ToString("0.00")}";

            return balanceReport;
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

            foreach(var pair in _traders)
                pair.Value.Stop();

            TraceAndLog.GetInstance().Dispose();

            _candleBot.Stop();
        }

        private void LoadTraders()
        {
            _traders = _params.GetAssetTraders(this);
        }

        public void Runner()
        {
            foreach(var pair in _traders)
                pair.Value.Start();

            while (_running)
                Thread.Sleep(1000);

            DisposeResources();
        }
    }

}