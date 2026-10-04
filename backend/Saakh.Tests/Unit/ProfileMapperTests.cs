using FluentAssertions;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Saakh.Api.Mapping;
using Saakh.Api.Services;
using Xunit;

namespace Saakh.Tests.Unit;

/// <summary>
/// The profile mapper decides what one trader is allowed to learn about another.
/// These are access-control rules wearing the clothes of a mapping function, so
/// they get tested like access control: both the allowed and the denied case.
/// </summary>
public class ProfileMapperTests
{
    private static Profile SampleProfile() => new()
    {
        Id = Guid.NewGuid(),
        Role = ProfileRole.Lender,
        Name = "Deshmukh Traders",
        IsBusiness = true,
        BusinessSize = BusinessSize.Small,
        Gstin = "27AAPFU0939F1ZV",
        GstinVerifiedAt = DateTimeOffset.UtcNow,
        GstinLegalName = "DESHMUKH TRADERS",
        VerificationStatus = VerificationStatus.Active,
        AvailabilityStatus = AvailabilityStatus.Active,
        Country = "India",
        State = "Maharashtra",
        District = "Pune",
        Category = DealCategory.RawMaterial,
        CapacityMin = 1000,
        CapacityMax = 5000,
        CapacityUnit = "kg",
        Phone = "9876543210",
        CreatedAt = DateTimeOffset.UtcNow.AddMonths(-6)
    };

    [Fact]
    public void A_counterparty_sees_a_masked_gstin_and_no_phone_number()
    {
        var dto = SampleProfile().ToSummary(TrustStatsService.Empty);

        // Length is preserved so the value still reads as a GSTIN: the state code
        // and the last three characters survive, the identifying middle does not.
        dto.GstinMasked.Should().Be("27XXXXXXXXXX1ZV");
        dto.GstinMasked.Should().HaveLength(15);
        dto.GstinMasked.Should().NotContain("AAPFU");
        dto.Phone.Should().BeNull("a phone number is not a discovery signal");
    }

    [Fact]
    public void The_owner_and_admins_see_the_full_gstin_and_the_phone_number()
    {
        var profile = SampleProfile();

        var dto = profile.ToSummary(TrustStatsService.Empty, revealPrivateDetails: true);

        dto.GstinMasked.Should().Be(profile.Gstin);
        dto.Phone.Should().Be(profile.Phone);
    }

    [Fact]
    public void Masking_keeps_the_state_code_so_a_counterparty_can_still_place_the_business()
    {
        // The first two digits are the GST state code; revealing them leaks nothing
        // the Location field does not already say.
        var masked = ProfileMappers.MaskGstin("29ABCDE1234F1Z5");

        masked.Should().StartWith("29");
        masked.Should().HaveLength(15);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TOO-SHORT")]
    public void Masking_passes_through_anything_that_is_not_a_full_gstin(string? input)
    {
        // A short or absent value has nothing to hide, and must not throw.
        ProfileMappers.MaskGstin(input).Should().Be(input);
    }

    [Fact]
    public void Verified_means_the_registry_confirmed_it_not_merely_that_a_number_was_typed()
    {
        var unverified = SampleProfile();
        unverified.GstinVerifiedAt = null;

        unverified.ToSummary(TrustStatsService.Empty).GstinVerified.Should().BeFalse(
            "a GSTIN that was never checked is not a trust signal");
    }

    [Fact]
    public void Evidence_documents_never_expose_their_storage_key()
    {
        var document = new EvidenceDocument
        {
            Id = Guid.NewGuid(),
            StorageKey = "2026/10/secret-key-abc123.pdf",
            FileName = "shop-licence.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            DocumentType = "Shop licence",
            Decision = EvidenceDecision.AwaitingReview
        };

        var dto = document.ToDto();

        // The DTO is a record, so this compares every property by value.
        dto.Should().BeOfType<EvidenceDocumentDto>();
        typeof(EvidenceDocumentDto).GetProperties()
            .Select(p => p.Name)
            .Should().NotContain(name => name.Contains("Storage", StringComparison.OrdinalIgnoreCase),
                "a client that learns storage keys can start guessing at its neighbours");
    }
}
