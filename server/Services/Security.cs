using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CoachingApp.Data;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
namespace CoachingApp.Services;

public static class Security
{
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var h = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(salt) + "." + Convert.ToBase64String(h);
    }
    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        var p = stored.Split('.');
        if (p.Length != 2) return false;
        var h = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(p[0]), 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(h, Convert.FromBase64String(p[1]));
    }
}

public class TokenService(IConfiguration cfg)
{
    string Create(IEnumerable<Claim> claims)
    {
        var j = cfg.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(j["Key"]!));
        var token = new JwtSecurityToken(j["Issuer"], j["Audience"], claims,
            expires: DateTime.UtcNow.AddDays(60), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    public string ForAdmin(AdminUser a) => Create(new[] {
        new Claim(ClaimTypes.NameIdentifier, a.Id.ToString()), new Claim(ClaimTypes.Name, a.FullName), new Claim(ClaimTypes.Role, "Admin") });
    public string ForStudent(Student s) => Create(new[] {
        new Claim(ClaimTypes.NameIdentifier, s.Id.ToString()), new Claim("sid", s.Id.ToString()),
        new Claim(ClaimTypes.Name, s.Name), new Claim(ClaimTypes.Role, "Student") });
}

public class FileStore(IWebHostEnvironment env)
{
    static readonly string[] Ok = { ".jpg", ".jpeg", ".png", ".webp" };
    public async Task<string> SaveAsync(Stream s, string fileName, string folder)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!Ok.Contains(ext)) throw new InvalidOperationException("Sirf JPG / PNG / WEBP image allowed hai");
        var root = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        var dir = Path.Combine(root, "uploads", folder);
        Directory.CreateDirectory(dir);
        var name = $"{Guid.NewGuid():N}{ext}";
        await using var fs = File.Create(Path.Combine(dir, name));
        await s.CopyToAsync(fs);
        return $"/uploads/{folder}/{name}";
    }
}
