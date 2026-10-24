using System.Threading;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.TPL;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test.TPLTests
{
    [TestFixture]
    public class ThreadPoolFloorFixture : TestBase
    {
        [Test]
        public void should_raise_the_minimum_thread_count_to_at_least_the_floor_without_lowering_it()
        {
            // Raising the floor is harmless for tests running in parallel; lowering it is not, so this never does.
            ThreadPool.GetMinThreads(out var workerBefore, out var portBefore);

            ThreadPoolFloor.Apply();

            ThreadPool.GetMinThreads(out var worker, out var port);
            worker.Should().BeGreaterOrEqualTo(ThreadPoolFloor.MinimumThreads);
            port.Should().BeGreaterOrEqualTo(ThreadPoolFloor.MinimumThreads);
            worker.Should().BeGreaterOrEqualTo(workerBefore);
            port.Should().BeGreaterOrEqualTo(portBefore);
        }
    }
}
