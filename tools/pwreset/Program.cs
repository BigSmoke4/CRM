using Microsoft.AspNetCore.Identity;
var hasher = new PasswordHasher<object>();
Console.WriteLine(hasher.HashPassword(new object(), args.Length > 0 ? args[0] : "Admin@123"));
