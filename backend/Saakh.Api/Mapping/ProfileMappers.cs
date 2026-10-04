using Saakh.Api.Domain;
using Saakh.Api.Dtos;

namespace Saakh.Api.Mapping;

/// <summary>
/// Entity to DTO mapping for profiles and the shared taxonomy.
///
/// Written by hand rather than generated. These are the shapes that decide what
/// one trader is allowed to see about another, so the rule — a GSTIN is masked
/// and a phone number omitted unless the caller owns the profile — is visible on
/// the line that applies it, not buried in a convention.
/// </summary>
public static class ProfileMappers
{
    /// <summary>
    /// Masks all but the state code and the final character. A GSTIN is a public
    /// tax identifier, but a discovery list has no reason to hand out the full
    /// number before the two parties have made any contact.
    /// </summary>
    public static string? MaskGstin(string? gstin)
    {
        if (string.IsNullOrWhiteSpace(gstin) || gstin.Length < 15)
        {
            return gstin;
        }

        return $"{gstin[..2]}{new string('X', 10)}{gstin[12..]}";
    }

    public static CategorySubTypeDto ToDto(this CategorySubType subType) =>
        new(subType.Id, subType.Category, subType.Key, subType.DisplayName, subType.DefaultUnit);

    /// <summary>
    /// The profile shape shown wherever a profile appears.
    /// </summary>
    /// <param name="profile">The profile being described.</param>
    /// <param name="trust">
    /// Passed in rather than computed here, so a list endpoint can batch the trust
    /// lookup across every row instead of querying once per profile.
    /// </param>
    /// <param name="revealPrivateDetails">
    /// True only when the caller owns this profile, or is an Admin. It unmasks the
    /// GSTIN and includes the phone number; everyone else sees neither.
    /// </param>
    public static ProfileSummaryDto ToSummary(
        this Profile profile,
        TrustSummaryDto trust,
        bool revealPrivateDetails = false) =>
        new(
            profile.Id,
            profile.Role,
            profile.Name,
            profile.IsBusiness,
            profile.BusinessSize,
            // Verified means the registry confirmed it, not merely that a number exists.
            profile.GstinVerifiedAt is not null,
            revealPrivateDetails ? profile.Gstin : MaskGstin(profile.Gstin),
            profile.GstinLegalName,
            profile.VerificationStatus,
            profile.AvailabilityStatus,
            profile.SuspensionEndDate,
            profile.RejectionReason,
            profile.Country,
            profile.State,
            profile.District,
            profile.Category,
            profile.CategorySubType?.ToDto(),
            profile.CapacityMin,
            profile.CapacityMax,
            profile.CapacityUnit,
            profile.OwnTradeDescription,
            revealPrivateDetails ? profile.Phone : null,
            profile.CreatedAt,
            trust);
}
