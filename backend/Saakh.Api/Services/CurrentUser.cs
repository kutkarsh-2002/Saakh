using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Saakh.Api.Data;
using Saakh.Api.Domain;

namespace Saakh.Api.Services;

public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? ProfileId { get; }
    Guid? AdminId { get; }
    bool IsAdmin { get; }
    bool IsAuthenticated { get; }

    /// <summary>The caller's Profile, tracked. Throws when the caller has no Profile.</summary>
    Task<Profile> RequireProfileAsync(CancellationToken ct = default);

    Task<Admin> RequireAdminAsync(CancellationToken ct = default);
}

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;
    private readonly SaakhDbContext _db;

    public CurrentUser(IHttpContextAccessor accessor, SaakhDbContext db)
    {
        _accessor = accessor;
        _db = db;
    }

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId => ReadGuid(ClaimTypes.NameIdentifier) ?? ReadGuid("sub");

    public Guid? ProfileId => ReadGuid(JwtTokenService.ProfileIdClaim);

    public Guid? AdminId => ReadGuid(JwtTokenService.AdminIdClaim);

    public bool IsAdmin => Principal?.IsInRole(SaakhRoles.Admin) ?? false;

    public async Task<Profile> RequireProfileAsync(CancellationToken ct = default)
    {
        var id = ProfileId ?? throw new DomainException("This account does not have a trading profile.");

        return await _db.Profiles
                   .Include(p => p.CategorySubType)
                   .FirstOrDefaultAsync(p => p.Id == id, ct)
               ?? throw new DomainException("Profile not found.");
    }

    public async Task<Admin> RequireAdminAsync(CancellationToken ct = default)
    {
        var id = AdminId ?? throw new DomainException("This account is not an administrator.");

        return await _db.Admins.FirstOrDefaultAsync(a => a.Id == id, ct)
               ?? throw new DomainException("Administrator not found.");
    }

    private Guid? ReadGuid(string claimType)
    {
        var raw = Principal?.FindFirst(claimType)?.Value;
        return Guid.TryParse(raw, out var value) ? value : null;
    }
}

public static class SaakhRoles
{
    public const string Lender = "Lender";
    public const string Seeker = "Seeker";
    public const string Admin = "Admin";

    public static readonly string[] All = [Lender, Seeker, Admin];

    public const string Trader = Lender + "," + Seeker;
}

/// <summary>
/// A business-rule violation. Surfaced as a 400 with a plain-language message by the
/// global exception handler, so rule wording lives next to the rule itself.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message, int statusCode = StatusCodes.Status400BadRequest) : base(message)
        => StatusCode = statusCode;

    public int StatusCode { get; }

    public static DomainException NotFound(string message) =>
        new(message, StatusCodes.Status404NotFound);

    public static DomainException Forbidden(string message) =>
        new(message, StatusCodes.Status403Forbidden);

    public static DomainException Conflict(string message) =>
        new(message, StatusCodes.Status409Conflict);
}
