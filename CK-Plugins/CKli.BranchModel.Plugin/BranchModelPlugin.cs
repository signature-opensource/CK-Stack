using CK.Core;
using CKli.Core;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace CKli.BranchModel.Plugin;

public sealed class BranchModelPlugin : PluginBase
{
    /// <summary>
    /// This is a primary plugin.
    /// </summary>
    public BranchModelPlugin( PrimaryPluginContext primaryContext )
        : base( primaryContext )
    {
        primaryContext.World.Events.PluginInfo += e =>
        {
            Throw.CheckState( PrimaryContext.PluginInfo.FullPluginName == "CKli.BranchModel.Plugin" );
            Throw.CheckState( PrimaryContext.World == e.World );
            e.AddMessage( PrimaryContext, e.ScreenType.Text( "Message from 'BranchModel' plugin." ) );
            e.Monitor.Info( $"New 'BranchModel' in world '{e.World.Name}' plugin certainly requires some development." );
            Console.WriteLine( $"Hello from 'BranchModel' plugin." );
        };
    }
}
