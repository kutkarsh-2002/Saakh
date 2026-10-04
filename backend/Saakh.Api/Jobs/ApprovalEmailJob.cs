using Microsoft.EntityFrameworkCore;
using Saakh.Api.Data;
using Saakh.Api.Services;

namespace Saakh.Api.Jobs;

/// <summary>
/// Sends the approval email the moment an Admin approves a no-GSTIN account. Runs on
/// Hangfire so the Admin's review action is never held up by the mail provider.
/// </summary>
public class ApprovalEmailJob
{
    private readonly SaakhDbContext _db;
    private readonly IEmailSender _email;
    private readonly ILogger<ApprovalEmailJob> _log;

    public ApprovalEmailJob(SaakhDbContext db, IEmailSender email, ILogger<ApprovalEmailJob> log)
    {
        _db = db;
        _email = email;
        _log = log;
    }

    /// <summary>Subject and body are the copy drafted in spec.md, used verbatim.</summary>
    public const string Subject = "Your account has been approved";

    public static string Body(string name) =>
        $"""
         Hi {name},

         Good news - your account has been reviewed and approved. You now have full access:
         search for trusted Lenders and Seekers, send and receive interest, and start tracking deals.

         Log in to get started.

         - The Saakh Team
         """;

    public async Task SendAsync(Guid profileId)
    {
        var profile = await _db.Profiles.AsNoTracking()
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == profileId);

        if (profile is null)
        {
            _log.LogWarning("Approval email skipped: profile {ProfileId} no longer exists", profileId);
            return;
        }

        var address = profile.User.Email;

        if (string.IsNullOrWhiteSpace(address))
        {
            _log.LogWarning("Approval email skipped: profile {ProfileId} has no email address", profileId);
            return;
        }

        await _email.SendAsync(address, profile.Name, Subject, Body(profile.Name));
    }
}
