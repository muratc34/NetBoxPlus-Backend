using Auth.Application.Abstractions;
using System.Text;

namespace Auth.Infrastructure.Security.Hashing;

public class PasswordHasher : IPasswordHasher
{
    public void CreatePasswordHash(string password, out byte[] passwordHash, out byte[] passwordSalt)
    {
        using (var hmac = new System.Security.Cryptography.HMACSHA512())
        {
            passwordSalt = hmac.Key;
            passwordHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
        }
    }
    public bool VerifyPasswordHash(string password, byte[] passwordHash, byte[] passwordSalt)
    {
        using (var hmac = new System.Security.Cryptography.HMACSHA512(passwordSalt))
        {
            var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
            for (int i = 0; i < computedHash.Length; i++)
            {
                if (computedHash[i] != passwordHash[i])
                {
                    return false;
                }
            }
        }
        return true;
    }

    public void ChangePassword(string password, out byte[] passwordHash, byte[] passwordSalt)
    {        
        using (var hmac = new System.Security.Cryptography.HMACSHA512())
        {
            hmac.Key = passwordSalt;
            passwordHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
        }
    }
}
