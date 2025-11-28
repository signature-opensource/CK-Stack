using CKli.Core;

namespace CKli.Plugins;

public static class Plugins
{
    public static IPluginFactory Register( PluginCollectorContext ctx )
    {
        return PluginCollector.Create( ctx ).BuildPluginFactory( [
            // <AutoSection>
            typeof( BranchModel.Plugin.BranchModelPlugin ),
            typeof( VersionTag.Plugin.VersionTagPlugin ),
             typeof( Build.Plugin.BuildPlugin ),
              typeof( LocalNuGetFeed.Plugin.LocalNuGetFeedPlugin ),
               // </AutoSection>
        ] );
    }
}                
