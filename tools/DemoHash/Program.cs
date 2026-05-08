using Microsoft.AspNetCore.Identity;

if (args.Length == 0 || string.IsNullOrEmpty(args[0]))
{
    Console.Error.WriteLine("Usage: DemoHash <plain-password>");
    Environment.Exit(1);
}

var hasher = new PasswordHasher<IdentityUser<Guid>>();
Console.Write(hasher.HashPassword(new IdentityUser<Guid>(), args[0]));
