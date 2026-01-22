using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace ParkingService.Services
{
    public class CriticalErrorLogger
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, int> _errorCounts = new ConcurrentDictionary<string, int>();

        public CriticalErrorLogger(ILogger logger)
        {
            _logger = logger;
        }

        public void LogCriticalOperation(string category, string operation, string status, Dictionary<string, object>? details = null)
        {
            _logger.LogInformation("Category: {Category}, Operation: {Operation}, Status: {Status}", category, operation, status);
        }

        public void LogCriticalError(string category, string operation, Exception? ex = null, Dictionary<string, object>? details = null)
        {
            _errorCounts.AddOrUpdate(category, 1, (k, v) => v + 1);
            if (ex != null)
            {
                _logger.LogError(ex, "Critical Error - Category: {Category}, Operation: {Operation}", category, operation);
            }
            else
            {
                _logger.LogError("Critical Error - Category: {Category}, Operation: {Operation}", category, operation);
            }
        }

        public void LogSdkCall(string callName, int handle, int result, Dictionary<string, object>? details = null)
        {
            if (result < 0)
            {
                _logger.LogWarning("SDK Call Failed: {CallName}, Handle: {Handle}, Result: {Result}", callName, handle, result);
            }
            else
            {
                 _logger.LogDebug("SDK Call Success: {CallName}, Handle: {Handle}, Result: {Result}", callName, handle, result);
            }
        }

        public Dictionary<string, object> GetErrorStatistics()
        {
            var stats = new Dictionary<string, object>();
            foreach (var kvp in _errorCounts)
            {
                stats[kvp.Key] = kvp.Value;
            }
            return stats;
        }

        public void ResetCounters()
        {
            _errorCounts.Clear();
        }
    }
}
