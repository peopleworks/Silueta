namespace Silueta.Core;

/// <summary>A writer could not acquire the vault lock, or would lose assignments already on disk.
/// Reload the vault and repeat the complete operation before emitting redacted output.</summary>
public sealed class VaultWriteConflictException : InvalidOperationException
{
    public VaultWriteConflictException() : base(
        "The vault is in use or this write would discard existing assignments. Reload the vault and " +
        "repeat the complete operation before writing any redacted output.") { }
}
