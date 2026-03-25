using System;

namespace IronConfig.ILog;

/// <summary>
/// Options for ILOG encoding (particularly for AUDITED profile signature generation).
/// </summary>
public class IlogEncodeOptions
{
    /// <summary>
    /// Ed25519 private key (32 bytes) for signing in AUDITED profile.
    /// Required for AUDITED profile, ignored for other profiles.
    /// </summary>
    public ReadOnlyMemory<byte>? Ed25519PrivateKey32 { get; set; }

    /// <summary>
    /// Ed25519 public key (32 bytes) for signature metadata in AUDITED profile.
    /// Required for AUDITED profile, ignored for other profiles.
    /// If not provided and private key is provided, public key may be derived (if supported).
    /// </summary>
    public ReadOnlyMemory<byte>? Ed25519PublicKey32 { get; set; }

    /// <summary>
    /// Validate that options are consistent for the given profile.
    /// Throws InvalidOperationException if required keys are missing for AUDITED profile.
    /// </summary>
    public void ValidateForProfile(IlogProfile profile)
    {
        if (profile == IlogProfile.AUDITED)
        {
            if (!Ed25519PrivateKey32.HasValue || Ed25519PrivateKey32.Value.Length != 32)
                throw new InvalidOperationException("AUDITED profile requires Ed25519PrivateKey32 (32 bytes)");

            if (!Ed25519PublicKey32.HasValue || Ed25519PublicKey32.Value.Length != 32)
                throw new InvalidOperationException("AUDITED profile requires Ed25519PublicKey32 (32 bytes)");
        }
    }
}

/// <summary>
/// Options for ILOG verification.
/// </summary>
public class IlogVerifyOptions
{
    /// <summary>
    /// Ed25519 public key (32 bytes) for signature verification in AUDITED profile.
    /// Required for verifying AUDITED profile signatures.
    /// </summary>
    public ReadOnlyMemory<byte>? Ed25519PublicKey32 { get; set; }

    /// <summary>
    /// Validate that options are consistent for verification.
    /// </summary>
    public void ValidateForAuditedVerification()
    {
        if (!Ed25519PublicKey32.HasValue || Ed25519PublicKey32.Value.Length != 32)
            throw new InvalidOperationException("AUDITED profile verification requires Ed25519PublicKey32 (32 bytes)");
    }
}
