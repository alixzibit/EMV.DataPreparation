using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


    public class FileLogger : ILogger
    {
        private readonly string _filePath;
        private static readonly object _lock = new object();
        private readonly string _categoryName;

        public FileLogger(string categoryName, string filePath)
        {
            _categoryName = categoryName;
            _filePath = filePath;
        }

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel)
        {
            // For simplicity, enable all log levels
            return true;
        }

        public void Log<TState>(LogLevel logLevel,
                                EventId eventId,
                                TState state,
                                Exception exception,
                                Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            lock (_lock)
            {
                var message = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {_categoryName} - {formatter(state, exception)}";
                if (exception != null)
                    message += Environment.NewLine + exception;

                File.AppendAllText(_filePath, message + Environment.NewLine);
            }
        }
    }

    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _filePath;
        private readonly Dictionary<string, FileLogger> _loggers = new Dictionary<string, FileLogger>();

        public FileLoggerProvider(string filePath)
        {
            _filePath = filePath;
        }

        public ILogger CreateLogger(string categoryName)
        {
            if (!_loggers.ContainsKey(categoryName))
            {
                _loggers[categoryName] = new FileLogger(categoryName, _filePath);
            }
            return _loggers[categoryName];
        }

        public void Dispose()
        {
            _loggers.Clear();
        }
    }


