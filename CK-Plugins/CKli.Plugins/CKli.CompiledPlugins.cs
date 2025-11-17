using CKli.Core;
using CK.Core;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
        
namespace CKli.Plugins;

public static class CompiledPlugins
{
    static ReadOnlySpan<byte> _configSignature => [213,181,62,32,194,124,136,225,169,247,177,3,123,203,244,78,210,106,95,85,    ];

    public static IPluginFactory? Get( PluginCollectorContext ctx )
    {
        if( !_configSignature.SequenceEqual( ctx.Signature ) ) return null;
        var infos = new PluginInfo[]{
            new PluginInfo( "CKli.VSSolution.Plugin", "VSSolution", (PluginStatus)0, new IPluginTypeInfo[1] ),
        };
        PluginInfo plugin;
        IPluginTypeInfo[] types;
        plugin = infos[0];
        types = (IPluginTypeInfo[])plugin.PluginTypes;
        types[0] = new PluginTypeInfo( plugin, "CKli.VSSolution.Plugin.VSSolutionPlugin", true, 0, 0 );
        var pluginCommands = new PluginCommand[]{
        };
        var commandBuilder = new CommandNamespaceBuilder();
        foreach( var c in pluginCommands )
        {
            commandBuilder.Add( c );
        }
        return new Generated( infos, pluginCommands, commandBuilder.Build() );
    }
}

sealed class Generated : IPluginFactory
{
    readonly PluginInfo[] _plugins;
    readonly PluginCommand[] _pluginCommands;
    readonly CommandNamespace _commands;

    internal Generated( PluginInfo[] plugins, PluginCommand[] pluginCommands, CommandNamespace commands )
    {
        _plugins = plugins;
        _pluginCommands = pluginCommands;
        _commands = commands;
    }
    
#if DEBUG
    public PluginCompilationMode CompilationMode => PluginCompilationMode.Debug;
#else
    public PluginCompilationMode CompilationMode => PluginCompilationMode.Release;
#endif

    public string GenerateCode() => throw new InvalidOperationException( "CompilationMode is not PluginCompilationMode.None" );

    public IPluginCollection Create( IActivityMonitor monitor, World world )
    {
        var configs = world.DefinitionFile.ReadPluginsConfiguration( monitor );
        Throw.CheckState( "Plugins configurations have already been loaded.", configs != null );
        var objects = new object[1];
        objects[0] = new CKli.VSSolution.Plugin.VSSolutionPlugin( new PrimaryPluginContext( _plugins[0], configs, world ) );
        return PluginCollection.CreateAndBindCommands( objects, _plugins, _commands, _pluginCommands );
    }

    public void Dispose() { }
}
