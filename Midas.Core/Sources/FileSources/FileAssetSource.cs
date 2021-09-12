using System;
using System.IO;
using Midas.Core.Common;
using Midas.FeedStream;
using Midas.Sources;

namespace Midas.Sources
{
    public class FileAssetSource : AssetSource
    {
        public override AssetFeedStream GetFeedStream(
            string asset,
            DateRange range,
            CandleType type
        )
        {
            //Recebendo um range de datas pega os arquivos mapeados, inicia pelo primeiro
            //abre o stream e vai até o ponto de inicio da data para retornar o stream.

            var configMapper = new ConfigFileMapper("/Users/cironola/Documents/CandlesFace Projects/CandlesGit/Midas.Core/Sources/ConfigFiles");
            var mapper = new DateToFileMapper(configMapper.GetConfigMapper(asset, type));
            var files = mapper.GetFiles(range);

            if(type < mapper.MinGroup)
                throw new ArgumentException("Candle type not supported by the source - "+type.ToString());

            FileAssetFeedStream ret = null;
            if(files.Length > 0)
            {
                FileAssetFeedStream binanceStream = new BinanceFileAssetFeedStream(
                    files,
                    range,
                    mapper.MinGroup,
                    type
                );
                
                ret = binanceStream;
            }
            else
                throw new ArgumentException(String.Format("No records found! - ConfigFile"));

            return ret;
        }

    }

    public class ConfigFileMapper
    {
        private string _baseConfigPath;

        public ConfigFileMapper(string baseConfigPath)
        {
            _baseConfigPath = baseConfigPath;
        }
        /*
            Return the JsonConfig containing the file mappings
        */
        public string GetConfigMapper(string asset, CandleType type) 
        {
            string filePath = Path.Combine(this._baseConfigPath, String.Format("{0}_{1}.json", asset, type.ToString()));
            string config = null;
            if(File.Exists(filePath))
            {
                config = File.ReadAllText(filePath);
            }
            else
                throw new FileNotFoundException(String.Format("Impossible to map Asset {0} to a config file. Check that the file {1} exists", asset,filePath));

            return config;
        }

    }
}