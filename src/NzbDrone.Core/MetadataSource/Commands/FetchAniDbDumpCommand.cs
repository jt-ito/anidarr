using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MetadataSource.Commands
{
    public class FetchAniDbDumpCommand : Command
    {
        public override bool SendUpdatesToClient => true;
        public override bool UpdateScheduledTask => true;
    }
}
