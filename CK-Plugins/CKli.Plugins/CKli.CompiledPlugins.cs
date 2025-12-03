using CKli.Core;
using CK.Core;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
        
namespace CKli.Plugins;

public static class CompiledPlugins
{
    static ReadOnlySpan<byte> _configSignature => [25,185,97,238,57,196,244,38,55,211,112,227,118,131,98,89,219,119,72,229,    ];

    public static IPluginFactory? Get( PluginCollectorContext ctx )
    {
        if( !_configSignature.SequenceEqual( ctx.Signature ) ) return null;
        var infos = new PluginInfo[]{
            new PluginInfo( "CKli.BranchModel.Plugin", "BranchModel", (PluginStatus)0, null, new IPluginTypeInfo[1] ),
            new PluginInfo( "CKli.VersionTag.Plugin", "VersionTag", (PluginStatus)0, null, new IPluginTypeInfo[1] ),
            new PluginInfo( "CKli.Build.Plugin", "Build", (PluginStatus)0, null, new IPluginTypeInfo[2] ),
            new PluginInfo( "CKli.ReleaseDatabase.Plugin", "ReleaseDatabase", (PluginStatus)0, null, new IPluginTypeInfo[0] ),
            new PluginInfo( "CKli.ArtifactHandler.Plugin", "ArtifactHandler", (PluginStatus)0, null, new IPluginTypeInfo[1] ),
        };
        PluginInfo plugin;
        IPluginTypeInfo[] types;
        plugin = infos[0];
        types = (IPluginTypeInfo[])plugin.PluginTypes;
        types[0] = new PluginTypeInfo( plugin, "CKli.BranchModel.Plugin.BranchModelPlugin", true, 0, 1 );
        plugin = infos[1];
        types = (IPluginTypeInfo[])plugin.PluginTypes;
        types[0] = new PluginTypeInfo( plugin, "CKli.VersionTag.Plugin.VersionTagPlugin", true, 0, 0 );
        plugin = infos[2];
        types = (IPluginTypeInfo[])plugin.PluginTypes;
        types[0] = new PluginTypeInfo( plugin, "CKli.Build.Plugin.RepositoryBuilderPlugin", true, 0, 3 );
        types[1] = new PluginTypeInfo( plugin, "CKli.Build.Plugin.BuildPlugin", true, 0, 4 );
        plugin = infos[3];
        types = (IPluginTypeInfo[])plugin.PluginTypes;
        plugin = infos[4];
        types = (IPluginTypeInfo[])plugin.PluginTypes;
        types[0] = new PluginTypeInfo( plugin, "CKli.ArtifactHandler.Plugin.ArtifactHandlerPlugin", true, 0, 2 );
        var pluginCommands = new PluginCommand[]{
            new Cmd_branch＿fix( infos[0].PluginTypes[0] ),
            new Cmd_repo＿build( infos[2].PluginTypes[1] ),
            new Cmd_repo＿rebuild＿old( infos[2].PluginTypes[1] ),
            new Cmd_repo＿rebuild＿version( infos[2].PluginTypes[1] ),
        };
        var cmds = new Dictionary<string,Command?>();
        foreach( var c in pluginCommands )
        {
            cmds.Add( c.CommandPath, c );
        }
       cmds.Add( "branch", null );
       cmds.Add( "repo", null );
       cmds.Add( "repo rebuild", null );
        return new Generated( infos, pluginCommands, CommandNamespace.UnsafeCreate( cmds ) );
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
    public PluginCompileMode CompileMode => PluginCompileMode.Debug;
#else
    public PluginCompileMode CompileMode => PluginCompileMode.Release;
#endif

    public string GenerateCode() => throw new InvalidOperationException( "CompileMode is not PluginCompileMode.None" );

    public PluginCollection Create( IActivityMonitor monitor, World world )
    {
        var configs = world.DefinitionFile.ReadPluginsConfiguration( monitor );
        Throw.CheckState( "Plugins configurations have already been loaded.", configs != null );
        var objects = new object[5];
        objects[0] = new CKli.VersionTag.Plugin.VersionTagPlugin( new PrimaryPluginContext( _plugins[1], configs, world ) );
        objects[1] = new CKli.BranchModel.Plugin.BranchModelPlugin( new PrimaryPluginContext( _plugins[0], configs, world ), (CKli.VersionTag.Plugin.VersionTagPlugin)objects[0] );
        objects[2] = new CKli.ArtifactHandler.Plugin.ArtifactHandlerPlugin( new PrimaryPluginContext( _plugins[4], configs, world ) );
        objects[3] = new CKli.Build.Plugin.RepositoryBuilderPlugin( new PrimaryPluginContext( _plugins[2], configs, world ), (CKli.ArtifactHandler.Plugin.ArtifactHandlerPlugin)objects[2] );
        objects[4] = new CKli.Build.Plugin.BuildPlugin( new PrimaryPluginContext( _plugins[2], configs, world ), (CKli.VersionTag.Plugin.VersionTagPlugin)objects[0], (CKli.BranchModel.Plugin.BranchModelPlugin)objects[1], (CKli.Build.Plugin.RepositoryBuilderPlugin)objects[3], (CKli.ArtifactHandler.Plugin.ArtifactHandlerPlugin)objects[2] );
        return PluginCollectionImpl.CreateAndBindCommands( objects, _plugins, _commands, _pluginCommands );
    }

    public void Dispose() { }
}
sealed class Cmd_branch＿fix : PluginCommand
{
    internal Cmd_branch＿fix( IPluginTypeInfo typeInfo )
        : base( typeInfo,
                "branch fix",
                "Ensures that a 'vMajor.Minor/fix' branch exists in the repository and checkouts it.",
                true,
                arguments: [
                    ("version", "The Major or Major.Minor for which a fix must be produced."),
                ],
                options: [
                ],
                flags: [
                    (["--no-fetch",], "Don't initially fetch 'origin' repository." ),
                    (["--move-branch",], "<no description>" ),
                ],
                "BranchFix", MethodAsyncReturn.None ) {}
    protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor, CKliEnv context, CommandLineArguments cmdLine )
    {
        var a0 = cmdLine.EatArgument();
        var f0 = cmdLine.EatFlag( Flags[0].Names );
        var f1 = cmdLine.EatFlag( Flags[1].Names );
        if( !cmdLine.Close( monitor ) ) return ValueTask.FromResult( false );
        return ValueTask.FromResult( ((CKli.BranchModel.Plugin.BranchModelPlugin)Instance).BranchFix(
                                           monitor, context, a0, f0, f1 ) );
    }
}
sealed class Cmd_repo＿build : PluginCommand
{
    internal Cmd_repo＿build( IPluginTypeInfo typeInfo )
        : base( typeInfo,
                "repo build",
                "Build-Test-Package and propagates the current Repo/branch if needed.",
                true,
                arguments: [
                ],
                options: [
                    (["--branch",], "Specify the branch to build. By default, the current head is considered.", false ),
                ],
                flags: [
                    (["--skip-tests",], "Don't run tests even if they have never locally run on this commit." ),
                    (["--force-tests",], "Run tests even if they have already run successfully on this commit." ),
                    (["--rebuild",], "Build even if a version tag exists and its artifacts locally found." ),
                ],
                "RepoBuild", MethodAsyncReturn.None ) {}
    protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor, CKliEnv context, CommandLineArguments cmdLine )
    {
        var o0 = cmdLine.EatSingleOption( Options[0].Names );
        var f0 = cmdLine.EatFlag( Flags[0].Names );
        var f1 = cmdLine.EatFlag( Flags[1].Names );
        var f2 = cmdLine.EatFlag( Flags[2].Names );
        if( !cmdLine.Close( monitor ) ) return ValueTask.FromResult( false );
        return ValueTask.FromResult( ((CKli.Build.Plugin.BuildPlugin)Instance).RepoBuild(
                                           monitor, context, o0, f0, f1, f2 ) );
    }
}
sealed class Cmd_repo＿rebuild＿old : PluginCommand
{
    internal Cmd_repo＿rebuild＿old( IPluginTypeInfo typeInfo )
        : base( typeInfo,
                "repo rebuild old",
                "Tries to rebuild the oldest releases until a success.\r\nFailing commits are tagged with a '+Deprecated' tag.",
                true,
                arguments: [
                ],
                options: [
                ],
                flags: [
                    (["--warn-only",], "Warns only: doesn't create a 'Deprecated' tag on the failing commit." ),
                    (["--run-test",], "Runs unit tests. They must be successful." ),
                    (["--all",], "Consider all the Repos of the current World (even if current path is in a Repo)." ),
                ],
                "RebuildOld", MethodAsyncReturn.None ) {}
    protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor, CKliEnv context, CommandLineArguments cmdLine )
    {
        var f0 = cmdLine.EatFlag( Flags[0].Names );
        var f1 = cmdLine.EatFlag( Flags[1].Names );
        var f2 = cmdLine.EatFlag( Flags[2].Names );
        if( !cmdLine.Close( monitor ) ) return ValueTask.FromResult( false );
        return ValueTask.FromResult( ((CKli.Build.Plugin.BuildPlugin)Instance).RebuildOld(
                                           monitor, context, f0, f1, f2 ) );
    }
}
sealed class Cmd_repo＿rebuild＿version : PluginCommand
{
    internal Cmd_repo＿rebuild＿version( IPluginTypeInfo typeInfo )
        : base( typeInfo,
                "repo rebuild version",
                "Rebuild the specified version in the current repository.",
                true,
                arguments: [
                    ("version", "The version to rebuild."),
                ],
                options: [
                ],
                flags: [
                    (["--skip-tests",], "Don't run tests even if they have never locally run on this commit." ),
                    (["--force-tests",], "Run tests even if they have already run successfully on this commit." ),
                ],
                "RebuildVersion", MethodAsyncReturn.None ) {}
    protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor, CKliEnv context, CommandLineArguments cmdLine )
    {
        var a0 = cmdLine.EatArgument();
        var f0 = cmdLine.EatFlag( Flags[0].Names );
        var f1 = cmdLine.EatFlag( Flags[1].Names );
        if( !cmdLine.Close( monitor ) ) return ValueTask.FromResult( false );
        return ValueTask.FromResult( ((CKli.Build.Plugin.BuildPlugin)Instance).RebuildVersion(
                                           monitor, context, a0, f0, f1 ) );
    }
}
