using System.ComponentModel.DataAnnotations;
using SchoolSearch.Api.Errors;
using SchoolSearch.Api.Models;

namespace SchoolSearch.Api.Filters;

/// <summary>
/// Runs DataAnnotations on request objects that implement <see cref="IValidatedRequest"/> and
/// returns the uniform 400 shape on failure. Lists are clamped, not rejected (see CatalogEndpoints),
/// so this filter only guards values that must be refused outright, such as a malformed slug.
/// </summary>
public sealed class ValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        foreach (var argument in context.Arguments)
        {
            if (argument is not IValidatedRequest)
            {
                continue;
            }

            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(argument, new ValidationContext(argument), results, validateAllProperties: true))
            {
                var message = string.Join("; ", results.Select(r => r.ErrorMessage));
                return ApiErrorWriter.Result(
                    StatusCodes.Status400BadRequest,
                    "validation_failed",
                    message.Length > 0 ? message : "Request is invalid.");
            }
        }

        return await next(context);
    }
}
