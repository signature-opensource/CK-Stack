using CK.Core;
using Microsoft.Extensions.FileProviders;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Modified .Net <see cref="IFileProvider"/> contract that:
/// <list type="bullet">
///     <item>Uses <see cref="NormalizedPath"/>.</item>
///     <item>Avoids meaningless <see cref="IFileProvider.Watch(string)"/> burden.</item>
///     <item>
///     Avoids the <see cref="IFileInfo.Exists"/> trap by simply returning null instead of
///     the <see cref="NotFoundDirectoryContents.Singleton"/> or useless <see cref="NotFoundFileInfo"/> instances.
///     </item>
/// </list>
/// </summary>
public interface INormalizedFileProvider
{
    /// <summary>
    /// Gets a directory at the given path or null.
    /// </summary>
    /// <param name="sub">The relative path that identifies the directory.</param>
    /// <returns>The contents of the directory or null.</returns>
    IDirectoryContents? GetDirectoryContents( NormalizedPath sub );

    /// <summary>
    /// Locates a file at the given path.
    /// </summary>
    /// <param name="sub">The relative path that identifies the file.</param>
    /// <returns>The file information or null.</returns>
    IFileInfo? GetFileInfo( NormalizedPath sub );
}

