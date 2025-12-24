using CK.Core;
using CKli.Core;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace CKli.ShallowSolution.Plugin;

public sealed class ShallowSolutionPlugin : PrimaryPluginBase
{
    /// <summary>
    /// This is a primary plugin. <see cref="PrimaryPluginBase.PrimaryPluginContext"/>
    /// is always available (as well as the <see cref="PluginBase.World"/>).
    /// </summary>
    public ShallowSolutionPlugin( PrimaryPluginContext primaryContext )
        : base( primaryContext )
    {
        primaryContext.World.Events.PluginInfo += e =>
        {
            Throw.CheckState( PrimaryPluginContext.PluginInfo.FullPluginName == "CKli.ShallowSolution.Plugin" );
            Throw.CheckState( World == e.World );
            Throw.CheckState( PrimaryPluginContext.World == e.World );
            e.AddMessage( PrimaryPluginContext, e.ScreenType.Text( "Message from 'ShallowSolution' plugin." ) );
            e.Monitor.Info( $"New 'ShallowSolution' in world '{e.World.Name}' plugin certainly requires some development." );
            Console.WriteLine( $"Hello from 'ShallowSolution' plugin." );
        };
    }
}
