//using CK.Core;
//using CK.Testing;
//using CKli.Core;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;
//using System.Xml.Linq;
//using static CK.Testing.MonitorTestHelper;

//namespace CKli;

//static partial class TestEnv
//{
//    /// <summary>
//    /// Models a "Remotes/" folder that contains a stack repository and code repositories for the stack.
//    /// </summary>
//    public sealed partial class RemotesCollection
//    {
//        readonly string _fullName;
//        readonly string[] _repositoryNames;
//        readonly Uri _stackUri;
//        readonly string _stackName;

//        internal RemotesCollection( string fullName, string[] repositoryNames )
//        {
//            _fullName = fullName;
//            _repositoryNames = repositoryNames;
//            int idx = fullName.IndexOf( '(' );
//            Throw.DebugAssert( idx != 0 );
//            _stackName = idx < 0 ? fullName : fullName.Substring( 0, idx );
//            _stackUri = GetUriFor( _stackName + "-Stack" );
//        }

//        /// <summary>
//        /// Gets the full name (with the optional parentheses).
//        /// </summary>
//        public string FullName => _fullName;

//        /// <summary>
//        /// Gets the stack name. There must be a "StackName-Stack" repository folder that
//        /// contains, at least the "StackName.xml" default world definition file in it.
//        /// </summary>
//        public string StackName => _stackName;

//        /// <summary>
//        /// Gets the Url of the remote Stack repository (in the "Remotes/bare/" folder)..
//        /// </summary>
//        public Uri StackUri => _stackUri;

//        /// <summary>
//        /// Gets all the repository names (including the "StackName-Stack").
//        /// </summary>
//        public IReadOnlyList<string> Repositories => _repositoryNames;

//        /// <summary>
//        /// Gets the Url for one of the <see cref="Repositories"/>.
//        /// <para>
//        /// When missing, a fake url "file:///Missing..." is returned that will trigger an error is used.
//        /// </para>
//        /// </summary>
//        /// <param name="repositoryName">The repository name that should belong to the <see cref="Repositories"/>.</param>
//        /// <returns>The url for the remote repository (in the "Remotes/bare/" folder).</returns>
//        public Uri GetUriFor( string repositoryName )
//        {
//            if( _repositoryNames.Contains( repositoryName ) )
//            {
//                return new Uri( _barePath.AppendPart( _fullName ).AppendPart( repositoryName ) );
//            }
//            return new Uri( "file:///Missing '" + repositoryName + "' repository in '" + _fullName + "' remotes" );
//        }

//        /// <summary>
//        /// <para>
//        /// Returns a <see cref="CKliEnv"/> context for the default World of this RemotesCollection.
//        /// </para>
//        /// Clones these <see cref="Repositories"/> in the <paramref name="clonedFolder"/>'s <see cref="CKliEnv.CurrentDirectory"/>
//        /// that must be inside the <see cref="_clonedPath"/> and configures its default World plugins with the host's stack's default
//        /// World plugins configuration.
//        /// </summary>
//        /// <param name="clonedFolder">The cloned folder of the unit test.</param>
//        /// <param name="pluginConfigurationEditor">Optional plugin configuration editor.</param>
//        /// <returns>The default world cloned context.</returns>
//        public CKliEnv Clone( NormalizedPath clonedFolder, Action<IActivityMonitor, XElement>? pluginConfigurationEditor = null, bool privateStack = false )
//        {
//            Throw.CheckArgument( clonedFolder.StartsWith( _clonedPath ) );

//            var context = new CKliEnv( clonedFolder, screen: new StringScreen(), findCurrentStackPath: false );
//            CloneOrThrow( context, _stackUri, _stackName, privateStack );
//            context = context.ChangeDirectory( _stackName );
//            if( !StackRepository.OpenWorldFromPath( TestHelper.Monitor,
//                                        context,
//                                        out var stack,
//                                        out var world,
//                                        skipPullStack: true,
//                                        withPlugins: false ) )
//            {
//                Throw.CKException( $"Unable to open default World of cloned test Stack from '{context.CurrentDirectory}'." );
//            }

//            try
//            {
//                // Injects Plugins configuration of the host world into the world definition of the world to test.
//                world.DefinitionFile.RawEditPlugins( TestHelper.Monitor, ( monitor, plugins ) =>
//                {
//                    plugins.RemoveAll();
//                    plugins.Add( _hostPluginsConfiguration.Attributes() );
//                    plugins.Add( _hostPluginsConfiguration.Nodes() );
//                    pluginConfigurationEditor?.Invoke( monitor, plugins );
//                } );
//                world.DefinitionFile.SaveFile( TestHelper.Monitor );
//                stack.Commit( TestHelper.Monitor, "Updated <Plugins> configuration." );
//            }
//            finally
//            {
//                stack.Dispose();
//            }
//            return context;


//            static void CloneOrThrow( CKliEnv context, Uri stackUri, string stackName, bool privateStack )
//            {
//                using( var stack = StackRepository.Clone( TestHelper.Monitor,
//                                                          context,
//                                                          stackUri,
//                                                          !privateStack,
//                                                          allowDuplicateStack: false,
//                                                          ignoreParentStack: true ) )
//                {
//                    if( stack == null )
//                    {
//                        Throw.CKException( $"Unable to open test stack '{stackName}' from '{stackUri}'." );
//                    }
//                }
//            }
//        }



//        public override string ToString() => $"{_fullName} - {_repositoryNames.Length} repositories";
//    }

//}
