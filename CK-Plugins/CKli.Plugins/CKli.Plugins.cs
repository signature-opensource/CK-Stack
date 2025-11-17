using CKli.Core;

namespace CKli.Plugins;

public static class Plugins
{
    public static IPluginFactory Register( PluginCollectorContext ctx )
    {
        return PluginCollector.Create( ctx ).BuildPluginFactory( [
            // <AutoSection>
            typeof( VSSolution.Plugin.VSSolutionPlugin ),
             // </AutoSection>
        ] );
    }
}
                