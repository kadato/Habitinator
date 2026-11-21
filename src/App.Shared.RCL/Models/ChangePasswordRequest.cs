using System.ComponentModel.DataAnnotations;

using Microsoft.Extensions.Validation;

namespace App.Shared.RCL.Models;

[ValidatableType]
public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, MinLength(8)] string NewPassword);
