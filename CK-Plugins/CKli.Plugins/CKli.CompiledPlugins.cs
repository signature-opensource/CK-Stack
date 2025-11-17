using CKli.Core;
using CK.Core;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
        
namespace CKli.Plugins;

public static class CompiledPlugins
{
    static ReadOnlySpan<byte> _configSignature => [218,57,163,238,94,107,75,13,50,85,191,239,149,96,24,144,175,216,7,9,    ];

    public static IPluginFactory? Get( PluginCollectorContext ctx )
    {
        if( !_configSignature.SequenceEqual( ctx.Signature ) ) return null;
        var infos = new PluginInfo[]{
        };
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
        var objects = new object[0];
        return PluginCollection.CreateAndBindCommands( objects, _plugins, _commands, _pluginCommands );
    }

    public void Dispose() { }
}
