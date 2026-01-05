using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace backend.Services.Logging;

public class FileLogger : ILogger
{
    private readonly string _name;
    private readonly Func<FileLoggerConfiguration> _getCurrentConfig;
    private static readonly object _lock = new object();

    public FileLogger(string name, Func<FileLoggerConfiguration> getCurrentConfig)
    {
        _name = name;
        _getCurrentConfig = getCurrentConfig;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var config = _getCurrentConfig();
        if (string.IsNullOrWhiteSpace(config.FolderPath))
        {
            return;
        }

        var message = formatter(state, exception);
        var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{logLevel}] [{_name}] {message}";
        if (exception != null)
        {
            logEntry += Environment.NewLine + exception.ToString();
        }

        var fileName = $"log_{DateTime.Now:yyyyMMdd}.txt";
        var fullPath = Path.Combine(config.FolderPath, fileName);

        // Ensure directory exists
        if (!Directory.Exists(config.FolderPath))
        {
            try 
            {
                Directory.CreateDirectory(config.FolderPath);
            }
            catch 
            {
                // If we can't create the directory, we can't log.
                return;
            }
        }

        lock (_lock)
        {
            try
            {
                File.AppendAllText(fullPath, logEntry + Environment.NewLine);
            }
            catch
            {
                // Suppress file write errors to avoid crashing the app
            }
        }
    }
}

public class FileLoggerConfiguration
{
    public string FolderPath { get; set; } = string.Empty;
}
