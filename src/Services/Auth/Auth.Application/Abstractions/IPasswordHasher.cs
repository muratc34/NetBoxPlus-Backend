namespace Auth.Application.Abstractions;

/// <summary>Hashes and checks passwords. Implemented with a vetted library, never by hand.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    PasswordCheck Verify(string passwordHash, string password);
}

public enum PasswordCheck
{
    Failed = 0,
    Succeeded,

    /// <summary>Correct, but hashed with older settings: store a new hash of the same password.</summary>
    SucceededRehashNeeded,
}
