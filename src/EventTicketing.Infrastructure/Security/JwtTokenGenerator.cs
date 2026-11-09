using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EventTicketing.Core.Constants;
using Microsoft.IdentityModel.Tokens;

namespace EventTicketing.Infrastructure.Security;

public class JwtTokenGenerator
{
    private readonly string _issuer;
    private readonly string _audience;
    private readonly string _secretKey;

    public JwtTokenGenerator(
        string issuer = AntiScalpingConfig.JWT_ISSUER,
        string audience = AntiScalpingConfig.JWT_AUDIENCE,
        string secretKey = AntiScalpingConfig.DEFAULT_JWT_SECRET)
    {
        _issuer = issuer;
        _audience = audience;
        _secretKey = secretKey;
    }

    public string GenerateToken(string userId, string role, string email = "", string fullName = "")
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, fullName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(AntiScalpingConfig.JWT_EXPIRY_MINUTES),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal? ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_secretKey);
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            return handler.ValidateToken(token, parameters, out _);
        }
        catch
        {
            return null;
        }
    }
}
