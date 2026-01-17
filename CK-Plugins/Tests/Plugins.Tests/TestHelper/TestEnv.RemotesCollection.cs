using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CKli;

static partial class TestEnv
{
    /// <summary>
    /// Models a "Remotes/" folder that contains a stack repository and code repositories for the stack.
    /// </summary>
    public sealed partial class RemotesCollection
    {
        readonly string _fullName;
        readonly string[] _repositoryNames;
        readonly Uri _stackUri;
        readonly string _stackName;

        internal RemotesCollection( string fullName, string[] repositoryNames )
        {
            _fullName = fullName;
            _repositoryNames = repositoryNames;
            int idx = fullName.IndexOf( '(' );
            Throw.DebugAssert( idx != 0 );
            _stackName = idx < 0 ? fullName : fullName.Substring( 0, idx );
            _stackUri = GetUriFor( _stackName + "-Stack" );
        }

        /// <summary>
        /// Gets the full name (with the optional parentheses).
        /// </summary>
        public string FullName => _fullName;

        /// <summary>
        /// Gets the stack name. There must be a "StackName-Stack" repository folder that
        /// contains, at least the "StackName.xml" default world definition file in it.
        /// </summary>
        public string StackName => _stackName;

        /// <summary>
        /// Gets the Url of the remote Stack repository (in the "Remotes/bare/" folder)..
        /// </summary>
        public Uri StackUri => _stackUri;

        /// <summary>
        /// Gets all the repository names (including the "StackName-Stack").
        /// </summary>
        public IReadOnlyList<string> Repositories => _repositoryNames;

        /// <summary>
        /// Gets the Url for one of the <see cref="Repositories"/>.
        /// <para>
        /// When missing, a fake url "file:///Missing..." is returned that will trigger an error is used.
        /// </para>
        /// </summary>
        /// <param name="repositoryName">The repository name that should belong to the <see cref="Repositories"/>.</param>
        /// <returns>The url for the remote repository (in the "Remotes/bare/" folder).</returns>
        public Uri GetUriFor( string repositoryName )
        {
            if( _repositoryNames.Contains( repositoryName ) )
            {
                return new Uri( _barePath.AppendPart( _fullName ).AppendPart( repositoryName ) );
            }
            return new Uri( "file:///Missing '" + repositoryName + "' repository in '" + _fullName + "' remotes" );
        }

        public override string ToString() => $"{_fullName} - {_repositoryNames.Length} repositories";
    }

}
