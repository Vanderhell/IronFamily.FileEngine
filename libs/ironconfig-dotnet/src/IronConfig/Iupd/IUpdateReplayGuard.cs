namespace IronConfig.Iupd;

/// <summary>
/// Replay protection guard for UpdateSequence validation.
/// Tracks the last accepted sequence number per manifest to prevent downgrade attacks.
/// </summary>
public interface IUpdateReplayGuard
{
    /// <summary>
    /// Get the last accepted sequence number for this manifest.
    /// Returns false if no sequence has been accepted yet.
    /// </summary>
    bool TryGetLastAccepted(out ulong last);

    /// <summary>
    /// Record the new accepted sequence number.
    /// Must be called only after validation passes.
    /// </summary>
    void SetLastAccepted(ulong seq);
}
