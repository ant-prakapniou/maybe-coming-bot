using MaybeComingBot.Models;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace MaybeComingBot.Utils;

public class BotSettingsValidator : IValidateOptions<BotSettings>
{
    public ValidateOptionsResult Validate(string? name, BotSettings options)
    {
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(options);

        if (!Validator.TryValidateObject(options, validationContext, validationResults, true))
        {
            var errors = string.Join("; ", validationResults.Select(r => r.ErrorMessage));
            return ValidateOptionsResult.Fail(errors);
        }

        if (!Uri.TryCreate(options.JsonUrl, UriKind.Absolute, out _))
        {
            return ValidateOptionsResult.Fail("JsonUrl must be a valid absolute URL");
        }

        if (options.AdminUserIds is null || options.AdminUserIds.Length == 0)
        {
            return ValidateOptionsResult.Fail("At least one admin user ID must be specified");
        }

        return ValidateOptionsResult.Success;
    }
}