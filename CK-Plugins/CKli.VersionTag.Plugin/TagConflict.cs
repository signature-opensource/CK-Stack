namespace CKli.VersionTag.Plugin;

enum TagConflict
{
    None,
    DuplicateInvalidTag,
    InvalidTagOnWrongCommit,
    SameVersionOnDifferentCommit,
    DuplicatedVersionTag
}
