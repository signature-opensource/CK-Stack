using CK.Core;
using System.Threading.Tasks;

namespace CKli.Publish.Plugin;

public sealed partial class NuGetFeedClient
{
    /// <summary>
    /// Adapts an <see cref="IActivityMonitor"/> to the NuGet <see cref="NuGet.Common.ILogger"/> interface.
    /// Not thread-safe: the wrapped <see cref="IActivityMonitor"/> must not be used concurrently.
    /// </summary>
    sealed class LoggerAdapter : NuGet.Common.ILogger
    {
        readonly IActivityMonitor _monitor;

        public LoggerAdapter( IActivityMonitor monitor ) => _monitor = monitor;

        public void LogDebug( string data ) => _monitor.Trace( data );
        public void LogVerbose( string data ) => _monitor.Trace( data );
        public void LogMinimal( string data ) => _monitor.Info( data );
        public void LogInformation( string data ) => _monitor.Info( data );
        public void LogInformationSummary( string data ) => _monitor.Info( data );
        public void LogWarning( string data ) => _monitor.Warn( data );
        public void LogError( string data ) => _monitor.Error( data );

        public void Log( NuGet.Common.LogLevel level, string data )
        {
            switch( level )
            {
                case NuGet.Common.LogLevel.Debug:
                case NuGet.Common.LogLevel.Verbose: _monitor.Trace( data ); break;
                case NuGet.Common.LogLevel.Minimal:
                case NuGet.Common.LogLevel.Information: _monitor.Info( data ); break;
                case NuGet.Common.LogLevel.Warning: _monitor.Warn( data ); break;
                case NuGet.Common.LogLevel.Error: _monitor.Error( data ); break;
            }
        }

        public Task LogAsync( NuGet.Common.LogLevel level, string data )
        {
            Log( level, data );
            return Task.CompletedTask;
        }

        public void Log( NuGet.Common.ILogMessage message ) => Log( message.Level, message.Message );

        public Task LogAsync( NuGet.Common.ILogMessage message )
        {
            Log( message );
            return Task.CompletedTask;
        }
    }
}
