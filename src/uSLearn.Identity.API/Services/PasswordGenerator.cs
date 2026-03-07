namespace uSLearn.Identity.Services
{
    public class PasswordGenerator : IPasswordGenerator
    {
        public string GenerateTemporaryPassword()
        {
            // Simple temporary password generation
            return $"Temp{Guid.NewGuid().ToString("N")[..8]}!";
        }
    }
}
