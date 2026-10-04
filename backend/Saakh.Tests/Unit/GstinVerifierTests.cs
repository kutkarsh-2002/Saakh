using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Saakh.Api.Services;
using Xunit;

namespace Saakh.Tests.Unit;

/// <summary>
/// A GSTIN is the strongest identity signal in the product, and an invalid one
/// blocks account creation outright. The format gate matters commercially too:
/// every call that reaches the real provider spends a credit from a 100/month
/// free tier, so a mistyped number must never get that far.
/// </summary>
public class GstinFormatTests
{
    [Theory]
    [InlineData("27AAPFU0939F1ZV")]
    [InlineData("29ABCDE1234F1Z5")]
    [InlineData("09AAACH7409R1ZZ")]
    public void Accepts_a_well_formed_gstin(string gstin) =>
        GstinFormat.IsWellFormed(gstin).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NOTAGSTIN")]
    [InlineData("27AAPFU0939F1Z")]       // 14 characters
    [InlineData("27AAPFU0939F1ZVX")]     // 16 characters
    [InlineData("2AAAPFU0939F1ZV")]      // state code is not two digits
    [InlineData("27AAPFU0939F1AV")]      // the fixed Z in position 14 is missing
    [InlineData("27AAPFU0939F0ZV")]      // entity code 0 is not valid
    public void Rejects_anything_malformed(string? gstin) =>
        GstinFormat.IsWellFormed(gstin).Should().BeFalse();

    [Fact]
    public void Normalisation_trims_and_uppercases_so_user_typing_does_not_matter() =>
        GstinFormat.Normalize("  27aapfu0939f1zv  ").Should().Be("27AAPFU0939F1ZV");
}

public class MockGstinVerifierTests
{
    private static MockGstinVerifier Verifier() => new(NullLogger<MockGstinVerifier>.Instance);

    [Fact]
    public async Task A_well_formed_gstin_verifies_and_returns_a_legal_name()
    {
        var result = await Verifier().VerifyAsync("27AAPFU0939F1ZV");

        result.IsValid.Should().BeTrue();
        result.LegalName.Should().NotBeNullOrWhiteSpace();
        result.Status.Should().Be("Active");
        result.TransientFailure.Should().BeFalse();
    }

    [Fact]
    public async Task A_malformed_gstin_is_rejected_without_a_registry_call()
    {
        var result = await Verifier().VerifyAsync("NOT-A-GSTIN");

        result.IsValid.Should().BeFalse();
        result.TransientFailure.Should().BeFalse(
            "a mistyped number is the user's error to correct, not something to retry");
    }

    [Fact]
    public async Task The_00_prefix_rehearses_an_unregistered_number()
    {
        var result = await Verifier().VerifyAsync("00AAPFU0939F1ZV");

        result.IsValid.Should().BeFalse();
        result.TransientFailure.Should().BeFalse();
        result.Message.Should().Contain("not registered");
    }

    [Fact]
    public async Task The_99_prefix_rehearses_a_registry_outage_which_is_retryable()
    {
        var result = await Verifier().VerifyAsync("99AAPFU0939F1ZV");

        result.IsValid.Should().BeFalse();
        // The distinction matters: a transient failure must not block signup the way
        // an invalid number does, or a third-party outage locks out real vendors.
        result.TransientFailure.Should().BeTrue();
    }
}
