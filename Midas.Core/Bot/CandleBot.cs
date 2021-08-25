using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
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
                await HandleErrorAsync(botClient, exception, cancellationToken);
            }
        }

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
                case "config":
                    await botClient.SendTextMessageAsync(chatId: message.Chat.Id,
                                                                text: _myService.GetAssetsConfig(),
                                                                replyMarkup: new ReplyKeyboardRemove());

                    break;

                case "Snapshot":
                    if (currentTrader != null)
                    {
                        await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

                        var trend = currentTrader.GetTextSnapShot();
                        var img = currentTrader.GetSnapshotForBot();

                        if (img != null)
                        {
                            await botClient.SendTextMessageAsync(
                                chatId: message.Chat.Id,
                                text: trend);

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

                        if (state == null)
                            state = "No state";

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
                case "P&L":
                    await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

                    if (currentTrader != null)
                    {

                        var balanceReport = currentTrader.GetReport();

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
                        generalpl = _myService.GetReport();
                    }
                    catch (Exception err)
                    {
                        generalpl = "Error: " + err.Message;
                    }

                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: Truncate(generalpl, 200));

                    break;
                case "Balance":
                    await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

                    string allCoins = String.Empty;
                    try
                    {
                        allCoins = _myService.GetBalanceReport();

                    }
                    catch (Exception err)
                    {
                        allCoins = "Error: " + err.Message;
                    }

                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: Truncate(allCoins, 1000));

                    break;
                case "ForceSell":
                    await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

                    var ret = currentTrader.ForceMaketOrder();

                    await botClient.SendTextMessageAsync(chatId: message.Chat.Id,
                                                                text: ret);

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
                    string asset = split[0];
                    string candleType = split[1];

                    currentTrader = null;

                    _traders.TryGetValue(command, out currentTrader);

                    if (currentTrader == null)
                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: "Unknown Asset");
                    else
                    {
                        session.Data["Trader"] = currentTrader;

                        await SendAssetOptions(botClient, message, asset);
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
                    new KeyboardButton[] { "Snapshot","State" },
                    new KeyboardButton[] { "P&L", "ForceSell" },
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
            buttonsLines.Add(new KeyboardButton[] { "General P&L" });
            foreach (var pair in _traders)
                buttonsLines.Add(new KeyboardButton[] { pair.Key });
            buttonsLines.Add(new KeyboardButton[] { "Balance" });
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

            // switch (callbackQuery.Data)
            // {
            //     case "Snapshot":
            //         if (currentTrader != null)
            //         {
            //             await botClient.SendChatActionAsync(callbackQuery.Message.Chat.Id, ChatAction.Typing);

            //             var trend = currentTrader.GetTextSnapShot();
            //             var img = currentTrader.GetSnapshotForBot();

            //             if (img != null)
            //             {
            //                 await botClient.SendTextMessageAsync(
            //                     chatId: callbackQuery.Message.Chat.Id,
            //                     text: trend);

            //                 using (MemoryStream ms = new MemoryStream())
            //                 {
            //                     img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            //                     ms.Flush();
            //                     ms.Position = 0;


            //                     await botClient.SendPhotoAsync(chatId: callbackQuery.Message.Chat.Id,
            //                                                     photo: new InputOnlineFile(ms),
            //                                                     caption: "This is what I look like!");
            //                 }
            //             }
            //             else
            //             {
            //                 await botClient.SendTextMessageAsync(
            //                     chatId: callbackQuery.Message.Chat.Id,
            //                     text: "Wait for the first candle...");
            //             }
            //         }
            //         else
            //         {
            //             await botClient.SendTextMessageAsync(
            //                 chatId: callbackQuery.Message.Chat.Id,
            //                 text: "No asset selected");
            //         }
            //         break;
            // }
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