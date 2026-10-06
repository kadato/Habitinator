using System.ComponentModel.DataAnnotations;

namespace App.Shared.RCL.Models;

public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, MinLength(8)] string NewPassword);
