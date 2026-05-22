using RemnantSaveGuardian.Views.Windows;
using System;
using System.Collections.Generic;
using System.IO;

namespace RemnantSaveGuardian
{
    internal static class Logger
    {
        public static event EventHandler<MessageLoggedEventArgs> MessageLogged;
        private static List<LogMessage> messages = new();
        public static List<LogMessage> Messages { get { return messages; } }

        private static string LogFolder => Path.Combine(AppContext.BaseDirectory, "logs");
        private static string LogPath => Path.Combine(LogFolder, $"log_{DateTime.Now:yyyy-MM-dd}.txt");

        static Logger()
        {
            try
            {
                Directory.CreateDirectory(LogFolder);
                // Write session header at the start of each run
                File.AppendAllText(LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [INFO] Session started — Version {typeof(MainWindow).Assembly.GetName().Version}\r\n");
            }
            catch { }
        }

        public static void Log(object message, LogType logType)
        {
            message ??= "null";
            MessageLogged?.Invoke(null, new(message.ToString(), logType));
            messages.Add(new(message.ToString(), logType));
            WriteToFile(logType, message.ToString());
        }

        public static void Log(object message) => Log(message, LogType.Normal);
        public static void Success(object message) => Log(message, LogType.Success);
        public static void Error(object message) => Log(message, LogType.Error);
        public static void Warn(object message) => Log(message, LogType.Warning);

        private static void WriteToFile(LogType logType, string message)
        {
            try
            {
                var prefix = logType switch
                {
                    LogType.Error   => "ERROR",
                    LogType.Warning => "WARN ",
                    LogType.Success => "OK   ",
                    _               => "INFO ",
                };
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{prefix}] {message}\r\n");
            }
            catch { }
        }
    }

    public enum LogType
    {
        Normal,
        Success,
        Error,
        Warning,
    }

    public class MessageLoggedEventArgs : EventArgs
    {
        public string Message { get; set; }
        public LogType LogType { get; set;}
        public MessageLoggedEventArgs(string message, LogType logType)
        {
            Message = message;
            LogType = logType;
        }
        public MessageLoggedEventArgs(string message)
        {
            Message = message;
            LogType = LogType.Normal;
        }
    }
    public class LogMessage
    {
        public string Message { get; set; }
        public LogType LogType { get; set; }
        public LogMessage(string message, LogType logType)
        {
            Message = message;
            LogType = logType;
        }
    }
}
