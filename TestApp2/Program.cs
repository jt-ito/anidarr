using System;
using System.Collections.Generic;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace TestApp2
{
    public class ExceptionVerification : Target
    {
        public List<LogEventInfo> Logs = new List<LogEventInfo>();

        protected override void Write(LogEventInfo logEvent)
        {
            Console.WriteLine("ExceptionVerification.Write called with level " + logEvent.Level);
            if (logEvent.Level >= LogLevel.Warn)
            {
                Logs.Add(logEvent);
            }
        }
    }

    class Program
    {
        static readonly Logger TestLogger = LogManager.GetLogger("TestLogger");

        static void Main(string[] args)
        {
            LogManager.Configuration = new LoggingConfiguration();
            LogManager.GlobalThreshold = LogLevel.Trace;
            
            var target = new ExceptionVerification();
            LogManager.Configuration.AddTarget("ExceptionVerification", target);
            LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Warn, target));
            LogManager.Configuration = LogManager.Configuration;
            
            Console.WriteLine("IsWarnEnabled: " + TestLogger.IsWarnEnabled);
            TestLogger.Warn("This is a warning");
            
            Console.WriteLine("Captured logs: " + target.Logs.Count);
        }
    }
}
