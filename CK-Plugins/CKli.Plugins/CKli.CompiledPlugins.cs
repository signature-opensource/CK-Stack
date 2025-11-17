using CKli.Core;
using CK.Core;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
        
namespace CKli.Plugins;

public static class CompiledPlugins
{
    static ReadOnlySpan<byte> _configSignature => [120,88,114,211,186,10,30,241,43,205,178,172,238,9,184,164,50,105,84,113,    ];

    public static IPluginFactory? Get( PluginCollectorContext ctx )
    {
        if( !_configSignature.SequenceEqual( ctx.Signature ) ) return null;
        var infos = new PluginInfo[]{
            new PluginInfo( "CKli.VSSolution.Plugin", "VSSolution", (PluginStatus)0, new IPluginTypeInfo[1] ),
            new PluginInfo( "CKli.BranchModel.Plugin", "BranchModel", (PluginStatus)0, new IPluginTypeInfo[1] ),
        };
        PluginInfo plugin;
        IPluginTypeInfo[] types;
        plugin = infos[0];
        types = (IPluginTypeInfo[])plugin.PluginTypes;
        types[0] = new PluginTypeInfo( plugin, "CKli.VSSolution.Plugin.VSSolutionPlugin", true, 0, 0 );
        plugin = infos[1];
        types = (IPluginTypeInfo[])plugin.PluginTypes;
        types[0] = new PluginTypeInfo( plugin, "CKli.BranchModel.Plugin.BranchModelPlugin", true, 0, 1 );
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
        var objects = new object[2];
        objects[0] = new CKli.VSSolution.Plugin.VSSolutionPlugin( new PrimaryPluginContext( _plugins[0], configs, world ) );
        objects[1] = new CKli.BranchModel.Plugin.BranchModelPlugin( new PrimaryPluginContext( _plugins[1], configs, world ) );
        return PluginCollection.CreateAndBindCommands( objects, _plugins, _commands, _pluginCommands );
    }

    public void Dispose() { }
}
