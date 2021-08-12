using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Midas.Core.Services;
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

        public CandleBot(InvestorService service, RunParameters @params)
        {
            _myService = service;
            _params = @params;
        }

        public void Start()
        {
            _telegramClient = new TelegramBotClient(_params.BotToken);

            var meTask = _telegramClient.GetMeAsync();
            meTask.Wait(10000);
            var me = meTask.Result;

            _cancToken = new CancellationTokenSource();

            // StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
            _telegramClient.StartReceiving(new DefaultUpdateHandler(this.HandleUpdateAsync, this.HandleErrorAsync),
                               _cancToken.Token);

            TraceAndLog.StaticLog("Main", $"Start listening for @{me.Username}");            
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

        private string GetReport()
        {
            return _myService.GetReport();
        }

        private Bitmap GetSnapShot()
        {
            return _myService.GetSnapshot();
        }
        private string GetTextSnapshot()
        {
            return _myService.GetTextSnapShop();
        }

        private string GetState()
        {
            return _myService.GetState();
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
            switch (command)
            {
                case "hi":
                    await SendOptions(botClient, message);
                    break;
                case "forcesell":
                    await botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);
                    
                    var ret = _myService.ForceMaketOrder();

                    

                    await botClient.SendTextMessageAsync(chatId: message.Chat.Id,
                                                                text: ret,
                                                                replyMarkup: new ReplyKeyboardRemove());

                    break;
                default:
                    const string usage = "Usage:\n" +
                                        "Send \"hi\" to get the options";

                    await botClient.SendTextMessageAsync(chatId: message.Chat.Id,
                                                                text: usage,
                                                                replyMarkup: new ReplyKeyboardRemove());
                    break;
            }

            Console.WriteLine("Message replied");
        }

        private async Task<Message> SendOptions(ITelegramBotClient botClient, Message message)
        {
            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] {InlineKeyboardButton.WithCallbackData("Snapshot", "snapshot")},
                new[] {InlineKeyboardButton.WithCallbackData("State", "state")},
                new[] {InlineKeyboardButton.WithCallbackData("P&L", "report")},
                new[] {InlineKeyboardButton.WithCallbackData("Balance", "balance")}
            });

            string prefix = String.Empty;
            if(message.From.Id == 1392335823)
                prefix = "E ai princesa! ";

            return await botClient.SendTextMessageAsync(chatId: message.Chat.Id,
                                                        text: prefix+ "Please choose:",
                                                        replyMarkup: inlineKeyboard);
        }


        private async Task BotOnCallbackQueryReceived(ITelegramBotClient botClient, CallbackQuery callbackQuery)
        {
            await botClient.AnswerCallbackQueryAsync(
                callbackQueryId: callbackQuery.Id,
                text: $"Received {callbackQuery.Data}");

            switch (callbackQuery.Data)
            {
                case "snapshot":
                    await botClient.SendChatActionAsync(callbackQuery.Message.Chat.Id, ChatAction.Typing);

                    var trend = GetTextSnapshot();
                    var img = GetSnapShot();

                    if (img != null)
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: callbackQuery.Message.Chat.Id,
                            text: trend);

                        using (MemoryStream ms = new MemoryStream())
                        {
                            img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            ms.Flush();
                            ms.Position = 0;


                            await botClient.SendPhotoAsync(chatId: callbackQuery.Message.Chat.Id,
                                                            photo: new InputOnlineFile(ms),
                                                            caption: "This is what I look like!");
                        }
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: callbackQuery.Message.Chat.Id,
                            text: "Wait for the first candle...");
                    }
                    break;
                case "state":
                    var state = GetState();

                    if(state == null)
                        state = "No state";

                    await botClient.SendTextMessageAsync(
                        chatId: callbackQuery.Message.Chat.Id,
                        text: state);

                    break;
                case "report":
                    await botClient.SendChatActionAsync(callbackQuery.Message.Chat.Id, ChatAction.Typing);                

                    var report = GetReport();

                    await botClient.SendTextMessageAsync(
                        chatId: callbackQuery.Message.Chat.Id,
                        text: report);
                    break;

                case "balance":
                    await botClient.SendChatActionAsync(callbackQuery.Message.Chat.Id, ChatAction.Typing);

                    string balances;               
                    try
                    {
                        balances = _myService.GetBalanceReport();
                    }
                    catch(Exception err)
                    {
                        balances = "Error: "+err.Message;
                    }

                    await botClient.SendTextMessageAsync(
                        chatId: callbackQuery.Message.Chat.Id,
                        text: Truncate(balances,200));

                    break;
            }
        }

        private string Truncate(string msg, int size)
        {
            return msg.Substring(0,Math.Min(msg.Length-1,size));
        }

        private Task UnknownUpdateHandlerAsync(ITelegramBotClient botClient, Update update)
        {
            Console.WriteLine($"Unknown update type: {update.Type}");
            return Task.CompletedTask;
        }
    }
}