using CK.Core;
using CKli.Core;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace CKli.Publish.Plugin;

public sealed class PublishPlugin : PrimaryPluginBase
{
    /// <summary>
    /// This is a primary plugin. <see cref="PrimaryPluginBase.PrimaryPluginContext"/>
    /// is always available (as well as the <see cref="PluginBase.World"/>).
    /// </summary>
    public PublishPlugin( PrimaryPluginContext primaryContext )
        : base( primaryContext )
    {
        primaryContext.World.Events.PluginInfo += e =>
        {
            Throw.CheckState( PrimaryPluginContext.PluginInfo.FullPluginName == "CKli.Publish.Plugin" );
            Throw.CheckState( World == e.World );
            Throw.CheckState( PrimaryPluginContext.World == e.World );
            e.AddMessage( PrimaryPluginContext, e.ScreenType.Text( "Message from 'Publish' plugin." ) );
            e.Monitor.Info( $"New 'Publish' in world '{e.World.Name}' plugin certainly requires some development." );
            Console.WriteLine( $"Hello from 'Publish' plugin." );
        };
    }
}
