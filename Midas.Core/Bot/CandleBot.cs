using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Midas.Core.Services;
using Midas.Core.Trade;
using Midas.Core.Util;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;

namespace Midas.Core.Telegram
{
    public class CandleBot
    {
        private InvestorService _myService;
        private RunParameters _params;

        private TelegramBotClient _telegramClient;

        private CancellationTokenSource _cancToken;

        private Dictionary<string, AssetTrader> _traders;

        private Dictionary<long, TelegramSession> _sessions;

        public CandleBot(InvestorService service, RunParameters @params, Dictionary<string, AssetTrader> traders)
        {
            _myService = service;
            _params = @params;
            _traders = traders;

            _sessions = new Dictionary<long, TelegramSession>(11);

            _lastShutDown = DateTime.MinValue;
        }

        private TelegramSession GetSession(long id)
        {
            TelegramSession session = null;

            _sessions.TryGetValue(id, out session);

            if (session == null)
            {
                session = new TelegramSession();
                _sessions.Add(id, session);
            }

            return session;
        }

        public void Start()
        {
            _telegramClient = new TelegramBotClient(_params.TelegramBotCode);

            var meTask = _telegramClient.GetMeAsync();
            if (meTask.Wait(20000))
            {
                var me = meTask.Result;

                _cancToken = new CancellationTokenSource();

                // StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
                _telegramClient.StartReceiving(new DefaultUpdateHandler(this.HandleUpdateAsync, this.HandleErrorAsync),
                                   _cancToken.Token);

                TraceAndLog.StaticLog("Main", $"Start listening for @{me.Username}");
            }
            else
            {
                TraceAndLog.StaticLog("Start", "Timeout error getting telegram bot ME. Bot will be off");
            }
        }

        public void Stop()
        {
            _cancToken.Cancel();
        }

        public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            TraceAndLog.StaticLog("Bot", ErrorMessage);
            return Task.CompletedTask;
        }


        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            var handler = update.Type switch
            {
                UpdateType.Message => BotOnMessageReceived(botClient, update.Message),
                UpdateType.EditedMessage => BotOnMessageReceived(botClient, update.EditedMessage),
                UpdateType.CallbackQuery => BotOnCallbackQueryReceived(botClient, update.CallbackQuery),
                _ => UnknownUpdateHandlerAsync(botClient, update)
            };

