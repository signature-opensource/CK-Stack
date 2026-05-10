using CK.Core;
using CKli.Core;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace CKli.HotZone.Plugin;

public sealed class HotZonePlugin : PrimaryPluginBase
{
    /// <summary>
    /// This is a primary plugin. <see cref="PrimaryPluginBase.PrimaryPluginContext"/>
    /// is always available (as well as the <see cref="PluginBase.World"/>).
    /// </summary>
    public HotZonePlugin( PrimaryPluginContext primaryContext )
        : base( primaryContext )
    {
        primaryContext.World.Events.PluginInfo += e =>
        {
            Throw.CheckState( PrimaryPluginContext.PluginInfo.FullPluginName == "CKli.HotZone.Plugin" );
            Throw.CheckState( World == e.World );
            Throw.CheckState( PrimaryPluginContext.World == e.World );
            e.AddMessage( PrimaryPluginContext, e.ScreenType.Text( "Message from 'HotZone' plugin." ) );
            e.Monitor.Info( $"New 'HotZone' in world '{e.World.Name}' plugin certainly requires some development." );
            Console.WriteLine( $"Hello from 'HotZone' plugin." );
        };
    }
}
