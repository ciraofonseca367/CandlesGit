using System;
using System.Drawing;
using System.IO;
using Telegram.Bot;
using Telegram.Bot.Types;
using System.Collections.Generic;
using Midas.Core.Util;

namespace Midas.Core.Telegram
{
    public class TelegramBot
    {
        private static string BOT_API = "1817976920:AAFwSV3rRDq2Cd8TGKwGRGoNhnHt4seJfU4";
        private static DateTime _last;

        private static Dictionary<string, DateTime> _buffers;

        static TelegramBot()
        {
            _last = DateTime.Now.AddSeconds(-10);
            _buffers = new Dictionary<string, DateTime>(7);
        }

        public static void SetApiCode(string code)
        {
            BOT_API = code;
        }

        public static async void SendImage(Bitmap img, string msg)
        {

            bool isTesting = false;
            if(RunParameters.GetInstance() != null)
                isTesting = RunParameters.GetInstance().IsTesting;

            if(isTesting)
                return;

            try
            {
                var botClient = new TelegramBotClient(BOT_API);

                using (MemoryStream ms = new MemoryStream())
                {
                    img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Flush();
                    ms.Position = 0;

                    var media = new InputMedia(ms, msg);

                    var t = await botClient.SendPhotoAsync("@CandlesFace", media,(isTesting ? "TESTING -" : String.Empty) + msg);

                    _last = DateTime.Now;
                }
            }
            catch (Exception err)
            {
                TraceAndLog.StaticLog("Telegram","Telegram error: " + err.Message);
            }

        }

        public static void SendImageBuffered(string threadName, Bitmap img, string msg)
        {
            if (!_buffers.ContainsKey(threadName))
                _buffers.Add(threadName, DateTime.Now.AddSeconds(-600));

            DateTime last = _buffers[threadName];
            if ((DateTime.Now - last).TotalSeconds > 30)
            {
                SendImage(img, msg);
                _buffers[threadName] = DateTime.Now;
            }
        }

        public static async void SendMessage(string msg)
        {
            bool isTesting = false;
            if(RunParameters.GetInstance() != null)
                isTesting = RunParameters.GetInstance().IsTesting;


            if(isTesting)
                return;                
            
            try
            {
                var botClient = new TelegramBotClient(BOT_API);

                var t = await botClient.SendTextMessageAsync("@CandlesFace", (isTesting ? "TESTING -" : String.Empty) + msg);

                _last = DateTime.Now;
            }
            catch (Exception err)
            {
                TraceAndLog.StaticLog("Telegram","Telegram error: " + err.Message);
            }
        }

        public static void SendMessageBuffered(string threadName, string msg)
        {
            if (!_buffers.ContainsKey(threadName))
                _buffers.Add(threadName, DateTime.Now.AddSeconds(-600));

            DateTime last = _buffers[threadName];
            if ((DateTime.Now - last).TotalSeconds > 60)
            {
                SendMessage(msg);
                _buffers[threadName] = DateTime.Now;
            }
        }
    }
}