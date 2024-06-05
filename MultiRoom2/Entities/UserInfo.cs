namespace MultiRoom2.Entities;

public class UserInfo
{
    public long Id { get; set; }
    public string? Login { get; set; }
    public string? LoginKey { get; set; }
    public string? Password { get; set; }
    public DateTime RegistrationStamp { get; set; }
    public DateTime LastLoginStamp { get; set; }
    public string? PasswordHash { get; set; }
    public string? HashSalt { get; set; }
    public string? Email { get; set; }
    public bool Activated { get; set; }
    public string? LastToken { get; set; }
    public DateTime? LastTokenStamp { get; set; }
    public DbUserTokenKind? LastTokenKind { get; set; }
    public bool IsDeleted { get; set; }
}

public enum DbUserTokenKind
{
    Activation,
    AccessRestore
}