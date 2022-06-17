# CandlesFace AI Trading

This is an experimental .NET 6.0 solution for a trading bot based on image recognition using a neural network model pre-trained with "faces" of
45 1 hour candles that have been grouped by the ones with optimal result, considering exists in 16, 25, 30, and 36 periods and 1% stops.

# Projects

# Midas.Core

Contains all core classes to control the Asset Trades, TradeOperations, Broker communications, etc...

# Midas.DataGather

A BitCoin specific news indexer that sends unique BitCoin news to the telegram channel

# Midas.Investor

Executable project responsible for monitoring the market for a specific broker(Binance, CoinBase, etc...), host the telegram server for remote
controlling and reports.

The can be used as a backtest tool, since it can be configured to run on Historical data streams :D

# Midas.ML

Some of the python files with the grouping and neural networks trainning. I will post later the main Colab Python files with the image grouping
and neural networks training.

# Midas.Runner

Executable project responsible por generating all possible cards(images) of historical trading history in any crypto Asset Pair

# Metrics

My experiments show that running this model based on the date range 2021-05-01 until 2022-04-31 would generate 120% profit, considering the following stats:

Number of transations: 318

Taxes: Worth of 23%

Success Rate: 24%

StopLoss: 10% of the MaxAtr in 14 periods

Avg gain per transaction: 0.39%

Optimized cost per transaction(In and Out) using Binance: 0.0085%


I would definitelly accept contributions and suggestions!

You can find me at ciro.nola@gmail.com
