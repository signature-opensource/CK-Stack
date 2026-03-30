using CK.Core;
using System.Threading.Tasks;

namespace CKli.Publish.Plugin;

public sealed partial class NuGetFeedClient
{
    /// <summary>
    /// Adapts an <see cref="IActivityLineEmitter"/> to the NuGet <see cref="NuGet.Common.ILogger"/> interface.
    /// </summary>
    sealed class LoggerAdapter : NuGet.Common.ILogger
    {
        readonly IActivityLineEmitter _logger;

        public LoggerAdapter( IActivityLineEmitter logger ) => _logger = logger;

        public void LogDebug( string data ) => _logger.Trace( data );
        public void LogVerbose( string data ) => _logger.Trace( data );
        public void LogMinimal( string data ) => _logger.Info( data );
        public void LogInformation( string data ) => _logger.Info( data );
        public void LogInformationSummary( string data ) => _logger.Info( data );
        public void LogWarning( string data ) => _logger.Warn( data );
        public void LogError( string data ) => _logger.Error( data );

        public void Log( NuGet.Common.LogLevel level, string data )
        {
            switch( level )
            {
                case NuGet.Common.LogLevel.Debug:
                case NuGet.Common.LogLevel.Verbose: _logger.Trace( data ); break;
                case NuGet.Common.LogLevel.Minimal:
                case NuGet.Common.LogLevel.Information: _logger.Info( data ); break;
                case NuGet.Common.LogLevel.Warning: _logger.Warn( data ); break;
                case NuGet.Common.LogLevel.Error: _logger.Error( data ); break;
            }
        }

        public Task LogAsync( NuGet.Common.LogLevel level, string data )
        {
            Log( level, data );
            return Task.CompletedTask;
        }

        public void Log( NuGet.Common.ILogMessage message ) => Log( message.Level, $"{message.Code}: {message.Message}" );

        public Task LogAsync( NuGet.Common.ILogMessage message )
        {
            Log( message );
            return Task.CompletedTask;
        }
    }
}
