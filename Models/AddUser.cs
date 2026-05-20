namespace CRM.Api.Models
{
    public class AddUserRequest
    {
        public Guid AuthUserId { get; set; }      // ← dari frontend
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int RoleId { get; set; }
        public string? Position { get; set; }
        // Password tidak perlu — sudah dihandle frontend
    }

    public class AddUserResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public AddedUserDto? User { get; set; }
    }

    public class AddedUserDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int RoleId { get; set; }
        public string? Position { get; set; }
        public int? CompanyId { get; set; }
    }
}