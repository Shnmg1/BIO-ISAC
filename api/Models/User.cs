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
    
    // 2FA fields
    public string? TotpSecret { get; set; }
    public bool IsTwoFactorEnabled { get; set; }
    public DateTime? TwoFactorEnabledAt { get; set; }
    
    // Document verification fields
    public string? WorkIdPath { get; set; }
    public string? ProfessionalLicensePath { get; set; }
    public string? SupervisorLetterPath { get; set; }
    
    // AI Analysis
    public double? AiConfidenceScore { get; set; }
    public string? AiAnalysisResult { get; set; }
    public bool RequiresManualReview { get; set; }
    
    // Admin review
    public string? RejectionReason { get; set; }
    public int? ReviewedByAdminId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
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
    Disabled,
    Rejected
}


