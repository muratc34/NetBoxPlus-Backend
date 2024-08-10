using Auth.Application.Abstractions;
using Auth.Domain.Authentication;
using Auth.Domain.Users;
using Auth.Infrastructure.Security.Encryption;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Auth.Infrastructure.Security;

public class JwtProvider : IJwtProvider
{
    public IConfiguration Configuration { get; }
    private readonly TokenOptions _options;
    private DateTime _accessTokenExpiration;

    public JwtProvider(IConfiguration configuration)
    {
        Configuration = configuration;
        _options = Configuration.GetSection("TokenOptions").Get<TokenOptions>();
    }

    public AccessToken CreateToken(User user)
    {
        _accessTokenExpiration = DateTime.Now.AddDays(_options.AccessTokenExpiration);
        var securityKey = SecurityKeyHelper.CreateSecurityKey(_options.SecurityKey);
        var signingCredentials = SigningCredentialsHelper.CreateSigningCredentials(securityKey);
        var jwt = CreateJwtSecurityToken(_options, user, signingCredentials);

        string token = new JwtSecurityTokenHandler()
            .WriteToken(jwt);

        return new AccessToken(token, _accessTokenExpiration);
    }

    private JwtSecurityToken CreateJwtSecurityToken(TokenOptions tokenOptions, User user,
            SigningCredentials signingCredentials)
    {
        var claims = new Claim[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email.Value),
        };

        var jwt = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.Now,
            expires: _accessTokenExpiration,
            signingCredentials: signingCredentials
        );
        return jwt;
    }
}
