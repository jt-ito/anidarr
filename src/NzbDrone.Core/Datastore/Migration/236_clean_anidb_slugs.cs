using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(236)]
    public class clean_anidb_slugs : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.Sql(@"
                UPDATE OR IGNORE Series
                SET TitleSlug = SUBSTR(TitleSlug, 1, INSTR(TitleSlug, '-anidb-') - 1)
                WHERE INSTR(TitleSlug, '-anidb-') > 0;
            ");
        }
    }
}
