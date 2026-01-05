using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;

namespace backend.Services.Logging;

public static class FileLoggerExtensions
{
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder, Action<FileLoggerConfiguration> configure)
    {
        builder.Services.AddSingleton<ILoggerProvider, FileLoggerProvider>();
        builder.Services.Configure(configure);
        return builder;
    }
}
