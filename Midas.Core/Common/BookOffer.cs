using System;
using System.Collections.Generic;

namespace Midas.Core.Common
{
    public class BookView
    {
        public BookView()
        {
            Bids = new List<BookOffer>();
            Asks = new List<BookOffer>();
        }
        public string LastUpdateId
        {
            get;set;
        }

        public List<BookOffer> Bids
        {
            get;set;
        }
        public List<BookOffer> Asks
        {
            get;set;
        }

    }

    public class BookOffer
    {
        public OfferType OfferType
        {
            get;set;
        }

        public double Qty
        {
            get;set;
        }

        public double Price
        {
            get;set;
        }        
    }

    public enum OfferType
    {
        Bid,
        Ask
    }
}