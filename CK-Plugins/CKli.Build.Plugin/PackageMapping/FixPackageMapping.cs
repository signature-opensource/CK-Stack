using CSemVer;

namespace CKli.Build.Plugin;

sealed class FixPackageMapping : IPackageMapping
{
    readonly PackageMapper _mapper;

    public FixPackageMapping( PackageMapper mapper )
    {
        _mapper = mapper;
    }

    /// <inheritdoc />
    public bool IsEmpty => _mapper.IsEmpty;

    /// <inheritdoc/>
    public bool HasMapping( string packageId ) => _mapper.HasMapping( packageId );

    public SVersion? GetMappedVersion( string packageId, SVersion from )
    {
        var v = _mapper.GetMappedVersion( packageId, from );
        if( v == null && from.IsPrerelease && from.Patch > 0 )
        {
            var fromSource = SVersion.Create( from.Major, from.Minor, from.Patch - 1 );
            return _mapper.GetMappedVersion( packageId, fromSource );
        }
        return v;
    }

}
