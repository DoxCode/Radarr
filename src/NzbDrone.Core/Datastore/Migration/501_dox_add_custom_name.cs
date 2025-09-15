using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(501)]
    public class dox_add_custom_name : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Añade columna Nullable para no romper DB existentes.
            if (!Schema.Table("Movies").Column("CustomName").Exists())
            {
                Alter.Table("Movies")
                     .AddColumn("CustomName").AsString().Nullable();
            }
        }
    }
}
