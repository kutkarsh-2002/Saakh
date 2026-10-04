using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Saakh.Api.Configuration;
using Saakh.Api.Data;
using Saakh.Api.Domain;

namespace Saakh.Api.Services;

public record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);

public interface ITokenService
{
    Task<TokenPair> IssueAsync(AppUser user, CancellationToken ct = default);

    Task<TokenPair?> RotateAsync(string refreshToken, CancellationToken ct = default);

    Task RevokeAsync(string refreshToken, CancellationToken ct = default);
}

public class JwtTokenService : ITokenService
{
    /// <summary>Claim carrying the caller's Profile id, so controllers never re-look it up.</summary>
    public const string ProfileIdClaim = "saakh:profile_id";

    /// <summary>Claim carrying verification status, so route guards can gate tabs without a round trip.</summary>
    public const string VerificationClaim = "saakh:verification";

    public const string AdminIdClaim = "saakh:admin_id";

    private readonly SaakhDbContext _db;
    private readonly UserManager<AppUser> _users;
    private readonly JwtOptions _options;

    public JwtTokenService(SaakhDbContext db, UserManager<AppUser> users, IOptions<JwtOptions> options)
    {
        _db = db;
        _users = users;
        _options = options.Value;
    }

    public async Task<TokenPair> IssueAsync(AppUser user, CancellationToken ct = default)
    {
        var roles = await _users.GetRolesAsync(user);

        var profile = await _db.Profiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        var admin = await _db.Admins.AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == user.Id, ct);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName)
        };

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        if (profile is not null)
        {
            claims.Add(new Claim(ProfileIdClaim, profile.Id.ToString()));
            claims.Add(new Claim(VerificationClaim, profile.VerificationStatus.ToString()));
        }

        if (admin is not null)
        {
            claims.Add(new Claim(AdminIdClaim, admin.Id.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var expires = DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        var refresh = await CreateRefreshTokenAsync(user.Id, ct);

        return new TokenPair(accessToken, refresh.Token, expires);
    }

    public async Task<TokenPair?> RotateAsync(string refreshToken, CancellationToken ct = default)
    {
        var existing = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == refreshToken, ct);

        if (existing is null || !existing.IsActive)
        {
            return null;
        }

        var pair = await IssueAsync(existing.User, ct);

        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.ReplacedByToken = pair.RefreshToken;
        await _db.SaveChangesAsync(ct);

        return pair;
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        var existing = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == refreshToken, ct);
        if (existing is { RevokedAt: null })
        {
            existing.RevokedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task<RefreshToken> CreateRefreshTokenAsync(Guid userId, CancellationToken ct)
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenDays)
        };

        _db.RefreshTokens.Add(token);
        await _db.SaveChangesAsync(ct);
        return token;
    }

    /// <summary>JWT registered claim names used when minting the access token.</summary>
    private static class JwtRegisteredClaimNames
    {
        public const string Sub = "sub";
        public const string Email = "email";
        public const string Jti = "jti";
    }
}
