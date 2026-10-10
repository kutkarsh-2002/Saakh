using Saakh.Api.Domain;

namespace Saakh.Api.Dtos;

/// <summary>
/// The email is optional and is only used where the code travels by email instead of
/// SMS. The signup form collects it a step earlier, so it is already in hand.
/// </summary>
public record OtpRequestDto(string Phone, string? Email = null);

/// <summary>
/// <paramref name="Sent"/> is false when a code was already sent moments ago and the
/// existing one still stands; <paramref name="RetryAfterSeconds"/> is what the form
/// counts down before offering Resend again.
/// </summary>
public record OtpRequestResultDto(
    bool Sent,
    string Message,
    string? DevCode,
    int RetryAfterSeconds,
    int ExpiresInSeconds);

public record OtpVerifyDto(string Phone, string Code);

/// <summary>
/// Signup payload. GSTIN is optional: supplied and valid gives an Active profile immediately;
/// omitted creates the account in Needs Approval (spec, Registration and Profile).
/// </summary>
public record RegisterDto(
    string Email,
    string Password,
    string FullName,
    ProfileRole Role,
    string Phone,
    string? Gstin,
    bool IsBusiness,
    BusinessSize BusinessSize,
    string Country,
    string State,
    string District,
    DealCategory Category,
    int? CategorySubTypeId,
    decimal CapacityMin,
    decimal CapacityMax,
    string CapacityUnit,
    string? OwnTradeDescription);

public record LoginDto(string Email, string Password);

public record RefreshDto(string RefreshToken);

public record AuthResultDto(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    SessionDto Session);

/// <summary>Everything the Angular shell needs to decide which tabs are reachable.</summary>
public record SessionDto(
    Guid UserId,
    string Email,
    string FullName,
    string[] Roles,
    ProfileSummaryDto? Profile,
    AdminSummaryDto? Admin);

public record AdminSummaryDto(Guid Id, string Name);

public record GstinCheckDto(string Gstin);

public record GstinCheckResultDto(bool IsValid, string? LegalName, string? Status, string Message, bool Retrying);
