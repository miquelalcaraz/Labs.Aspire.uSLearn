namespace uSLearn.Identity.Models
{
    public class AdminUser
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string TemporaryPassword { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
