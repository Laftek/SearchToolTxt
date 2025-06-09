using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace WpfBlazorSearchTool.Services
{
    /// <summary>
    /// Validates that a list property is not null and contains at least one element.
    /// Can be used conditionally based on the value of another boolean property.
    /// </summary>
    public class ListMustContainElementsAttribute : ValidationAttribute
    {
        private readonly string? _boolPropertyName;
        private readonly bool _expectedValue;

        /// <summary>
        /// Unconditionally validates that the list has elements.
        /// </summary>
        public ListMustContainElementsAttribute()
        {
            // _boolPropertyName will be null, indicating unconditional check.
        }

        /// <summary>
        /// Conditionally validates that the list has elements.
        /// </summary>
        /// <param name="boolPropertyName">The name of the boolean property that dictates if validation should occur.</param>
        /// <param name="expectedValue">The required value of the boolean property for this validation to be enforced.</param>
        public ListMustContainElementsAttribute(string boolPropertyName, bool expectedValue)
        {
            _boolPropertyName = boolPropertyName;
            _expectedValue = expectedValue;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            // --- Conditional Check (if configured) ---
            if (!string.IsNullOrEmpty(_boolPropertyName))
            {
                PropertyInfo? boolProperty = validationContext.ObjectType.GetProperty(_boolPropertyName);

                if (boolProperty == null)
                {
                    return new ValidationResult($"Internal Error: Unknown property '{_boolPropertyName}'");
                }

                var boolPropertyValue = (bool?)boolProperty.GetValue(validationContext.ObjectInstance);

                // If the condition for validation is NOT met, we skip the validation and it passes.
                if (boolPropertyValue != _expectedValue)
                {
                    return ValidationResult.Success;
                }
            }

            // --- Core List Validation ---
            // This part runs if the check is unconditional, or if the conditional check passed.
            if (value is IList list && list.Count > 0)
            {
                return ValidationResult.Success;
            }

            // If we reach here, the list is empty or null, so validation fails.
            return new ValidationResult(ErrorMessage ?? $"{validationContext.DisplayName} must contain at least one item.");
        }
    }
}