// using System;
// using System.Collections.Generic;
// using Midas.Core.Common;
// using Midas.Util;
// using System.Collections;
// using System.Linq;
// using System.Drawing;
// using Midas.Core.Chart;

// namespace Midas.Core.Indicators
// {
//     public class PredictionIndicator : CalculatedIndicator
//     {
//         private Dictionary<string, FixedSizedQueue<Candle>> _queuesMap;

//         public PredictionIndicator(string bufferSize, string windowSize) : this(new object[] { bufferSize, windowSize })
//         {
//         }

//         public PredictionIndicator(object[] args) : base(args)
//         {
//             _queuesMap = new Dictionary<string, FixedSizedQueue<Candle>>();
//         }

//         private IEnumerable<Candle> ExpandCandles(double lower, double upper, DateRange range)
//         {
//             var periods = Math.Round(range.GetSpan().TotalMinutes / 5);

//             for (int i = 0; i < periods; i++)
//             {
//                 yield return new Candle()
//                 {
//                     OpenValue = lower,
//                     CloseValue = upper,
//                     LowestValue = lower,
//                     HighestValue = upper,
//                     PointInTime_Open = range.Start.AddMinutes(i * 5),
//                     PointInTime_Close = range.Start.AddMinutes((i * 5) + 5),
//                     Volume = 0
//                 };
//             }
//         }

//         private FixedSizedQueue<Candle> GetTagQueue(string identifier)
//         {
//             FixedSizedQueue<Candle> queue = null;
//             _queuesMap.TryGetValue(identifier, out queue);
//             if (queue == null)
//             {
//                 queue = new FixedSizedQueue<Candle>(_windowSize);
//                 _queuesMap.Add(identifier, queue);
//             }

//             return queue;
//         }

//         private bool DateEqual(DateTime date1, DateTime date2)
//         {
//             date1 = new DateTime(date1.Year, date1.Month, date1.Day, date1.Hour, date1.Minute, 0);
//             date2 = new DateTime(date2.Year, date2.Month, date2.Day, date2.Hour, date2.Minute, 0);

//             return date1 == date2;
//         }

//         public override void AddFramePoint(IStockPointInTime point)
//         {
//             throw new NotImplementedException();
//         }

//         public override IEnumerable<IStockPointInTime> TakeSnapShot()
//         {
//             var snapShot = new List<IStockPointInTime>();

//             foreach (var entry in _queuesMap)
//             {
//                 snapShot.AddRange(entry.Value.GetList());
//             }

//             return snapShot;
//         }

//         public override void AddIdentifedFramePoint(IStockPointInTime predictionPoint, string identifier)
//         {
//             //Here we get can get a Candle with a large span of periods, we need to expeand it into 
//             //The corresponding number of candles or just one candle if it is a empty candle.
//             var expanpedCandles = this.ExpandCandles(
//                 predictionPoint.OpenValue,
//                 predictionPoint.CloseValue,
//                 new DateRange(
//                     predictionPoint.PointInTime_Open,
//                     predictionPoint.PointInTime_Close
//                 )
//             );

//             var queues = new List<FixedSizedQueue<Candle>>();
//             if (identifier != "All")
//             {
//                 var queue = GetTagQueue(identifier);
//                 queues.Add(queue);
//             }
//             else
//             {
//                 queues = _queuesMap.Values.ToList();
//             }

//             foreach(var queue in queues)
//             {
//                 foreach (var c in expanpedCandles)
//                 {
//                     var currentState = queue.GetList();
//                     var existing = currentState.Where(stateCandle =>
//                     (
//                         DateEqual(stateCandle.OpenTime, c.OpenTime)
//                     ));

//                     if (existing.Count() == 0)
//                         queue.Enqueue(c);
//                 }
//             }
//         }
//     }
// }