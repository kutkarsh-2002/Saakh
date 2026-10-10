using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Saakh.Api.Data;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Saakh.Api.Mapping;
using Saakh.Api.Jobs;

namespace Saakh.Api.Services;

public interface IAuthService
{
    Task<OtpRequestResultDto> RequestOtpAsync(string phone, string? email, CancellationToken ct = default);

    Task<bool> VerifyOtpAsync(string phone, string code, CancellationToken ct = default);

    Task<GstinCheckResultDto> CheckGstinAsync(string gstin, CancellationToken ct = default);

    Task<AuthResultDto> RegisterAsync(RegisterDto dto, CancellationToken ct = default);

    Task<AuthResultDto> LoginAsync(LoginDto dto, CancellationToken ct = default);

    Task<AuthResultDto> RefreshAsync(string refreshToken, CancellationToken ct = default);

    Task<SessionDto> SessionAsync(Guid userId, CancellationToken ct = default);
}

public class AuthService : IAuthService
{
    private readonly SaakhDbContext _db;
    private readonly UserManager<AppUser> _users;
    private readonly ITokenService _tokens;
    private readonly IGstinVerifier _gstin;
    private readonly IOtpService _otp;
    private readonly ITrustStatsService _trust;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<AuthService> _log;

    public AuthService(SaakhDbContext db, UserManager<AppUser> users, ITokenService tokens,
        IGstinVerifier gstin, IOtpService otp, ITrustStatsService trust,
        IBackgroundJobClient jobs, ILogger<AuthService> log)
    {
        _db = db;
        _users = users;
        _tokens = tokens;
        _gstin = gstin;
        _otp = otp;
        _trust = trust;
        _jobs = jobs;
        _log = log;
    }

    public async Task<OtpRequestResultDto> RequestOtpAsync(string phone, string? email,
        CancellationToken ct = default)
    {
        if (!LooksLikePhone(phone))
        {
            throw new DomainException("Enter a 10-digit mobile number.");
        }

        var issue = await _otp.RequestAsync(new OtpRecipient(phone, email), ct);

        var minutes = Math.Max(1, issue.ExpiresInSeconds / 60);

        // Named rather than assumed: a code that went to an inbox must not say it was
        // texted, or the user waits at the wrong screen.
        var where = _otp.Destination;

        var message = issue.Sent
            ? $"We sent a 6-digit code to {where}. It expires in {minutes} minute{(minutes == 1 ? "" : "s")}."
            : $"A code is already on its way to {where}. You can ask for another one in "
              + $"{issue.RetryAfterSeconds} second{(issue.RetryAfterSeconds == 1 ? "" : "s")}.";

        return new OtpRequestResultDto(issue.Sent, message, issue.Code,
            issue.RetryAfterSeconds, issue.ExpiresInSeconds);
    }

    public async Task<bool> VerifyOtpAsync(string phone, string code, CancellationToken ct = default)
    {
        var ok = await _otp.VerifyAsync(phone, code, ct);

        if (!ok)
        {
            throw new DomainException("That code is not right, or it has expired. Request a new one.");
        }

        return true;
    }

    public async Task<GstinCheckResultDto> CheckGstinAsync(string gstin, CancellationToken ct = default)
    {
        var result = await _gstin.VerifyAsync(gstin, ct);

        return new GstinCheckResultDto(
            result.IsValid,
            result.LegalName,
            result.Status,
            result.Message ?? (result.IsValid ? "GSTIN verified." : "This GSTIN could not be verified."),
            result.TransientFailure);
    }

