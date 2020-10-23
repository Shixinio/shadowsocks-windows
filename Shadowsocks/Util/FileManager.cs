using NLog;

using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Shadowsocks.Controller
{
    public static class FileManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public static bool ByteArrayToFile(string fileName, byte[] content)
        {
            try
            {
                using (var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write))
                    fs.Write(content, 0, content.Length);

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return false;
        }

        public static void UncompressFile(string fileName, byte[] content)
        {
            using var input = new GZipStream(new MemoryStream(content), CompressionMode.Decompress, false);
            using var outputStream = new FileStream(fileName, FileMode.Create, FileAccess.Write);

            input.CopyTo(outputStream);
        }

        public static string NonExclusiveReadAllText(string path) => NonExclusiveReadAllText(path, Encoding.Default);

        public static string NonExclusiveReadAllText(string path, Encoding encoding)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs, encoding);
                return sr.ReadToEnd();
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                throw ex;
            }
        }
    }
}
