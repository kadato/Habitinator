namespace App.Web.Models;

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
