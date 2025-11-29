namespace api.Models;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string FacilityName { get; set; } = string.Empty;
    public FacilityType FacilityType { get; set; }
    public UserRole Role { get; set; }
    public UserStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum FacilityType
{
    Hospital,
    Lab,
    Biomanufacturing,
    Agriculture
}

public enum UserRole
{
    User,
    Admin
}

public enum UserStatus
{
    Active,
    Pending,
    Disabled
}


