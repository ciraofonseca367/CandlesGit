using System;
using Midas.Core.Binance;
using Midas.Core.Common;

namespace Midas.Core.Trade
{
    public class MatchMaker : IDisposable
    {
        private string _asset;
        private BinanceBookWebSocket _bookSocket; 

        private BookView _lastView;

        private static string BINANCE_URI = "wss://stream.binance.com:9443/ws";

        public MatchMaker(string asset)
        {
            this._asset = asset;
        }

        public void Start()
        {
            _bookSocket = new BinanceBookWebSocket(BINANCE_URI, 20000, _asset, 5);
            _bookSocket.Open();

            _bookSocket.OnNewBookView(this.OnNewBookEvent);
        }

        private void OnNewBookEvent(BookView view)
        {
            _lastView = view;
        }

        public double GetPurchasePrice(double qty)
        {
            double price = 0;

            if(_lastView != null)
                price = MatchMaker.GetPrice(_lastView, qty, OfferType.Ask);
            else
                price = 0;

            return price;
        }

        public double GetSellingPrice(double qty)
        {
            return MatchMaker.GetPrice(_lastView, qty, OfferType.Bid);
        }        

        public static double GetPrice(BookView view, double qty, OfferType offerType)
        {
            double price=0;
            if(offerType == OfferType.Ask)
            {
                for(int i = view.Asks.Count - 1; i > 0; i--)
                {
                    var a = view.Asks[i];
                    if(a.Qty >= qty)
                    {
                        price = a.Price;
                        break;
                    }
                }
            }
            else
            {
              for(int i = 0; i < view.Bids.Count - 1; i++)
                {
                    var a = view.Bids[i];
                    if(a.Qty >= qty)
                    {
                        price = a.Price;
                        break;
                    }
                }
            }

            return price;
        }

        public void Dispose()
        {
            if(_bookSocket != null)
                _bookSocket.Dispose();
        }
    }
}