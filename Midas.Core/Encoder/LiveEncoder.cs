using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;

namespace Midas.Core.Encoder
{
    public class LiveEncoder : IDisposable
    {
        private string _baseFfmpegPath;
        private string _broadcastUrl;
        private string _broadCastKey;

        private int _frameRate;

        private System.Drawing.Imaging.ImageFormat _imageFormat;

        private DateTime _start;

        public LiveEncoder(string baseFfmpegPath, string broadcastUrl, string broadCastKey, int frameRate, System.Drawing.Imaging.ImageFormat imageFormat)
        {
            _baseFfmpegPath = baseFfmpegPath;
            _broadcastUrl = broadcastUrl;
            _broadCastKey = broadCastKey;
            _frameRate = frameRate;
            _runningProcess = null;
            _imageFormat = imageFormat;
        }

        private Process _runningProcess;

        public void Start()
        {
            //string templateCommand = @"-f image2pipe -framerate {0} -i - -c:v libx264 -preset slow -x264-params keyint=5:min-keyint=5:scenecut=-1 -b:v 360k -maxrate 360k -bufsize 1080k -framerate 4 -g 8 -bufsize 720k -pix_fmt yuv420p -f flv {1}/{2}";
            //string templateCommand = "-f image2pipe -framerate {0} -i - -c:v libx264 -preset veryfast -b:v 360k -maxrate 360k -bufsize 1080k -bufsize 720k -vf \"format=yuv420p\" -f flv {1}{2}";
            string templateCommand = "-f image2pipe -framerate {0} -i - -c:v libx264 -preset veryfast -x264-params keyint=10:scenecut=0:nal-hrd=cbr:force-cfr=1 -b:v 500k -bufsize 1000k -pix_fmt yuv420p -f flv {1}/{2}";


            string commandArgs = String.Format(templateCommand, _frameRate, _broadcastUrl, _broadCastKey);
            string ffmpegPath = Path.Combine(_baseFfmpegPath, _baseFfmpegPath);

            _runningProcess = new Process
            {
                StartInfo =
                {
                    FileName = ffmpegPath,
                    Arguments = commandArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true
                }
            };

            _start = DateTime.Now;

            _runningProcess.Start();
        }

        public void Dispose()
        {
            _runningProcess.Kill(true);
        }

        public void SendImage(Bitmap frame)
        {
            if ((DateTime.Now - _start).TotalMilliseconds > 1000) //We wait a second before sending images
            {
                MemoryStream ms = new MemoryStream();
                frame.Save(ms, _imageFormat);
                ms.Flush();
                ms.Position = 0;

                byte[] data = ms.ToArray();

                BinaryWriter bw = new BinaryWriter(_runningProcess.StandardInput.BaseStream);

                bw.Write(data);
                bw.Flush();
            }
        }

        public void Stop()
        {
            Dispose();
        }
    }
}