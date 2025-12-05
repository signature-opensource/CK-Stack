using CK.Core;
using CKli.Core;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace CKli.Net8Migration.Plugin;

public sealed class Net8MigrationPlugin : PrimaryPluginBase
{
    /// <summary>
    /// This is a primary plugin. <see cref="PrimaryPluginBase.PrimaryPluginContext"/>
    /// is always available (as well as the <see cref="PluginBase.World"/>).
    /// </summary>
    public Net8MigrationPlugin( PrimaryPluginContext primaryContext )
        : base( primaryContext )
    {
        primaryContext.World.Events.PluginInfo += e =>
        {
            Throw.CheckState( PrimaryPluginContext.PluginInfo.FullPluginName == "CKli.Net8Migration.Plugin" );
            Throw.CheckState( World == e.World );
            Throw.CheckState( PrimaryPluginContext.World == e.World );
            e.AddMessage( PrimaryPluginContext, e.ScreenType.Text( "Message from 'Net8Migration' plugin." ) );
            e.Monitor.Info( $"New 'Net8Migration' in world '{e.World.Name}' plugin certainly requires some development." );
            Console.WriteLine( $"Hello from 'Net8Migration' plugin." );
        };
    }
}
