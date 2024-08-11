namespace Auth.Application.Abstractions;

public interface IPasswordHasher
{
    void CreatePasswordHash(string password, out byte[] passwordHash, out byte[] passwordSalt);
    bool VerifyPasswordHash(string password, byte[] passwordHash, byte[] passwordSalt);
    void ChangePassword(string password, out byte[] passwordHash, byte[] passwordSalt);
}
