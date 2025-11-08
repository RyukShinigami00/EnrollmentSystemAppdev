using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace UserRoles.Models
{
    public class Users : IdentityUser
    {
        public string FullName { get; set; } = string.Empty;

        [StringLength(20)]
        public string Role { get; set; } = "student";

        // Professor-specific fields
        [StringLength(10)]
        public string? AssignedGradeLevel { get; set; }

        public int? AssignedSection { get; set; }

        // Security properties
        public string? PasswordHistory { get; set; }
        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockoutEndTime { get; set; }
        public DateTime? LastPasswordChange { get; set; }
    }
}