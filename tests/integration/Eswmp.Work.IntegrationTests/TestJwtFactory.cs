using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Eswmp.Shared.Auth;
using Microsoft.IdentityModel.Tokens;

namespace Eswmp.Work.IntegrationTests;

/// <summary>
/// Mints JWTs signed with the same symmetric key/issuer/audience WorkApiFactory
/// configures the test host with — see AddEswmpAuthentication (Jwt:SecretKey/Issuer/Audience).
/// </summary>
public static class TestJwtFactory
{
    public const string SecretKey = "integration-test-signing-key-min-64-bytes-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public const string Issuer = "eswmp-test";
    public const string Audience = "eswmp-api-test";

    public static string CreateToken(Guid tenantId, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(EswmpClaimTypes.TenantId, tenantId.ToString()),
            new(EswmpClaimTypes.UserId, Guid.NewGuid().ToString()),
        };

        if (permissions.Length > 0)
        {
            claims.Add(new Claim(EswmpClaimTypes.Permissions, string.Join(',', permissions)));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
