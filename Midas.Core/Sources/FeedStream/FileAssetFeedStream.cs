using System;
using System.Collections.Generic;
using Midas.Core.Common;
using System.Linq;
using System.IO;

namespace Midas.FeedStream
{
    public abstract class FileAssetFeedStream : AssetFeedStream, IDisposable
    {
        private string[] _files;
        private DateRange _range;
        private CandleType _fileCandleType;
        private CandleType _queryCandleType;
        private FileStream _activeFile;
        private StreamReader _currentStream;
        private int _filePosition;

        public FileAssetFeedStream(string[] files, DateRange range, CandleType fileCandleType, CandleType queryCandleType)
        {
            if (files.Length == 0)
                throw new ArgumentException("Impossible to create FileFeedStream with 0 files on it dude!");

            _files = files;
            _range = range;
            _fileCandleType = fileCandleType;
            _queryCandleType = queryCandleType;
            _activeFile = null;
            _currentStream = null;

            GetNextFile();
        }

        ~FileAssetFeedStream()
        {
            this.Close(true);
        }

        public override int BufferCount()
        {
            throw new NotImplementedException("BufferCount no available for CandleFileStreams");
        }


        public override Candle Peek()
        {
            throw new NotImplementedException("Peek is not available for CandleFileStreams.");
        }

        private StreamReader GetCurrentStream()
        {
            return _currentStream;
        }

        private StreamReader GetNextFile()
        {
            if (_filePosition < _files.Length)
            {
                if (_activeFile != null)
                    _activeFile.Dispose();

                _activeFile = File.Open(_files[_filePosition], FileMode.Open, FileAccess.Read, FileShare.Read);
                _currentStream = new StreamReader(_activeFile);
                _filePosition++;
            }
            else
                _currentStream = null;

            return _currentStream;
        }

        public override Candle[] Read(int periods)
        {
            int ratio = Convert.ToInt32(_queryCandleType) / Convert.ToInt32(_fileCandleType);
            List<Candle> readCandles = new List<Candle>(periods);
            List<Candle> buffer = new List<Candle>(ratio);

            int filePeriods = periods * ratio; //The necessary lines to read from the file to make the amount of periods asked
            string fileLine = "";
            int linePos = 0;
            StreamReader lastFile = GetCurrentStream();
            bool canStart = true;
            while (linePos < filePeriods && lastFile != null)
            {
                fileLine = lastFile.ReadLine();
                if (fileLine != null)
                {
                    var candle = ParseFileLine(fileLine);

                    //Check if the candle is inside the desired range
                    //PS: We do a first check when we map files but since the file units can be based on one by month, for ex, we
                    //PS: will get here a list of files that contains the data in the range PLUS all the other data in the file
                    if (_range.IsInside(candle.PointInTime_Open))
                    {
                        // if (Candle.IsMilestone(candle.OpenTime, _queryCandleType))
                        //     canStart = true;

                        if (canStart)
                        {
                            if (ratio == 1)
                                readCandles.Add(candle);
                            else
                            {
                                buffer.Add(candle);
                                if (buffer.Count == ratio)
                                {
                                    readCandles.Add(Candle.Reduce(buffer));
                                    buffer.Clear();
                                }
                            }
                        }

                        linePos++;
                    }
                }
                else
                {
                    lastFile = GetNextFile();
                }
            }

            Candle[] ret = null;
            if (readCandles.Count > 0)
                ret = readCandles.ToArray();

            return ret;
        }

        public abstract Candle ParseFileLine(string line);

        public void Close(bool fromGC = false)
        {
            if (_activeFile != null)
                _activeFile.Close();

            if (!fromGC)
                GC.SuppressFinalize(this);
        }

        public override void Dispose()
        {
            this.Close(false);
        }
    }

    public class BinanceFileAssetFeedStream : FileAssetFeedStream
    {
        public BinanceFileAssetFeedStream(string[] files, DateRange range, CandleType fileCandleType, CandleType queryCandleType) :
            base(files, range, fileCandleType, queryCandleType)
        {

        }

        public override Candle ParseFileLine(string line)
        {

            string[] fields = line.Split(',');
            Candle c = new Candle();
            var openTime = FromTimeStamp(Convert.ToDouble(fields[0]));
            c.OpenTime = openTime;
            c.OpenValue = Convert.ToDouble(fields[1]);
            c.HighestValue = Convert.ToDouble(fields[2]);
            c.LowestValue = Convert.ToDouble(fields[3]);
            c.CloseValue = Convert.ToDouble(fields[4]);
            c.Volume = Convert.ToDouble(fields[5]);
            c.CloseTime = FromTimeStamp(Convert.ToDouble(fields[6]));

            return c;
        }


    }
}