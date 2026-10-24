using System;
using System.Threading;

namespace NzbDrone.Common.TPL
{
    public static class ThreadPoolFloor
    {
        public const int MinimumThreads = 32;

        // Outbound HTTP calls and rate limiters block the thread they run on while they wait. The pool
        // normally starts with one thread per CPU (two in a container limited to 2 CPUs) and adds more
        // slowly, so a few waiting threads make unrelated work queue behind them. Threads are only
        // created when needed, so a higher floor costs nothing while the app is idle.
        public static void Apply()
        {
            ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);

            ThreadPool.SetMinThreads(Math.Max(workerThreads, MinimumThreads), Math.Max(completionPortThreads, MinimumThreads));
        }
    }
}
