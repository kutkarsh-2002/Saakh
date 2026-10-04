using Bogus;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Saakh.Api.Domain;
using Saakh.Api.Services;

namespace Saakh.Api.Data;

/// <summary>
/// Demo/pilot data loader, invoked by `dotnet run --seed` only (tech-stack.md). Seeded
/// profiles are written straight to the database and never go through the signup endpoint,
/// so no live GSTIN credit is ever spent on seed rows.
///
/// Trust signals here are realistic but not flattering: ratings span 2 to 5 stars, a
/// handful of deals are Halted with a real at-fault party, and three accounts sit unverified
/// so the Admin verification queue has genuine work in it.
/// </summary>
public class DataSeeder
{
    private const string SeedPassword = "Saakh@2026";

    private readonly SaakhDbContext _db;
    private readonly UserManager<AppUser> _users;
    private readonly RoleManager<AppRole> _roles;
    private readonly ILogger<DataSeeder> _log;

    public DataSeeder(SaakhDbContext db, UserManager<AppUser> users, RoleManager<AppRole> roles,
        ILogger<DataSeeder> log)
    {
        _db = db;
        _users = users;
        _roles = roles;
        _log = log;
    }

    /// <summary>
    /// The three roles are a fixed part of the domain, so they are created on every start,
    /// not only when the demo data is seeded.
    /// </summary>
    public static async Task EnsureRolesAsync(IServiceProvider services)
    {
        var roles = services.GetRequiredService<RoleManager<AppRole>>();

        foreach (var role in SaakhRoles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new AppRole { Id = Guid.NewGuid(), Name = role });
            }
        }
    }

    private async Task EnsureRolesAsync()
    {
        foreach (var role in SaakhRoles.All)
        {
            if (!await _roles.RoleExistsAsync(role))
            {
                await _roles.CreateAsync(new AppRole { Id = Guid.NewGuid(), Name = role });
            }
        }
    }

    /// <summary>Indian states/districts the sample data is grounded in, per the design brief.</summary>
    private static readonly (string State, string[] Districts)[] Locations =
    [
        ("Maharashtra", ["Pune", "Nashik", "Mumbai Suburban", "Nagpur"]),
        ("Gujarat", ["Ahmedabad", "Surat", "Rajkot"]),
        ("Karnataka", ["Bengaluru Urban", "Mysuru", "Belagavi"]),
        ("Tamil Nadu", ["Coimbatore", "Madurai", "Chennai"]),
        ("Uttar Pradesh", ["Kanpur Nagar", "Lucknow", "Varanasi"]),
        ("West Bengal", ["Kolkata", "Howrah", "Siliguri"]),
        ("Rajasthan", ["Jaipur", "Jodhpur"]),
        ("Punjab", ["Ludhiana", "Amritsar"])
    ];

    private static readonly string[] LenderBusinessSuffixes =
        ["Traders", "Distributors", "& Sons", "Enterprises", "Wholesale", "Agencies", "Supply Co."];

    private static readonly string[] SeekerBusinessSuffixes =
        ["Kirana Store", "General Stores", "Provision Store", "Fresh Mart", "Dairy Corner", "Medical Store"];

    private static readonly string[] EvidenceTypes =
        ["Shop licence", "Aadhaar card", "Electricity bill", "Trade licence", "Rental agreement"];

    public async Task RunAsync(bool reset = false, CancellationToken ct = default)
    {
        await _db.Database.MigrateAsync(ct);
        await EnsureRolesAsync();

        if (reset)
        {
            _log.LogWarning("Seed --reset: clearing existing demo data");
            await ClearAsync(ct);
        }

        if (await _db.Profiles.AnyAsync(ct))
        {
            _log.LogInformation(
                "Seed skipped: the database already holds profiles. Pass --reset to wipe and reseed.");
            return;
        }

        // A fixed Bogus seed means every run of the demo shows the same numbers, so a
        // screenshot in the README still matches what a reviewer sees.
        Randomizer.Seed = new Random(20260925);
        var faker = new Faker("en_IND");

        var subTypes = await _db.CategorySubTypes.OrderBy(s => s.SortOrder).ToListAsync(ct);
        var moneySubTypes = subTypes.Where(s => s.Category == DealCategory.Money).ToList();
        var materialSubTypes = subTypes.Where(s => s.Category == DealCategory.RawMaterial).ToList();

        var admins = await SeedAdminsAsync(ct);
        var lenders = await SeedProfilesAsync(ProfileRole.Lender, 13, faker, moneySubTypes, materialSubTypes, ct);
        var seekers = await SeedProfilesAsync(ProfileRole.Seeker, 18, faker, moneySubTypes, materialSubTypes, ct);

        await SeedUnverifiedAsync(faker, materialSubTypes, ct);
        await SeedDealsAsync(faker, lenders, seekers, ct);

        _log.LogInformation(
            "Seeded {Lenders} Lender profiles, {Seekers} Seeker profiles, {Admins} admin(s), {Deals} deals, {Ratings} ratings.",
            lenders.Count, seekers.Count, admins.Count,
            await _db.Deals.CountAsync(ct), await _db.Ratings.CountAsync(ct));

        _log.LogInformation("Sign in with any seeded email and the password {Password}", SeedPassword);
        _log.LogInformation("Admin console: admin@saakh.app / {Password}", SeedPassword);
    }

    private async Task ClearAsync(CancellationToken ct)
    {
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM Ratings", ct);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM Messages", ct);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM ResumeRequests", ct);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM DealStateHistories", ct);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM Deals", ct);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM Interests", ct);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM Notifications", ct);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM AdminActionLogs", ct);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM EvidenceDocuments", ct);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM Profiles", ct);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM Admins", ct);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM RefreshTokens", ct);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM AspNetUserRoles", ct);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM AspNetUsers", ct);
    }

    private async Task<List<Admin>> SeedAdminsAsync(CancellationToken ct)
    {
        var admins = new List<Admin>();

        foreach (var (email, name) in new[]
                 {
                     ("admin@saakh.app", "Platform Admin"),
                     ("reviewer@saakh.app", "Verification Reviewer")
                 })
        {
            var user = await CreateUserAsync(email, name, "9000000000", SaakhRoles.Admin, ct);

            var admin = new Admin { Id = Guid.NewGuid(), UserId = user.Id, Name = name };
            _db.Admins.Add(admin);
            admins.Add(admin);
        }

        await _db.SaveChangesAsync(ct);
        return admins;
    }

    private async Task<List<Profile>> SeedProfilesAsync(ProfileRole role, int count, Faker faker,
        List<CategorySubType> moneySubTypes, List<CategorySubType> materialSubTypes, CancellationToken ct)
    {
        var profiles = new List<Profile>();

        for (var i = 0; i < count; i++)
        {
            var (state, districts) = faker.PickRandom(Locations);
            var district = faker.PickRandom(districts);

            // Lenders skew toward Money; Seekers skew toward material, matching how the
            // market actually looks: financiers lend cash, retailers need stock.
            var category = role == ProfileRole.Lender
                ? (faker.Random.Double() < 0.45 ? DealCategory.Money : DealCategory.RawMaterial)
                : (faker.Random.Double() < 0.3 ? DealCategory.Money : DealCategory.RawMaterial);

            var subType = category == DealCategory.Money
                ? faker.PickRandom(moneySubTypes)
                : faker.PickRandom(materialSubTypes);

            var isBusiness = faker.Random.Double() < (role == ProfileRole.Lender ? 0.85 : 0.6);
            var personName = faker.Name.FullName();

            var name = isBusiness
                ? $"{faker.Name.LastName()} {faker.PickRandom(role == ProfileRole.Lender ? LenderBusinessSuffixes : SeekerBusinessSuffixes)}"
                : personName;

            var (min, max) = CapacityRange(category, role, faker);

            var email = $"{role.ToString().ToLowerInvariant()}{i + 1}@saakh.demo";
            var phone = $"9{faker.Random.Long(100000000, 999999999)}";

            var user = await CreateUserAsync(email, name,
                phone, role == ProfileRole.Lender ? SaakhRoles.Lender : SaakhRoles.Seeker, ct);

            var profile = new Profile
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Role = role,
                Name = name,
                IsBusiness = isBusiness,
                BusinessSize = isBusiness
                    ? faker.PickRandom(BusinessSize.Small, BusinessSize.Medium, BusinessSize.Large)
                    : BusinessSize.Individual,
                // Seed rows carry a plausible GSTIN string written straight to the column; it
                // is never sent to the live registry (tech-stack.md seed note).
                Gstin = isBusiness ? PlausibleGstin(faker, state) : null,
                GstinVerifiedAt = isBusiness ? faker.Date.PastOffset(1) : null,
                GstinLegalName = isBusiness ? name.ToUpperInvariant() : null,
                // A no-GSTIN individual in the seed set is Active because an Admin already
                // reviewed their evidence - the unverified accounts are seeded separately.
                VerificationStatus = VerificationStatus.Active,
                AvailabilityStatus = AvailabilityStatus.Active,
                Country = "India",
                State = state,
                District = district,
                Category = category,
                CategorySubTypeId = subType.Id,
                CapacityMin = min,
                CapacityMax = max,
                CapacityUnit = subType.DefaultUnit,
                OwnTradeDescription = role == ProfileRole.Seeker
                    ? $"{faker.PickRandom("Retail", "Wholesale", "Street vending", "Home delivery")} of {subType.DisplayName.ToLowerInvariant()} in {district}"
                    : null,
                Phone = phone,
                CreatedAt = faker.Date.PastOffset(1)
            };

            _db.Profiles.Add(profile);
            profiles.Add(profile);
        }

        // One Lender and one Seeker are left Inactive, so the discovery exclusion rule is
        // visible in the data rather than only in the code.
        profiles[^1].AvailabilityStatus = AvailabilityStatus.Inactive;

        await _db.SaveChangesAsync(ct);
        return profiles;
    }

    /// <summary>
    /// Three accounts that have not cleared verification, so the Admin queue has real work:
    /// one Needs Approval (nothing submitted), two Pending (evidence awaiting review).
    /// </summary>
    private async Task SeedUnverifiedAsync(Faker faker, List<CategorySubType> materialSubTypes,
        CancellationToken ct)
    {
        var cases = new[]
        {
            ("pending1@saakh.demo", "Shaikh Fresh Vegetables", VerificationStatus.Pending, 2),
            ("pending2@saakh.demo", "Devi Provision Store", VerificationStatus.Pending, 1),
            ("needsapproval@saakh.demo", "Ramesh Tea Stall", VerificationStatus.NeedsApproval, 0)
        };

        foreach (var (email, name, status, evidenceCount) in cases)
        {
            var (state, districts) = faker.PickRandom(Locations);
            var district = faker.PickRandom(districts);
            var subType = faker.PickRandom(materialSubTypes);
            var phone = $"9{faker.Random.Long(100000000, 999999999)}";

            var user = await CreateUserAsync(email, name, phone, SaakhRoles.Seeker, ct);

            var profile = new Profile
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Role = ProfileRole.Seeker,
                Name = name,
                IsBusiness = false,
                BusinessSize = BusinessSize.Individual,
                // No GSTIN: this is exactly the account the manual checkpoint exists for.
                Gstin = null,
                VerificationStatus = status,
                AvailabilityStatus = AvailabilityStatus.Active,
                Country = "India",
                State = state,
                District = district,
                Category = DealCategory.RawMaterial,
                CategorySubTypeId = subType.Id,
                CapacityMin = 2000,
                CapacityMax = 15000,
                CapacityUnit = subType.DefaultUnit,
                OwnTradeDescription = $"Street vending of {subType.DisplayName.ToLowerInvariant()} in {district}",
                Phone = phone,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-faker.Random.Int(1, 6))
            };

            _db.Profiles.Add(profile);

            // Distinct types per profile, and the filename is derived from the type, so an
            // Admin never sees a row labelled "Trade licence" holding "electricity-bill.pdf".
            var chosenTypes = faker.PickRandom(EvidenceTypes, evidenceCount).ToList();

            foreach (var documentType in chosenTypes)
            {
                _db.EvidenceDocuments.Add(new EvidenceDocument
                {
                    Id = Guid.NewGuid(),
                    ProfileId = profile.Id,
                    // A placeholder key: the Admin console shows the row and reports the file
                    // as missing from storage rather than pretending to render a document.
                    StorageKey = $"seed/placeholder-{Guid.NewGuid():N}.pdf",
                    FileName = $"{documentType.Replace(' ', '-').ToLowerInvariant()}.pdf",
                    ContentType = "application/pdf",
                    SizeBytes = faker.Random.Long(80_000, 900_000),
                    DocumentType = documentType,
                    SubmittedAt = DateTimeOffset.UtcNow.AddHours(-faker.Random.Int(3, 60)),
                    Decision = EvidenceDecision.AwaitingReview
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedDealsAsync(Faker faker, List<Profile> lenders, List<Profile> seekers,
        CancellationToken ct)
    {
        var activeLenders = lenders.Where(p => p.AvailabilityStatus == AvailabilityStatus.Active).ToList();
        var activeSeekers = seekers.Where(p => p.AvailabilityStatus == AvailabilityStatus.Active).ToList();

        var subTypes = await _db.CategorySubTypes.ToListAsync(ct);
        var pairs = new HashSet<(Guid, Guid)>();
        var deals = new List<Deal>();
        var reference = 10000;

        // 28 deals spread over the last nine months: mostly Completed, a handful Halted, and
        // a few still in flight, so both charts show a real trend and the Open Deals table
        // is populated on first load.
        for (var i = 0; i < 28; i++)
        {
            // The two accounts a reviewer signs into first (lender1@ and seeker1@) are given
            // a deliberate spread of deals, so the dashboard, both charts and the History tab
            // all have something in them on first login rather than depending on the dice.
            var lender = i < 9 ? activeLenders[0] : faker.PickRandom(activeLenders);

            var seeker = i switch
            {
                < 9 => faker.PickRandom(activeSeekers.Skip(1).ToList()),
                < 16 => activeSeekers[0],
                _ => faker.PickRandom(activeSeekers)
            };

            var subType = subTypes.FirstOrDefault(s => s.Id == lender.CategorySubTypeId)
                          ?? faker.PickRandom(subTypes);

            // Interleaved rather than blocked, so every participant ends up with a mix of
            // settled, halted and in-flight deals. Over 28 deals this lands at 16 Completed,
            // 4 Halted, 4 Progress and 4 Open.
            var state = (i % 7) switch
            {
                <= 3 => DealState.Completed,
                4 => DealState.Halted,
                5 => DealState.Progress,
                _ => DealState.Open
            };

            // Closed deals are spread across the last nine months so both charts show a
            // trend. In-flight deals are recent, because a deal opened 200 days ago and
            // still untouched would be a data-quality problem, not a demo.
            var createdAt = state is DealState.Completed or DealState.Halted
                ? DateTimeOffset.UtcNow.AddDays(-faker.Random.Int(40, 270))
                : DateTimeOffset.UtcNow.AddDays(-faker.Random.Int(3, 45));

            var capacity = subType.Category == DealCategory.Money
                ? faker.Random.Int(15, 400) * 1000m
                : faker.Random.Int(20, 600) * 10m;

            var deal = new Deal
            {
                Id = Guid.NewGuid(),
                Reference = $"SK-{reference++}",
                LenderProfileId = lender.Id,
                SeekerProfileId = seeker.Id,
                Category = subType.Category,
                CategorySubTypeId = subType.Id,
                Capacity = capacity,
                CapacityUnit = subType.DefaultUnit,
                MaterialDescription = subType.Category == DealCategory.RawMaterial
                    ? $"{subType.DisplayName} consignment, {faker.PickRandom("grade A", "mixed grade", "retail pack", "bulk")}"
                    : null,
                Description = Description(faker, subType, capacity),
                // In-flight deals mostly settle in the future, with roughly one in five
                // genuinely overdue so the dashboard's overdue treatment is demonstrable
                // without every row screaming at the reader.
                EstimatedSettlementTime = state is DealState.Completed or DealState.Halted
                    ? createdAt.AddDays(faker.Random.Int(15, 75))
                    : (i % 5 == 0
                        ? DateTimeOffset.UtcNow.AddDays(-faker.Random.Int(2, 12))
                        : DateTimeOffset.UtcNow.AddDays(faker.Random.Int(4, 60))),
                State = state,
                Country = "India",
                LocationState = seeker.State,
                District = seeker.District,
                CreatedAt = createdAt,
                LenderConfirmedSettlement = state == DealState.Completed,
                SeekerConfirmedSettlement = state == DealState.Completed
            };

            if (state is DealState.Completed or DealState.Halted)
            {
                deal.ClosedAt = createdAt.AddDays(faker.Random.Int(10, 70));
            }

            if (state == DealState.Halted)
            {
                // Someone really did click halt, so the at-fault flag points at a party
                // rather than being left null and quietly unattributed.
                deal.HaltedByProfileId = faker.Random.Bool() ? lender.Id : seeker.Id;
                deal.HaltedAt = deal.ClosedAt;
            }

            deals.Add(deal);
            _db.Deals.Add(deal);

            // An accepted Interest precedes every deal, so the chat threads have somewhere
            // to hang and the Interest box is not empty.
            if (pairs.Add((lender.Id, seeker.Id)))
            {
                var initiatorIsLender = faker.Random.Bool();

                var interest = new Interest
                {
                    Id = Guid.NewGuid(),
                    FromProfileId = initiatorIsLender ? lender.Id : seeker.Id,
                    ToProfileId = initiatorIsLender ? seeker.Id : lender.Id,
                    Status = InterestStatus.Accepted,
                    Note = initiatorIsLender
                        ? "Have stock available this month, happy to discuss credit terms."
                        : "Looking for a reliable supplier on 30-day terms.",
                    CreatedAt = createdAt.AddDays(-faker.Random.Int(2, 10)),
                    RespondedAt = createdAt.AddDays(-faker.Random.Int(1, 2))
                };

                _db.Interests.Add(interest);
                deal.InterestId = interest.Id;

                _db.Messages.Add(new Message
                {
                    Id = Guid.NewGuid(),
                    InterestId = interest.Id,
                    DealId = deal.Id,
                    SenderProfileId = interest.FromProfileId,
                    Body = initiatorIsLender
                        ? "Namaste. I can supply from the 5th. What quantity are you looking at?"
                        : "Namaste. I need regular supply for my shop. What are your credit terms?",
                    SentAt = interest.RespondedAt!.Value.AddMinutes(20)
                });

                _db.Messages.Add(new Message
                {
                    Id = Guid.NewGuid(),
                    InterestId = interest.Id,
                    DealId = deal.Id,
                    SenderProfileId = interest.ToProfileId,
                    Body = "30 days works for me. Let us raise the ticket and keep it on record.",
                    SentAt = interest.RespondedAt!.Value.AddMinutes(45),
                    ReadAt = interest.RespondedAt!.Value.AddHours(1)
                });
            }

            AddHistory(deal, faker);
        }

        await _db.SaveChangesAsync(ct);
        await SeedRatingsAsync(faker, deals, ct);
    }

    private void AddHistory(Deal deal, Faker faker)
    {
        var actor = faker.Random.Bool() ? deal.LenderProfileId : deal.SeekerProfileId;

        _db.DealStateHistories.Add(new DealStateHistory
        {
            Id = Guid.NewGuid(),
            DealId = deal.Id,
            FromState = null,
            ToState = DealState.Open,
            TriggeredByProfileId = actor,
            Note = "Ticket raised, terms agreed.",
            OccurredAt = deal.CreatedAt
        });

        if (deal.State is DealState.Open)
        {
            return;
        }

        var progressAt = deal.CreatedAt.AddDays(faker.Random.Int(2, 12));

        _db.DealStateHistories.Add(new DealStateHistory
        {
            Id = Guid.NewGuid(),
            DealId = deal.Id,
            FromState = DealState.Open,
            ToState = DealState.Progress,
            TriggeredByProfileId = actor,
            Note = "Transfer started.",
            OccurredAt = progressAt
        });

        if (deal.State == DealState.Completed)
        {
            _db.DealStateHistories.Add(new DealStateHistory
            {
                Id = Guid.NewGuid(),
                DealId = deal.Id,
                FromState = DealState.Progress,
                ToState = DealState.Completed,
                TriggeredByProfileId = actor,
                Note = "Both parties confirmed settlement.",
                OccurredAt = deal.ClosedAt ?? progressAt.AddDays(20)
            });
        }
        else if (deal.State == DealState.Halted)
        {
            _db.DealStateHistories.Add(new DealStateHistory
            {
                Id = Guid.NewGuid(),
                DealId = deal.Id,
                FromState = DealState.Progress,
                ToState = DealState.Halted,
                TriggeredByProfileId = deal.HaltedByProfileId,
                Note = "Halted by the party above; recorded as at fault under the v1 rule.",
                OccurredAt = deal.HaltedAt ?? progressAt.AddDays(14)
            });
        }
    }

    private async Task SeedRatingsAsync(Faker faker, List<Deal> deals, CancellationToken ct)
    {
        foreach (var deal in deals.Where(d => d.State is DealState.Completed or DealState.Halted))
        {
            foreach (var (rater, rated) in new[]
                     {
                         (deal.LenderProfileId, deal.SeekerProfileId),
                         (deal.SeekerProfileId, deal.LenderProfileId)
                     })
            {
                // Not every party gets around to rating, which is what real data looks like.
                if (faker.Random.Double() < 0.15)
                {
                    continue;
                }

                var stars = deal.State == DealState.Completed
                    ? faker.Random.WeightedRandom([5, 4, 3], [0.62f, 0.3f, 0.08f])
                    // A halted deal pulls low ratings, and the party who halted takes the
                    // harsher score - that is the v1 fault rule showing up in the numbers.
                    : (rated == deal.HaltedByProfileId
                        ? faker.Random.WeightedRandom([1, 2], [0.55f, 0.45f])
                        : faker.Random.WeightedRandom([3, 4], [0.5f, 0.5f]));

                _db.Ratings.Add(new Rating
                {
                    Id = Guid.NewGuid(),
                    DealId = deal.Id,
                    RaterProfileId = rater,
                    RatedProfileId = rated,
                    Stars = stars,
                    Comment = Comment(faker, stars),
                    CreatedAt = (deal.ClosedAt ?? deal.CreatedAt).AddDays(faker.Random.Int(1, 5))
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private static string Comment(Faker faker, int stars) => stars switch
    {
        5 => faker.PickRandom(
            "Paid before the due date, no follow-up needed.",
            "Clean delivery, quality as promised. Would deal again.",
            "Straightforward throughout. Picked up the phone every time."),
        4 => faker.PickRandom(
            "Settled on time, one small shortfall in the first lot.",
            "Good to deal with. Communication could be quicker."),
        3 => faker.PickRandom(
            "Settled eventually, needed a few reminders.",
            "Mixed. Quality fine, timing slipped twice."),
        2 => faker.PickRandom(
            "Pulled out midway without much warning.",
            "Stopped responding once the first lot moved."),
        _ => faker.PickRandom(
            "Cancelled after I had already arranged the stock.",
            "Walked away from the arrangement.")
    };

    private static string Description(Faker faker, CategorySubType subType, decimal capacity) =>
        subType.Category == DealCategory.Money
            ? $"{subType.DisplayName} of {capacity:N0} INR against {faker.PickRandom("festival season stock", "a bulk purchase order", "a seasonal shortfall", "working capital for the month")}, settled in one instalment."
            : $"{subType.DisplayName} supply of {capacity:N0} {subType.DefaultUnit} on credit, {faker.PickRandom("delivered weekly", "in two lots", "in a single consignment")}, payment on agreed settlement date.";

    private static (decimal Min, decimal Max) CapacityRange(DealCategory category, ProfileRole role, Faker faker)
    {
        if (category == DealCategory.Money)
        {
            var min = faker.Random.Int(10, 60) * 1000m;
            return (min, min + faker.Random.Int(4, 20) * 10000m);
        }

        var low = faker.Random.Int(20, 200) * 10m;
        return (low, low + faker.Random.Int(20, 400) * 10m);
    }

    /// <summary>
    /// A plausible-looking GSTIN for a seed row. It is never verified against the registry,
    /// and the state code is made consistent with the profile's state so the data reads true.
    /// </summary>
    private static string PlausibleGstin(Faker faker, string state)
    {
        var stateCode = state switch
        {
            "Maharashtra" => "27",
            "Gujarat" => "24",
            "Karnataka" => "29",
            "Tamil Nadu" => "33",
            "Uttar Pradesh" => "09",
            "West Bengal" => "19",
            "Rajasthan" => "08",
            "Punjab" => "03",
            _ => "27"
        };

        var pan = new string(faker.Random.Chars('A', 'Z', 5))
                  + faker.Random.Int(1000, 9999)
                  + faker.Random.Char('A', 'Z');

        return $"{stateCode}{pan}{faker.Random.Int(1, 9)}Z{faker.Random.Char('A', 'Z')}";
    }

    private async Task<AppUser> CreateUserAsync(string email, string name, string phone, string role,
        CancellationToken ct)
    {
        var existing = await _users.FindByEmailAsync(email);
        if (existing is not null)
        {
            return existing;
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = name,
            PhoneNumber = phone,
            PhoneNumberConfirmed = true,
            PhoneVerified = true
        };

        var result = await _users.CreateAsync(user, SeedPassword);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create seed user {email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }

        await _users.AddToRoleAsync(user, role);
        return user;
    }
}
