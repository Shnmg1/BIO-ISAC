using api.Models;
using System.ComponentModel.DataAnnotations;

namespace api.DTOs;

public class UpdateProfileRequest
{
    [Required(ErrorMessage = "Full name is required")]
    [StringLength(255, ErrorMessage = "Full name must be 255 characters or less")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Facility name is required")]
    [StringLength(255, ErrorMessage = "Facility name must be 255 characters or less")]
    public string FacilityName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Facility type is required")]
    public FacilityType FacilityType { get; set; }
}

public class ChangePasswordRequest
{
    [Required(ErrorMessage = "Current password is required")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "New password is required")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm password is required")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class NotificationPreferencesRequest
{
    [Required]
    public NotificationDeliveryMode DeliveryMode { get; set; } = NotificationDeliveryMode.Immediate;

    public string? DailyDigestTime { get; set; } // Format: "HH:mm" (e.g., "09:00")

    public string? WeeklyDigestDay { get; set; } // Format: "Monday", "Tuesday", etc.

    public bool ReceiveHighTier { get; set; } = true;

    public bool ReceiveMediumTier { get; set; } = true;

    public bool ReceiveLowTier { get; set; } = false;
}

public enum NotificationDeliveryMode
{
    Immediate,      // Receive notifications immediately
    DailyDigest,    // Receive once per day at specified time
    WeeklyDigest    // Receive once per week on specified day
}


