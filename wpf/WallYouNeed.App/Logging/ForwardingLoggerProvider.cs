using System;
using Microsoft.Extensions.Logging;

namespace WallYouNeed.App.Logging
{
    /// <summary>
    /// A logger provider that forwards logging calls to an existing ILoggerFactory
    /// </summary>
    public class ForwardingLoggerProvider : ILoggerProvider
    {
        private readonly ILoggerFactory _loggerFactory;

        public ForwardingLoggerProvider(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggerFactory.CreateLogger(categoryName);
        }

        public void Dispose()
        {
            // No need to dispose the factory as it's managed elsewhere
        }
    }
} 