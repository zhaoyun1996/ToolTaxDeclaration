using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace MISA.ASP.ClientApp.Utils.Logging
{
    internal class LogUtil
    {
        #region Declare
        private static Logger _fileLogger { get; set; }
        private static Logger MainLogger
        {
            get
            {
                if(_fileLogger == null)
                {
                    _fileLogger = LogManager.GetLogger("*");
                }

                return _fileLogger;
            }
        }
        #endregion

        #region Sub/Func
        public static void LogError(string message)
        {
            MainLogger.Log(LogLevel.Error, message);
        }

        public static void LogError(Exception ex, string message)
        {
            MainLogger.Log(LogLevel.Error, ex, message);
        }

        public static void LogError(Exception ex)
        {
            MainLogger.Log(LogLevel.Error, ex);
        }

        public static void LogError(Exception ex, HttpResponseMessage responseMessage)
        {
            MainLogger.Log(LogLevel.Error, ex);
            if (responseMessage != null)
            {
                MainLogger.Log(LogLevel.Error,
                    $"HTTP {(int)responseMessage.StatusCode} ({responseMessage.ReasonPhrase})");
            }
        }

        public static void LogInfo(string message)
        {
            MainLogger.Log(LogLevel.Info, message);
        }

        public static void LogTrace(string message)
        {
            MainLogger.Log(LogLevel.Trace, message);
        }
        #endregion
    }
}
