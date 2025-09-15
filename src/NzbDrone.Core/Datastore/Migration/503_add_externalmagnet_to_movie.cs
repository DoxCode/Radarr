using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(503)]
    public class add_externalmagnet_to_movie : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            if (!Schema.Table("Movies").Column("ExternalMagnet").Exists())
            {
                Alter.Table("Movies")
                     .AddColumn("ExternalMagnet").AsString().Nullable();
            }
        }
    }
}
