using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Read only model of a solution as a consumer/producer of packages.
/// <para>
/// This can only obtained for a <see cref="GitBranch"/> in a <see cref="Repo"/> and only
/// exposes the <see cref="Projects"/> and the <see cref="Consumed"/> packages.
/// </para>
/// </summary>
[DebuggerDisplay( "{ToString(),nq}" )]
public sealed class GitSolution
{
    readonly Repo _repo;
    readonly Branch _branch;
    readonly HashSet<PackageInstance> _consumed;
    readonly List<Project> _projects;

    /// <summary>
    /// Gets the projects.
    /// </summary>
    public IReadOnlyList<Project> Projects => _projects;

    /// <summary>
    /// Gets all the &lt;PackageReference ... /&gt; that have been found in <see cref="Projects"/> files
    /// and all "Directory.Build.props" (reachable from Projects).
    /// </summary>
    public IReadOnlySet<PackageInstance> Consumed => _consumed;

    /// <summary>
    /// Gets the repository.
    /// </summary>
    public Repo Repo => _repo;

    /// <summary>
    /// Gets the Branch from which this solution has been read.
    /// </summary>
    public Branch GitBranch => _branch;

    /// <summary>
    /// Minimal project file.
    /// </summary>
    public sealed class Project
    {
        readonly NormalizedPath _path;
        readonly string _name;
        readonly XElement _root;
        bool _packableKnown;
        bool? _isPackable;

        internal Project( NormalizedPath path, XElement root )
        {
            _path = path;
            _name = path.LastPart[0..^7];
            _root = root;
        }

        /// <summary>
        /// Gets the path to the ".csproj" file in the solution.
        /// </summary>
        public NormalizedPath Path => _path;

        /// <summary>
        /// Gets the project name.
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// Gets whether this project is packable: its <see cref="Name"/> is the produced package name.
        /// <para>
        /// This is null when no &lt;IsPackable&gt; element can be found.
        /// </para>
        /// </summary>
        public bool? IsPackable
        {
            get
            {
                if( !_packableKnown )
                {
                    _packableKnown = true;
                    _isPackable = (bool?)_root.Elements( "PropertyGroup" )
                                              .SelectMany( g => g.Elements( "IsPackable" ) )
                                              .FirstOrDefault();
                }
                return _isPackable; 
            }
        }

    }

    internal GitSolution( Repo repo, Branch branch )
    {
        _repo = repo;
        _branch = branch;
        _consumed = new HashSet<PackageInstance>();
        _projects = new List<Project>();
    }

    internal bool AddProjectFile( IActivityMonitor monitor, NormalizedPath path, XElement project )
    {
        if( path.LastPart.EndsWith( ".csproj", StringComparison.OrdinalIgnoreCase ) )
        {
            _projects.Add( new Project( path, project ) );
            if( !HandlePackageReferences( monitor, path, project ) )
            {
                return false;
            }
        }
        else if( path.LastPart.Equals( "Directory.Build.props", StringComparison.OrdinalIgnoreCase ) )
        {
            if( !HandlePackageReferences( monitor, path, project ) )
            {
                return false;
            }
        }
        else
        {
            Throw.DebugAssert( path.LastPart.Equals( "Directory.Package.props", StringComparison.OrdinalIgnoreCase ) );
            foreach( var e in project.Descendants( "PackageVersion" ) )
            {
                var packageId = CommonSolution.GetIncludedName( monitor, path, e, CK.Core.LogLevel.Error );
                if( packageId == null
                    || !CommonSolution.ReadVersionAttribute( monitor, path, e, "Version", "Version", out var _, out var version ) )
                {
                    return false;
                }
                Throw.DebugAssert( version != null );
                _consumed.Add( new PackageInstance( packageId, version ) );
            }
        }
        return true;

        bool HandlePackageReferences( IActivityMonitor monitor, NormalizedPath path, XElement project )
        {
            foreach( var e in project.Descendants( "PackageReference" ) )
            {
                var packageId = CommonSolution.GetIncludedName( monitor, path, e, CK.Core.LogLevel.Error );
                if( packageId == null )
                {
                    return false;
                }
                if( !CommonSolution.ReadVersionAttribute( monitor, path, e, "VersionOverride", null, out var _, out var version )
                    || (version == null
                        && !CommonSolution.ReadVersionAttribute( monitor, path, e, "Version", null, out var _, out version )) )
                {
                    return false;
                }
                if( version != null )
                {
                    _consumed.Add( new PackageInstance( packageId, version ) );
                }
            }
            return true;
        }
    }

    public override string ToString() => $"{_repo.DisplayPath}/branch/{_branch.FriendlyName}";
}

