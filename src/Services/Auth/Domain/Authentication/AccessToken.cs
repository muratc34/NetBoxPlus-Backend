namespace Auth.Domain.Authentication;

public class AccessToken
{
    public AccessToken(string token, DateTime expiration)
    {
        Token = token;
        Expiration = expiration;
    }
    public string Token { get; set; }
    public DateTime Expiration { get; set; }

}
