# Remotes
This folder contains the `Remotes.zip` that is committed. It is unzipped automatically locally
and the `.gitignore` prevents the extracted folders (and the `bare/` folder) to be committed.
The `Remotes.zip` is reprocessed when the `bare/Remotes.txt` file doesn't exist or if last write time
is not the same as the last write time of the `Remotes.zip` file.

**Note:** From NUnitTestAdapter v6.0.0, the remotes must not contain any unit tests as they are discovered
even if the `Cloned` and `Remotes` folders are excluded from compilation.

Each root folder contains a Stack repository and one or more repositories that are used by unit tests.

The `Remotes.zip` contains non-bare repos. To simulate a "true" remote, one needs bare repositories
as our test remotes. See these references:
- https://stackoverflow.com/questions/3959924/whats-the-difference-between-git-clone-mirror-and-git-clone-bare
- https://stackoverflow.com/questions/2199897/how-to-convert-a-normal-git-repository-to-a-bare-one

The extracted folders can be edited freely and zipped back to `Remotes.zip` thanks to the (AI generated) `ZipRemotes.zip` 
if needed (but they are not directly used: extracted `bare/` folders are).

When updating the `Remotes.zip`, the `.gitignore` and this `README.md` and the `ZipRemotes.ps1` are not in the Zip archive:
the archive contains the folders with the git repositories.

After the extractions, we create bare repositories in the `bare/` folder by copying the .git folder and setting the `bare`
option to true in the `.git/config` file.
```
[core]
	bare = true
```
The `bare/` repositories are stable. Any unit test can push changes, any following unit test restarts with the original,
unchanged, remote repository: `bare/` repositories are zipped and each call to `RemotesCollection OpenRemotes( string name )` resets
the bare repository by deleting the potentially altered folder and extracting a clean one from its zip archive.

