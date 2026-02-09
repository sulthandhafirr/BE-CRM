namespace CRM.Api.Models
{
    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
        public List<ChatMessage>? History { get; set; }
    }

    public class ChatMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public class ChatResponse
    {
        public string Message { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? Error { get; set; }
    }
}
