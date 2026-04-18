using Kun.Validation;

namespace Kun.Sample.Catalog.Application.Commands;

/// <summary>Validates <see cref="CreateProductCommand"/> before the handler runs.</summary>
public sealed class CreateProductCommandValidator : IValidator<CreateProductCommand>
{
    /// <inheritdoc/>
    public ValueTask<ValidationResult> ValidateAsync(
        CreateProductCommand instance,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(instance.Name))
            errors.Add(new ValidationError(nameof(instance.Name), "Name.Required", "Product name is required."));
        else if (instance.Name.Length > 200)
            errors.Add(new ValidationError(nameof(instance.Name), "Name.TooLong", "Product name must be 200 characters or fewer."));

        if (instance.Price < 0)
            errors.Add(new ValidationError(nameof(instance.Price), "Price.Negative", "Price must be zero or greater."));

        if (instance.CategoryId == Guid.Empty)
            errors.Add(new ValidationError(nameof(instance.CategoryId), "CategoryId.Empty", "A valid category ID is required."));

        return errors.Count == 0
            ? ValueTask.FromResult(ValidationResult.Success)
            : ValueTask.FromResult(ValidationResult.Failure(errors));
    }
}
