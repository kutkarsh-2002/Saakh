using FluentValidation;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Saakh.Api.Services;

namespace Saakh.Api.Validators;

public class RegisterDtoValidator : AbstractValidator<RegisterDto>
{
    public RegisterDtoValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Enter an email address.")
            .EmailAddress().WithMessage("That email address does not look right.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Choose a password.")
            .MinimumLength(8).WithMessage("Use at least 8 characters.");

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Enter your business or personal name.")
            .MaximumLength(200);

        RuleFor(x => x.Role).IsInEnum().WithMessage("Pick whether you are a Lender or a Seeker.");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Enter your mobile number.")
            .Must(p => p.Count(char.IsDigit) is >= 10 and <= 13)
            .WithMessage("Enter a 10-digit mobile number.");

        RuleFor(x => x.OtpCode)
            .NotEmpty().WithMessage("Enter the 6-digit code we sent to your phone.");

        // GSTIN is optional, but a supplied one has to be well formed before it is worth a
        // registry call (tech-stack.md). The same pattern runs in the Angular form.
        RuleFor(x => x.Gstin!)
            .Matches(GstinFormat.Pattern)
            .WithMessage("A GSTIN is 15 characters, like 27AAPFU0939F1ZV.")
            .When(x => !string.IsNullOrWhiteSpace(x.Gstin));

        RuleFor(x => x.Country).NotEmpty().WithMessage("Pick a country.");
        RuleFor(x => x.State).NotEmpty().WithMessage("Pick a state.");
        RuleFor(x => x.District).NotEmpty().WithMessage("Pick a district.");

        RuleFor(x => x.Category).IsInEnum().WithMessage("Pick what you deal in.");

        RuleFor(x => x.CapacityMin)
            .GreaterThanOrEqualTo(0).WithMessage("Capacity cannot be negative.");

        RuleFor(x => x.CapacityMax)
            .GreaterThanOrEqualTo(x => x.CapacityMin)
            .WithMessage("The upper end of the range cannot be below the lower end.");

        RuleFor(x => x.CapacityUnit).NotEmpty().MaximumLength(32);

        RuleFor(x => x.OwnTradeDescription)
            .NotEmpty().WithMessage("Tell counterparties what trade you are in.")
            .When(x => x.Role == ProfileRole.Seeker);
    }
}

public class LoginDtoValidator : AbstractValidator<LoginDto>
{
    public LoginDtoValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class GstinCheckDtoValidator : AbstractValidator<GstinCheckDto>
{
    public GstinCheckDtoValidator()
    {
        RuleFor(x => x.Gstin)
            .NotEmpty().WithMessage("Enter a GSTIN.")
            .Matches(GstinFormat.Pattern)
            .WithMessage("A GSTIN is 15 characters, like 27AAPFU0939F1ZV.");
    }
}

public class OtpRequestDtoValidator : AbstractValidator<OtpRequestDto>
{
    public OtpRequestDtoValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Enter your mobile number.")
            .Must(p => p.Count(char.IsDigit) is >= 10 and <= 13)
            .WithMessage("Enter a 10-digit mobile number.");
    }
}

public class OtpVerifyDtoValidator : AbstractValidator<OtpVerifyDto>
{
    public OtpVerifyDtoValidator()
    {
        RuleFor(x => x.Phone).NotEmpty();
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Enter the code we sent you.")
            .Length(6).WithMessage("The code is 6 digits.");
    }
}

public class UpdateProfileDtoValidator : AbstractValidator<UpdateProfileDto>
{
    public UpdateProfileDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Country).NotEmpty();
        RuleFor(x => x.State).NotEmpty();
        RuleFor(x => x.District).NotEmpty();
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.CapacityMin).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CapacityMax).GreaterThanOrEqualTo(x => x.CapacityMin)
            .WithMessage("The upper end of the range cannot be below the lower end.");
        RuleFor(x => x.CapacityUnit).NotEmpty().MaximumLength(32);
    }
}

public class SendInterestDtoValidator : AbstractValidator<SendInterestDto>
{
    public SendInterestDtoValidator()
    {
        RuleFor(x => x.ToProfileId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public class RaiseTicketDtoValidator : AbstractValidator<RaiseTicketDto>
{
    public RaiseTicketDtoValidator()
    {
        RuleFor(x => x.InterestId).NotEmpty();

        RuleFor(x => x.Category).IsInEnum().WithMessage("Pick a deal category.");

        RuleFor(x => x.Capacity)
            .GreaterThan(0).WithMessage("Enter the amount or quantity for this deal.");

        RuleFor(x => x.CapacityUnit).NotEmpty().MaximumLength(32);

        // Raw Material tickets carry quantity, unit and a material description; Money
        // tickets carry amount and currency (spec, Ticket fields).
        RuleFor(x => x.MaterialDescription)
            .NotEmpty().WithMessage("Describe the material being supplied.")
            .MaximumLength(500)
            .When(x => x.Category == DealCategory.RawMaterial);

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Write a short description of the arrangement.")
            .MaximumLength(2000);

        RuleFor(x => x.EstimatedSettlementTime)
            .GreaterThan(DateTimeOffset.UtcNow.AddDays(-1))
            .WithMessage("The estimated settlement date needs to be in the future.");
    }
}

public class SubmitRatingDtoValidator : AbstractValidator<SubmitRatingDto>
{
    public SubmitRatingDtoValidator()
    {
        RuleFor(x => x.Stars).InclusiveBetween(1, 5).WithMessage("Pick between 1 and 5 stars.");
        RuleFor(x => x.Comment).MaximumLength(1000);
    }
}

public class SendMessageDtoValidator : AbstractValidator<SendMessageDto>
{
    public SendMessageDtoValidator()
    {
        RuleFor(x => x.Body)
            .NotEmpty().WithMessage("Type a message.")
            .MaximumLength(4000);
    }
}

public class ReviewVerificationDtoValidator : AbstractValidator<ReviewVerificationDto>
{
    public ReviewVerificationDtoValidator()
    {
        RuleFor(x => x.RejectionReason)
            .NotEmpty().WithMessage("Give a reason. It is shown to the user so they can resubmit.")
            .MaximumLength(1000)
            .When(x => !x.Approve);
    }
}

public class ModerationActionDtoValidator : AbstractValidator<ModerationActionDto>
{
    public ModerationActionDtoValidator()
    {
        RuleFor(x => x.ActionType).IsInEnum();

        RuleFor(x => x.SuspensionDuration)
            .NotNull().WithMessage("Pick a suspension window.")
            .When(x => x.ActionType == AdminActionType.Suspend);

        RuleFor(x => x.Notes)
            .NotEmpty().WithMessage("A warning needs a note explaining it.")
            .When(x => x.ActionType == AdminActionType.Warning);

        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