            try
            {
                await handler;
            }
            catch (Exception exception)
            {
                if (update != null && update.Message != null)
                    await botClient.SendTextMessageAsync(chatId: update.Message.Chat.Id,
                                                                text: $"General error: {exception.Message}",
                                                                replyMarkup: new ReplyKeyboardRemove(), parseMode: ParseMode.Html);

                await HandleErrorAsync(botClient, exception, cancellationToken);
            }
        }

        private DateTime _lastShutDown;

        private async Task BotOnMessageReceived(ITelegramBotClient botClient, Message message)
        {
            var command = (message.Text.Split(' ').First());

            AssetTrader currentTrader;
            object rawTrader = null;

            var session = GetSession(message.Chat.Id);
            session.Data.TryGetValue("Trader", out rawTrader);

            currentTrader = (AssetTrader)rawTrader;

            switch (message.Text)
            {
                case "hi":
                    await SendGeneralOptions(botClient, message);
                    break;
                case "Config":

                    var msg = String.Concat(_myService.GetAssetsConfig());

                    await botClient.SendTextMessageAsync(chatId: message.Chat.Id,
                                                                text: msg,
                                                                replyMarkup: new ReplyKeyboardRemove(), parseMode: ParseMode.Html);

                    break;

                case "Stop":
                    await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

                    currentTrader = null;
                    session.Data["Trader"] = null;

                    await botClient.SendTextMessageAsync(chatId: message.Chat.Id, "Stop requested...");

                    _myService.StopTraders();

                    await botClient.SendTextMessageAsync(chatId: message.Chat.Id, "Done!");

                    break;

                case "Restart":
                    await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

                    currentTrader = null;
                    session.Data["Trader"] = null;

                    await botClient.SendTextMessageAsync(chatId: message.Chat.Id, "Restart requested...");

                    await _myService.RestartTraders();

                    await botClient.SendTextMessageAsync(chatId: message.Chat.Id, "Done!");

                    break;

                case "Shutdown":
                    currentTrader = null;
                    session.Data["Trader"] = null;

                    if ((DateTime.Now - _lastShutDown).TotalSeconds < 60)
                    {
                        await botClient.SendTextMessageAsync(chatId: message.Chat.Id, "Bye!", replyMarkup: new ReplyKeyboardRemove());
                        _myService.Stop();
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(chatId: message.Chat.Id, "Are you sure? You have 60s to send Shutdown again otherwise I will reset I didn't hear you...");
                    }

                    _lastShutDown = DateTime.Now;

                    break;

                case "Snapshot":
                    if (currentTrader != null)
                    {
                        await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

                        var trend = currentTrader.GetTextSnapShot();
                        var img = currentTrader.GetSnapshotForBot();

                        if (img != null)
                        {
                            using (img)
                            {
                                await botClient.SendTextMessageAsync(
                                    chatId: message.Chat.Id,
                                    text: trend, parseMode: ParseMode.Html);

                                using (MemoryStream ms = new MemoryStream())
                                {
                                    img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                    ms.Flush();
                                    ms.Position = 0;


                                    await botClient.SendPhotoAsync(chatId: message.Chat.Id,
                                                                    photo: new InputOnlineFile(ms),
                                                                    caption: "This is what I look like!");
                                }
                            }
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(
                                chatId: message.Chat.Id,
                                text: "Wait for the first candle...");
                        }
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: "No asset selected");
                    }
                    break;
                case "State":
                    if (currentTrader != null)
                    {
                        var state = currentTrader.GetState();
                        if(String.IsNullOrEmpty(state))
                            state = "No operations here";

                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: state);
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: "No asset selected");
                    }

                    break;

                case "Open Positions":
                    var stateAll = _myService.GetAllTradersStatus();

                    if (!String.IsNullOrEmpty(stateAll.Trim()))
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: stateAll);
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: "No positions opened...");
                    }

                    break;
                case "Slots":
                    await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

                    if (currentTrader != null)
                    {
                        var dumpRes = "Deprecated";

                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: dumpRes, parseMode: ParseMode.Html);
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: "No asset selected");
                    }
                    break;
                case "P&L":
                    await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

                    if (currentTrader != null)
                    {

                        var balanceReport = await currentTrader.GetReport();

                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: balanceReport);
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: "No asset selected");
                    }
                    break;

                case "General P&L":
                    await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

                    string generalpl = String.Empty;
                    try
                    {
                        generalpl = await _myService.GetAllReport();
                    }
                    catch (Exception err)
                    {
                        generalpl = "Error: " + err.Message;
                    }

                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: generalpl, parseMode: ParseMode.Html);

                    break;

                case "Full Report":
                    await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

                    string fullReport = String.Empty;
                    try
                    {
                        fullReport = "<b>MONTH</b>:\n\n";
                        fullReport += _myService.GetOperationsSummary(30);
                        fullReport += "\n\n";
                        fullReport += "<b>WEEK</b>:\n\n";
                        fullReport += _myService.GetOperationsSummary(7);
                        fullReport += "<b>Day</b>:\n\n";
                        fullReport += _myService.GetOperationsSummary(1);
                    }
                    catch (Exception err)
                    {
                        fullReport = "Error: " + err.Message;
                    }

                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: fullReport, parseMode: ParseMode.Html);

                    break;

                case "Last Transactions":
                    await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

                    string lastTrans = String.Empty;
                    try
                    {
                        lastTrans = await _myService.GetLastOperations(30);
                    }
                    catch (Exception err)
                    {
                        lastTrans = "Error: " + err.Message;
                    }

                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: lastTrans, parseMode: ParseMode.Html);

                    break;
                case "Balance":
                    await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

                    string allCoins = String.Empty;
                    try
                    {
                        allCoins = await _myService.GetBalanceReport();

                    }
                    catch (Exception err)
                    {
                        allCoins = "Error: " + err.ToString();
                    }

                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: allCoins, parseMode: ParseMode.Html);

                    break;
                case "ReBalance":
                    await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

                    string rebalanceReport = "None";
                    try
                    {
                        rebalanceReport = await _myService.RebalanceFunds();
                    }
                    catch (Exception err)
                    {
                        rebalanceReport = "Error: " + err.ToString();
                    }

                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: rebalanceReport, parseMode: ParseMode.Html);  
                    break;              
                case "Try Enter":

                    await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

                    if (currentTrader != null)
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: "Wait while I get a prediction and check if it's advisible to open a position");

                        var enterResult = await currentTrader.SetPrediction();

                        if (enterResult == String.Empty)
                            enterResult = "Nothing to report";

                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: enterResult, parseMode: ParseMode.Html);

                        if(enterResult != "Nothing to report")
                        {
                            Thread.Sleep(1000);
                            await botClient.SendTextMessageAsync(
                                chatId: message.Chat.Id,
                                text: currentTrader.GetState(), parseMode: ParseMode.Html);
                        }
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: "No asset selected");
                    }

                    break;
                case "Force Order Status":
                    await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

                    string retText;
                    var hasOpsStatus = await currentTrader.ForceOrderStatusIfAny();
                    if (hasOpsStatus)
                        retText = "Done! Check the state for more information";
                    else
                        retText = "No operation running here";

                    await botClient.SendTextMessageAsync(chatId: message.Chat.Id,
                                                                text: retText);

                    if(hasOpsStatus)
                    {
                        Thread.Sleep(1000);
                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: currentTrader.GetState(), parseMode: ParseMode.Html);
                    }

                    break;               

                case "Ask To Close Position":
                    await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

                    await botClient.SendTextMessageAsync(chatId: message.Chat.Id,
                                                                text: "It can take a a couple of minutes to close the operation, wait...");

                    string askText;
                    var hasAskOps = currentTrader.AskToCloseOperationIfAny();
                    if (hasAskOps)
                    {
                        askText = "Done!";
                    }
                    else
                        askText = "No operation to close here";

                    await botClient.SendTextMessageAsync(chatId: message.Chat.Id,
                                                                text: askText); 

                    if(hasAskOps)
                    {
                        Thread.Sleep(1000);
                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: currentTrader.GetState(), parseMode: ParseMode.Html);
                    }
                                                                                                        
                    break;
                case "Close Position":
                    await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

                    await botClient.SendTextMessageAsync(chatId: message.Chat.Id,
                                                                text: "It can take a a couple of minutes to close the operation, wait...");

                    bool hardIndication = true;

                    string text;
                    var hasOps = await currentTrader.CloseAllOperationIfAny(hardIndication);
                    if (hasOps)
                    {
                        text = "Done!";
                    }
                    else
                        text = "No operation to close here";

                    await botClient.SendTextMessageAsync(chatId: message.Chat.Id,
                                                                text: text);

                    if(hasOps)
                    {
                        Thread.Sleep(1000);
                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: currentTrader.GetState(), parseMode: ParseMode.Html);
                    }                                                                

                    break;
                case "Clear":
                    await botClient.SendTextMessageAsync(chatId: message.Chat.Id,
                                                            text: "Removing keyboard",
                                                            replyMarkup: new ReplyKeyboardRemove());

                    session.Data["Trader"] = null;

                    break;

                case "Back":
                    await botClient.SendTextMessageAsync(chatId: message.Chat.Id,
                                                            text: "Removing keyboard",
                                                            replyMarkup: new ReplyKeyboardRemove());

                    session.Data["Trader"] = null;

                    await SendGeneralOptions(botClient, message);
                    break;

                default:
                    string[] split = command.Split(':');
                    if (split.Length > 1)
                    {
                        string asset = split[0];
                        string candleType = split[1];

                        currentTrader = _myService.GetAssetTrader(command);

                        if (currentTrader == null)
                            await botClient.SendTextMessageAsync(
                                chatId: message.Chat.Id,
                                text: "Unknown Asset or Service is stopped");
                        else
                        {
                            session.Data["Trader"] = currentTrader;

                            await SendAssetOptions(botClient, message, asset);
                        }
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: "Unknown command");
                    }

                    break;
            }

            Console.WriteLine("Message replied");
        }

        private async Task<Message> SendAssetOptions(ITelegramBotClient botClient, Message message, string asset)
        {
            var replyKeyboardMarkup = new ReplyKeyboardMarkup(
                new KeyboardButton[][]
                {
                    new KeyboardButton[] { "Snapshot","State", "Slots" },
                    new KeyboardButton[] { "Try Enter" },
                    new KeyboardButton[] { "Force Order Status"},
                    new KeyboardButton[] { "Ask To Close Position", "Close Position" },
                    new KeyboardButton[] { "Back"}
                })
            {
                ResizeKeyboard = true
            };

            return await botClient.SendTextMessageAsync(chatId: message.Chat.Id,
                                                            text: "Opções para - " + asset,
                                                            replyMarkup: replyKeyboardMarkup);

        }

        private async Task<Message> SendGeneralOptions(ITelegramBotClient botClient, Message message)
        {
            List<KeyboardButton[]> buttonsLines = new List<KeyboardButton[]>();
            buttonsLines.Add(new KeyboardButton[] { "General P&L", "Full Report", "Last Transactions" });
            buttonsLines.Add(new KeyboardButton[] { "Open Positions" });
            foreach (var pair in _traders)
                buttonsLines.Add(new KeyboardButton[] { pair.Key });
            buttonsLines.Add(new KeyboardButton[] { "Balance", "ReBalance", "Config" });
            buttonsLines.Add(new KeyboardButton[] { "Stop", "Restart", "Shutdown" });
            buttonsLines.Add(new KeyboardButton[] { "Clear" });

            var assetButtons = buttonsLines.ToArray();

            var replyKeyboardMarkup = new ReplyKeyboardMarkup(buttonsLines.ToArray())
            {
                ResizeKeyboard = true
            };

            return await botClient.SendTextMessageAsync(chatId: message.Chat.Id,
                                                            text: "Escolha",
                                                            replyMarkup: replyKeyboardMarkup);

        }

        private async Task BotOnCallbackQueryReceived(ITelegramBotClient botClient, CallbackQuery callbackQuery)
        {
            await botClient.AnswerCallbackQueryAsync(
                callbackQueryId: callbackQuery.Id,
                text: $"Received {callbackQuery.Data}");

            AssetTrader currentTrader;

            var session = GetSession(callbackQuery.Message.Chat.Id);
            currentTrader = (AssetTrader)session.Data["Trader"];
        }

        private string Truncate(string msg, int size)
        {
            return msg.Substring(0, Math.Min(msg.Length - 1, size));
        }

        private Task UnknownUpdateHandlerAsync(ITelegramBotClient botClient, Update update)
        {
            Console.WriteLine($"Unknown update type: {update.Type}");
            return Task.CompletedTask;
        }
    }

    public class TelegramSession
    {
        private Dictionary<string, object> _values;

        public TelegramSession()
        {
            _values = new Dictionary<string, object>();
        }

        public Dictionary<string, object> Data
        {
            get
            {
                return _values;
            }
        }
    }
}