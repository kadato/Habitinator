namespace App.Shared.RCL.Models;

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
