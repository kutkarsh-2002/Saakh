using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Saakh.Api.Domain;
using Saakh.Api.Services;

namespace Saakh.Api.Infrastructure;

/// <summary>
/// Enforces the access gate from the spec's verification flow: while an account is Needs
/// Approval, Pending or Rejected, everything except its own Profile tab stays locked.
///
/// The Angular route guards hide those tabs, but the lock is enforced here too, so it is a
/// real access boundary rather than a visual one.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireVerifiedProfileAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

        // Admins never hold a trading profile, so this gate does not apply to them.
        if (currentUser.IsAdmin)
        {
            await next();
            return;
        }

        var profile = await currentUser.RequireProfileAsync(context.HttpContext.RequestAborted);

        if (profile.VerificationStatus != VerificationStatus.Active)
        {
            context.Result = new ObjectResult(new ApiError(
                profile.VerificationStatus switch
                {
                    VerificationStatus.NeedsApproval =>
                        "Submit your proof documents to unlock search, deals and analytics.",
                    VerificationStatus.Pending =>
                        "Your documents are under review. Everything unlocks once an administrator approves them.",
                    VerificationStatus.Rejected =>
                        "Your verification was not approved. Resubmit your documents to unlock the platform.",
                    _ => "This part of Saakh unlocks once your account is verified."
                },
                "verification_required",
                new Dictionary<string, string[]>
                {
                    ["verificationStatus"] = [profile.VerificationStatus.ToString()]
                }))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        if (profile.AvailabilityStatus == AvailabilityStatus.Removed)
        {
            context.Result = new ObjectResult(new ApiError(
                "This profile has been removed by an administrator and can no longer act on the platform.",
                "profile_removed"))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
        else
        {
            await next();
        }
    }
}

/// <summary>Uniform error envelope, so the Angular interceptor has one shape to read.</summary>
public record ApiError(string Message, string? Code = null, IDictionary<string, string[]>? Errors = null);
