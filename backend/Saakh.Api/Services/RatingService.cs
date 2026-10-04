using Microsoft.EntityFrameworkCore;
using Saakh.Api.Data;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Saakh.Api.Mapping;

namespace Saakh.Api.Services;

public interface IRatingService
{
    Task<RatingDto> SubmitAsync(Profile actor, Guid dealId, SubmitRatingDto dto, CancellationToken ct = default);

    Task<IReadOnlyList<RatingDto>> ForProfileAsync(Guid profileId, CancellationToken ct = default);
}

public class RatingService : IRatingService
{
    private readonly SaakhDbContext _db;
    private readonly INotificationService _notifications;

    public RatingService(SaakhDbContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public async Task<RatingDto> SubmitAsync(Profile actor, Guid dealId, SubmitRatingDto dto,
        CancellationToken ct = default)
    {
        if (dto.Stars is < 1 or > 5)
        {
            throw new DomainException("A rating is between 1 and 5 stars.");
        }

        var deal = await _db.Deals
                       .Include(d => d.LenderProfile)
                       .Include(d => d.SeekerProfile)
                       .Include(d => d.Ratings)
                       .FirstOrDefaultAsync(d => d.Id == dealId, ct)
                   ?? throw DomainException.NotFound("That deal does not exist.");

        if (deal.LenderProfileId != actor.Id && deal.SeekerProfileId != actor.Id)
        {
            throw DomainException.Forbidden("This deal belongs to two other parties.");
        }

        // Rating opens when the deal reaches Completed or Halted (spec, Rating and Trust).
        if (!deal.IsClosed)
        {
            throw new DomainException(
                "You can rate a counterparty once the deal is Completed or Halted.");
        }

        // One rating per party per deal, enforced in the schema as well.
        if (deal.Ratings.Any(r => r.RaterProfileId == actor.Id))
        {
            throw DomainException.Conflict("You have already rated this deal.");
        }

        var ratedId = deal.LenderProfileId == actor.Id ? deal.SeekerProfileId : deal.LenderProfileId;

        var rating = new Rating
        {
            Id = Guid.NewGuid(),
            DealId = deal.Id,
            RaterProfileId = actor.Id,
            RatedProfileId = ratedId,
            Stars = dto.Stars,
            Comment = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim()
        };

        _db.Ratings.Add(rating);
        await _db.SaveChangesAsync(ct);

        await _notifications.PushAsync(ratedId, NotificationKinds.RatingReceived,
            $"{actor.Name} rated deal {deal.Reference}",
            $"{dto.Stars} of 5 stars. This now sits on your public trust record.",
            $"/deals/{deal.Id}", ct);

        rating.RaterProfile = actor;
        return rating.ToDto();
    }

    public async Task<IReadOnlyList<RatingDto>> ForProfileAsync(Guid profileId, CancellationToken ct = default)
    {
        var ratings = await _db.Ratings.AsNoTracking()
            .Include(r => r.RaterProfile)
            .Where(r => r.RatedProfileId == profileId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        return ratings.Select(DealMappers.ToDto).ToList();
    }
}
