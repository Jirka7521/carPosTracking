namespace CarPosAPI.Data.Entities;

/// <summary>
/// A temporary, account-less window onto one device's positions: a link that is
/// hard to guess, gated by a separate code, alive only between two instants the
/// creator picked, and revocable at any moment.
///
/// <para>
/// It sits <em>beside</em> <see cref="Access"/> rather than inside it, and that
/// separation is deliberate. An <see cref="Access"/> row answers "what may this
/// account do with this device" and is resolved through
/// <see cref="Services.Authorization.IDeviceAccessAuthorizer"/> for every
/// authenticated request. A share link answers a different question — "may this
/// anonymous visitor see these coordinates, right now" — and is resolved by
/// <see cref="Services.Sharing.IShareViewService"/> along a code path that never
/// takes a user id. Keeping the two apart is what guarantees a share can never be
/// mistaken for an account, or widened into one.
/// </para>
///
/// <para>
/// <b>Two secrets guard it, and both are stored in the clear.</b> The link carries
/// <c>selector.verifier</c>, and the code is a generated string; all three live
/// here readable, so the creator can look a link up again after the one-time
/// reveal has gone.
/// </para>
///
/// <para>
/// <b>That is a deliberate trade, made on 2026-09-12, and it has a cost worth
/// naming.</b> The reasoning for it: this database already holds the position
/// history in the clear, so a dump is a serious disclosure with or without these
/// columns, and the codes are machine-generated rather than chosen by a person, so
/// there is no reused-password risk of the kind that makes hashing account
/// passwords non-negotiable. The cost: a dump is a snapshot, but a live link is
/// ongoing access. An old backup that leaks — a file on a laptop, a forgotten
/// cloud snapshot — now yields working credentials to the running system for any
/// share still inside its window, reachable anonymously through the front door
/// without any further access to the database. Bounded by
/// <c>Sharing:MaxWindowDays</c>, and real.
/// </para>
///
/// <para>
/// Anything that changes this decision back has to change
/// <c>docs/RECORD-OF-PROCESSING.md</c>, <c>docs/DATA-INVENTORY.md</c> and the
/// published privacy policy in <c>FE/src/i18n/locales/{en,cs}/legal.json</c> with
/// it — they describe this storage to data subjects, and a policy that describes
/// the wrong one is worse than the storage choice either way.
/// </para>
///
/// Mapped by <see cref="Configurations.ShareLinkConfiguration"/>.
/// </summary>
public sealed class ShareLink
{
    /// <summary>
    /// Surrogate key, and the value carried in the share token's <c>share</c>
    /// claim. A Guid rather than an int precisely because it travels in a token:
    /// a sequential id would let a holder of one share reason about how many
    /// others exist and when they were made.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Lookup half of the link secret — Base64Url of 16 random bytes, unique.
    ///
    /// The token stays split in two even though both halves are now readable,
    /// because the split is what keeps finding a link an indexed equality probe
    /// rather than a scan: the selector is indexed, the verifier is not.
    /// </summary>
    public string Selector { get; set; } = string.Empty;

    /// <summary>
    /// Authorising half of the link secret — Base64Url of 32 random bytes, stored
    /// in the clear.
    ///
    /// Presented alongside <see cref="Selector"/> as <c>selector.verifier</c> and
    /// compared in constant time. Constant-time comparison is kept even though the
    /// value is no longer a digest: it costs nothing and still denies an attacker
    /// the byte-at-a-time timing signal that would turn a 256-bit search into a
    /// 43-step one.
    /// </summary>
    public string Verifier { get; set; } = string.Empty;

    /// <summary>
    /// The visitor's code, stored in the clear so the creator can look it up
    /// again.
    ///
    /// Server-generated from an unambiguous alphabet
    /// (<see cref="Services.Sharing.IPassphraseGenerator"/>), never chosen by a
    /// person — which is why storing it readable carries none of the reused-password
    /// risk that makes hashing account passwords non-negotiable. Account passwords
    /// are unaffected by this and are still PBKDF2; see
    /// <see cref="Services.Auth.IPasswordHasher"/>.
    /// </summary>
    public string Passphrase { get; set; } = string.Empty;

