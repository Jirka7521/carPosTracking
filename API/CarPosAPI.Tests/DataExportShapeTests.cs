using System.Reflection;
using System.Text.Json;
using CarPosAPI.Data.Entities;
using CarPosAPI.Services.Privacy;

namespace CarPosAPI.Tests;

/// <summary>
/// The data export is the one endpoint whose job is to hand over everything, which
/// makes it the one endpoint where a careless projection ships a password hash or a
/// device's sealed private key to whoever asked.
///
/// The defence in the service is that nothing reaches the JSON writer except the
/// dedicated <c>*ExportRow</c> records — the <see cref="User"/> and
/// <see cref="Device"/> entities, the only two types in the system carrying
/// secrets, are never written directly. These tests pin that shape down: if
/// somebody later "simplifies" <see cref="UserExportRow"/> by adding a field, or
/// widens it to the entity, this fails before the export does.
///
/// What it does <em>not</em> prove is that the service actually uses these records —
/// only that the records are safe to use. Keep the projections in
/// <c>DataExportService</c> honest as well.
/// </summary>
public sealed class DataExportShapeTests
{
    /// <summary>
    /// Fragments that must never appear in an exported property name. Matched
    /// case-insensitively as substrings, so <c>PasswordHash</c>, <c>passwordHash</c>
    /// and <c>OldPassword</c> are all caught.
    /// </summary>
    private static readonly string[] ForbiddenFragments =
    [
        "password",
        "privatekey",
        "ciphertext",
        "secret",
        "signingkey",
        "masterkey",
        "token",
    ];

    /// <summary>Every projection record the exporter writes rows from.</summary>
    public static TheoryData<Type> ExportRowTypes =>
    [
        typeof(UserExportRow),
        typeof(DeviceExportRow),
        typeof(GrantExportRow),
        typeof(DeviceAliasExportRow),
    ];

    [Theory]
    [MemberData(nameof(ExportRowTypes))]
    public void ExportRowsCarryNothingSecretShaped(Type rowType)
    {
        PropertyInfo[] properties = rowType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.NotEmpty(properties);

        foreach (PropertyInfo property in properties)
        {
            foreach (string fragment in ForbiddenFragments)
            {
                Assert.False(
                    property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                    $"{rowType.Name}.{property.Name} looks like a secret and must not be exported.");
            }
        }
    }

    [Fact]
    public void SerialisedExportRowsContainNoSecretKeys()
    {
        // The reflection checks above test names; this tests the bytes that would
        // actually leave the process, which is the thing that matters.
        UserExportRow user = new UserExportRow(
            1,
            "someone@example.org",
            "Test",
            "Person",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "2026-09-06",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        DeviceExportRow device = new DeviceExportRow(
            Guid.NewGuid(),
            "GNSS01",
            "Test car",
            true,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            null);

        string json = string.Concat(
            JsonSerializer.Serialize(user),
            JsonSerializer.Serialize(device));

        foreach (string fragment in ForbiddenFragments)
        {
            Assert.DoesNotContain(fragment, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheEntitiesTheseRecordsReplaceDoCarrySecrets()
    {
        // If this ever fails, the entities stopped holding secrets and the whole
        // projection dance became unnecessary — worth knowing, and worth deleting.
        // Until then it is the reason the records above exist at all.
        Assert.NotNull(typeof(User).GetProperty(nameof(User.PasswordHash)));
        Assert.NotNull(typeof(Device).GetProperty(nameof(Device.PrivateKeyCiphertext)));
    }
}
