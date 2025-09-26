using Microsoft.AspNetCore.Identity;

namespace PatinaBlazor.Data
{
    // Add profile data for application users by adding properties to the ApplicationUser class
    public class ApplicationUser : IdentityUser
    {
        public DateTime CreatedDate { get; set; }
        public bool IsLocked { get; set; } = false;
    }

}
