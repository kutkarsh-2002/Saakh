using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Saakh.Api.Infrastructure;
using Saakh.Api.Dtos;
using Saakh.Api.Services;

namespace Saakh.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly ITokenService _tokens;
    private readonly ICurrentUser _currentUser;

    public AuthController(IAuthService auth, ITokenService tokens, ICurrentUser currentUser)
    {
        _auth = auth;
        _tokens = tokens;
        _currentUser = currentUser;
    }

    /// <summary>Issues a phone OTP. Mandatory at signup on both verification paths.</summary>
    [HttpPost("otp/request")]
    [EnableRateLimiting(RateLimitPolicies.Signup)]
    public async Task<ActionResult<OtpRequestResultDto>> RequestOtp(OtpRequestDto dto, CancellationToken ct)
        => Ok(await _auth.RequestOtpAsync(dto.Phone, ct));

    [HttpPost("otp/verify")]
    [EnableRateLimiting(RateLimitPolicies.Signup)]
    public async Task<IActionResult> VerifyOtp(OtpVerifyDto dto, CancellationToken ct)
    {
        await _auth.VerifyOtpAsync(dto.Phone, dto.Code, ct);
        return Ok(new { verified = true });
    }

    /// <summary>
    /// Real-time GSTIN validation against the government registry, called from the signup
    /// form before submission. Rate-limited because every live call spends a credit.
    /// </summary>
    [HttpPost("gstin/check")]
    [EnableRateLimiting(RateLimitPolicies.Gstin)]
    public async Task<ActionResult<GstinCheckResultDto>> CheckGstin(GstinCheckDto dto, CancellationToken ct)
        => Ok(await _auth.CheckGstinAsync(dto.Gstin, ct));

    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.Signup)]
    public async Task<ActionResult<AuthResultDto>> Register(RegisterDto dto, CancellationToken ct)
        => Ok(await _auth.RegisterAsync(dto, ct));

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Signup)]
    public async Task<ActionResult<AuthResultDto>> Login(LoginDto dto, CancellationToken ct)
        => Ok(await _auth.LoginAsync(dto, ct));

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResultDto>> Refresh(RefreshDto dto, CancellationToken ct)
        => Ok(await _auth.RefreshAsync(dto.RefreshToken, ct));

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(RefreshDto dto, CancellationToken ct)
    {
        await _tokens.RevokeAsync(dto.RefreshToken, ct);
        return NoContent();
    }

    /// <summary>Re-reads the session, so the shell can refresh tab locks after an approval.</summary>
    [HttpGet("session")]
    [Authorize]
    public async Task<ActionResult<SessionDto>> Session(CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw DomainException.Forbidden("No active session.");
        return Ok(await _auth.SessionAsync(userId, ct));
    }
}