    /// <summary>
    /// The device being shared — the internal <see cref="Device.Id"/> Guid, never
    /// the MQTT identity.
    /// </summary>
    public Guid DeviceId { get; set; }

    /// <summary>Navigation to the shared device.</summary>
    public Device? Device { get; set; }

    /// <summary>
    /// Who created the link, kept for audit ("who handed this out?").
    ///
    /// <b>Nullable for the same reason as <see cref="Access.GrantedBy"/>:</b> an
    /// account erased under Art. 17 must be unlinkable from records that outlive
    /// it. In practice erasure deletes this account's links outright — nobody else
    /// can administer them — but the column stays nullable so the erasure service
    /// has the option, and so the two sharing tables behave alike.
    /// </summary>
    public int? CreatedByUserId { get; set; }

    /// <summary>
    /// What the visitor sees the tracker called, and the creator's own note about
    /// who the link was for.
    ///
    /// It doubles as a privacy control: the visitor never learns the device's
    /// <see cref="Device.DeviceId"/> (the case-sensitive MQTT topic name) and need
    /// not learn its real <see cref="Device.DisplayName"/> either. Defaults to the
    /// display name when the creator does not set one.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Start of the window (UTC). Before this the link answers "not yet active",
    /// and no position from before it is ever visible.
    /// </summary>
    public DateTime ValidFrom { get; set; }

    /// <summary>
    /// End of the window (UTC). <b>One window, used twice:</b> it bounds when the
    /// link works and, independently enforced, which fixes it can return. That is
    /// what makes "they could only ever see Tuesday afternoon" a property of the
    /// database rather than of the frontend asking nicely.
    /// </summary>
    public DateTime ValidUntil { get; set; }

    /// <summary>How much history the link exposes.</summary>
    public ShareScope Scope { get; set; } = ShareScope.LatestOnly;

    /// <summary>Whether speed travels with each fix. Off by default.</summary>
    public bool IncludeSpeed { get; set; }

    /// <summary>
    /// Whether the battery percentage travels with each fix. Off by default — it
    /// says nothing about location and quite a lot about the vehicle.
    /// </summary>
    public bool IncludeBattery { get; set; }

    /// <summary>
    /// Whether the temperature travels with each fix. Off by default, and chosen
    /// separately from <see cref="IncludeBattery"/>. These were one flag until
    /// 2026-09-14, which meant a creator who only wanted to show that the tracker
    /// still had charge had to disclose the cabin temperature to do it.
    /// </summary>
    public bool IncludeTemperature { get; set; }

    /// <summary>
    /// When the creator revoked the link, or null while it stands.
    ///
    /// Revocation is a soft stamp, like every other deactivation in this schema:
    /// the row survives so "this link existed, and was withdrawn at this time"
    /// stays answerable. Because every request re-reads this column, setting it
    /// stops access on the very next call rather than whenever a token expires.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Consecutive wrong-code attempts since the last success. Drives
    /// <see cref="LockedUntil"/>, and is shown to the creator so a link being
    /// worked on is visible rather than silent.
    /// </summary>
    public int FailedAttempts { get; set; }

    /// <summary>
    /// While set and in the future, the link refuses codes outright. Grows with
    /// <see cref="FailedAttempts"/> so guessing gets slower, and clears on the
    /// next success so a recipient who simply fumbled is not punished for it.
    /// </summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>How many times the code was entered correctly. Creator-visible.</summary>
    public int SuccessfulRedeems { get; set; }

    /// <summary>
    /// When the link was last opened successfully, or null if never.
    ///
    /// This and the two counters are the whole audit trail, on purpose. Recording
    /// the visitor's address or user agent would mean collecting personal data
    /// about a third party who never agreed to anything, to answer a question
    /// these three columns already answer.
    /// </summary>
    public DateTime? LastAccessedAt { get; set; }

    /// <summary>When the link was created (UTC). DB-generated default.</summary>
    public DateTime CreatedAt { get; set; }
}
