using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Zelo.Api.Domain.Entities;
using Zelo.Api.Domain.Enums;

namespace Zelo.Api.Infrastructure.Security;

public class JwtTokenService(IConfiguration configuration)
{
    public string GetSigningKeyString()
    {
        var envKey = Environment.GetEnvironmentVariable("JWT_SECRET")
                     ?? configuration["JWT_SECRET"]
                     ?? configuration["Jwt:Secret"];

        if (!string.IsNullOrWhiteSpace(envKey) && envKey.Length >= 32)
        {
            return envKey;
        }

        // Chave determinística apenas para ambiente Development/Testing local quando JWT_SECRET não foi injetada
        return "zelo-dev-shared-secret-key-hs256-minimum-32-bytes";
    }

    public (string Token, DateTime ExpiresAtUtc) GenerateToken(Usuario usuario)
    {
        return GenerateToken(usuario.Id, usuario.Papel, usuario.Email, usuario.Nome);
    }

    public (string Token, DateTime ExpiresAtUtc) GenerateServiceToken() =>
        GenerateToken("00000000-0000-0000-0000-000000000005", PapeisUsuario.Servico, "servico@zelo.teste", "zelo-cuidado");

    public (string Token, DateTime ExpiresAtUtc) GenerateToken(string usuarioId, string papel, string? email = null, string? nome = null)
    {
        var keyBytes = Encoding.UTF8.GetBytes(GetSigningKeyString());
        var securityKey = new SymmetricSecurityKey(keyBytes);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var expiresAt = DateTime.UtcNow.AddHours(24);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuarioId),
            new("papel", papel),
            new(ClaimTypes.Role, papel)
        };

        if (!string.IsNullOrWhiteSpace(email))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));
        }

        if (!string.IsNullOrWhiteSpace(nome))
        {
            claims.Add(new Claim("nome", nome));
        }

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"] ?? "zelo-cuidado",
            audience: configuration["Jwt:Audience"] ?? "zelo-services",
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}

public static class ClaimsPrincipalExtensions
{
    public static string GetUsuarioId(this ClaimsPrincipal user) =>
        user.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? string.Empty;

    public static string GetPapel(this ClaimsPrincipal user) =>
        user.FindFirstValue("papel")
        ?? user.FindFirstValue(ClaimTypes.Role)
        ?? string.Empty;
}
