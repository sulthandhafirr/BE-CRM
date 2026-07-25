namespace CRM.Api.Models
{
    public class RoleResponse
    {
        public int Id { get; set; }
        public string Role { get; set; } = string.Empty;
        public int CompanyId { get; set; }
        public bool IsSystem { get; set; }
        public int UserCount { get; set; }
        public Dictionary<string, bool> Permissions { get; set; } = new();
    }

    public class RoleUpdateDto
    {
        public string? Role { get; set; }
        public Dictionary<string, bool> Permissions { get; set; } = new();
    }

    public class RoleCreateDto
    {
        public string Role { get; set; } = string.Empty;
        public Dictionary<string, bool> Permissions { get; set; } = new();
    }
}