    public async Task<AuthResultDto> RegisterAsync(RegisterDto dto, CancellationToken ct = default)
    {
        if (await _users.FindByEmailAsync(dto.Email) is not null)
        {
            throw DomainException.Conflict("An account already exists for that email address.");
        }

        var subType = dto.CategorySubTypeId is null
            ? null
            : await _db.CategorySubTypes.FirstOrDefaultAsync(s => s.Id == dto.CategorySubTypeId, ct)
              ?? throw new DomainException("That category sub-type does not exist.");

        if (subType is not null && subType.Category != dto.Category)
        {
            throw new DomainException($"\"{subType.DisplayName}\" is not a sub-type of {dto.Category}.");
        }

        if (dto.CapacityMax < dto.CapacityMin)
        {
            throw new DomainException("The upper end of the capacity range cannot be below the lower end.");
        }

        // The GSTIN path is resolved before the account is created, because an incorrect or
        // invalid GSTIN blocks profile creation until it is corrected (spec, Registration).
        string? gstin = null;
        DateTimeOffset? gstinVerifiedAt = null;
        string? legalName = null;
        var status = VerificationStatus.NeedsApproval;
        var scheduleRetry = false;

        if (!string.IsNullOrWhiteSpace(dto.Gstin))
        {
            gstin = GstinFormat.Normalize(dto.Gstin);
            var check = await _gstin.VerifyAsync(gstin, ct);

            if (check.IsValid)
            {
                gstinVerifiedAt = DateTimeOffset.UtcNow;
                legalName = check.LegalName;
                // A validated GSTIN means the profile is Active from the start.
                status = VerificationStatus.Active;
            }
            else if (check.TransientFailure)
            {
                // The registry was unreachable, which is not the vendor's fault. The account
                // is created on the manual-review path and a retry job tries again, so the
                // signup is never left hanging on a third-party outage.
                scheduleRetry = true;
                _log.LogWarning("GSTIN verification deferred for {Email}: {Message}", dto.Email, check.Message);
            }
            else
            {
                throw new DomainException(check.Message ?? "That GSTIN could not be verified.");
            }
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = dto.Email.Trim(),
            Email = dto.Email.Trim(),
            FullName = dto.FullName.Trim(),
            PhoneNumber = dto.Phone.Trim(),
            PhoneVerified = true,
            PhoneNumberConfirmed = true
        };

        var created = await _users.CreateAsync(user, dto.Password);

        if (!created.Succeeded)
        {
            throw new DomainException(string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        var roleName = dto.Role == ProfileRole.Lender ? SaakhRoles.Lender : SaakhRoles.Seeker;
        await _users.AddToRoleAsync(user, roleName);

        var profile = new Profile
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Role = dto.Role,
            Name = dto.FullName.Trim(),
            IsBusiness = dto.IsBusiness,
            BusinessSize = dto.BusinessSize,
            Gstin = gstin,
            GstinVerifiedAt = gstinVerifiedAt,
            GstinLegalName = legalName,
            VerificationStatus = status,
            // An account that still needs approving is not discoverable anyway, so
            // starting it Active would have the Profile claim "visible in search"
            // while the gate hides it. It starts Inactive and is switched on at the
            // moment approval actually makes it visible.
            AvailabilityStatus = status == VerificationStatus.Active
                ? AvailabilityStatus.Active
                : AvailabilityStatus.Inactive,
            Country = dto.Country.Trim(),
            State = dto.State.Trim(),
            District = dto.District.Trim(),
            Category = dto.Category,
            CategorySubTypeId = subType?.Id,
            CapacityMin = dto.CapacityMin,
            CapacityMax = dto.CapacityMax,
            CapacityUnit = string.IsNullOrWhiteSpace(dto.CapacityUnit)
                ? subType?.DefaultUnit ?? "INR"
                : dto.CapacityUnit.Trim(),
            OwnTradeDescription = string.IsNullOrWhiteSpace(dto.OwnTradeDescription)
                ? null
                : dto.OwnTradeDescription.Trim(),
            Phone = dto.Phone.Trim()
        };

        _db.Profiles.Add(profile);
        await _db.SaveChangesAsync(ct);

        if (scheduleRetry)
        {
            _jobs.Schedule<GstinRetryJob>(job => job.RetryAsync(profile.Id), TimeSpan.FromMinutes(2));
        }

        var pair = await _tokens.IssueAsync(user, ct);
        return new AuthResultDto(pair.AccessToken, pair.RefreshToken, pair.AccessTokenExpiresAt,
            await SessionAsync(user.Id, ct));
    }

    public async Task<AuthResultDto> LoginAsync(LoginDto dto, CancellationToken ct = default)
    {
        var user = await _users.FindByEmailAsync(dto.Email.Trim());

        if (user is null || !await _users.CheckPasswordAsync(user, dto.Password))
        {
            // One message for both cases, so the endpoint cannot be used to enumerate accounts.
            throw new DomainException("That email address and password do not match.",
                StatusCodes.Status401Unauthorized);
        }

        var profile = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id, ct);

        if (profile?.AvailabilityStatus == AvailabilityStatus.Removed)
        {
            throw DomainException.Forbidden("This account has been removed by an administrator.");
        }

        var pair = await _tokens.IssueAsync(user, ct);
        return new AuthResultDto(pair.AccessToken, pair.RefreshToken, pair.AccessTokenExpiresAt,
            await SessionAsync(user.Id, ct));
    }

    public async Task<AuthResultDto> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var pair = await _tokens.RotateAsync(refreshToken, ct)
                   ?? throw new DomainException("That session has expired. Sign in again.",
                       StatusCodes.Status401Unauthorized);

        var token = await _db.RefreshTokens.AsNoTracking()
            .FirstAsync(t => t.Token == pair.RefreshToken, ct);

        return new AuthResultDto(pair.AccessToken, pair.RefreshToken, pair.AccessTokenExpiresAt,
            await SessionAsync(token.UserId, ct));
    }

    public async Task<SessionDto> SessionAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw DomainException.NotFound("That account does not exist.");

        var roles = await _users.GetRolesAsync(user);

        var profile = await _db.Profiles.AsNoTracking()
            .Include(p => p.CategorySubType)
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var admin = await _db.Admins.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId, ct);

        ProfileSummaryDto? profileDto = null;

        if (profile is not null)
        {
            var trust = await _trust.ForOneAsync(profile.Id, ct);
            profileDto = profile.ToSummary(trust, revealPrivateDetails: true);
        }

        return new SessionDto(
            user.Id,
            user.Email ?? string.Empty,
            user.FullName,
            roles.ToArray(),
            profileDto,
            admin is null ? null : new AdminSummaryDto(admin.Id, admin.Name));
    }

    private static bool LooksLikePhone(string phone)
    {
        var digits = phone.Where(char.IsDigit).ToArray();
        return digits.Length is >= 10 and <= 13;
    }
}
