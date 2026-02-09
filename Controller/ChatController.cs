using Microsoft.AspNetCore.Mvc;
using CRM.Api.Models;
using System.Text;
using System.Text.Json;

namespace CRM.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public ChatController(IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        [HttpPost]
        public async Task<ActionResult<ChatResponse>> SendMessage([FromBody] ChatRequest request)
        {
            try
            {
                var apiKey = _configuration["deepseek_api"];
                if (string.IsNullOrEmpty(apiKey))
                {
                    return BadRequest(new ChatResponse 
                    { 
                        Success = false, 
                        Error = "DeepSeek API key not configured" 
                    });
                }

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                var messages = new List<object>
                {
                    new { role = "system", content = "You are a helpful CRM AI assistant. You help users with customer relationship management tasks, ticket management, and general CRM queries. Be concise and professional." }
                };

                // Add history if available
                if (request.History != null && request.History.Any())
                {
                    foreach (var msg in request.History)
                    {
                        messages.Add(new { role = msg.Role, content = msg.Content });
                    }
                }

                // Add current message
                messages.Add(new { role = "user", content = request.Message });

                var payload = new
                {
                    model = "deepseek-chat",
                    messages = messages,
                    temperature = 0.7,
                    max_tokens = 500
                };

                var jsonContent = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://api.deepseek.com/v1/chat/completions", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return BadRequest(new ChatResponse 
                    { 
                        Success = false, 
                        Error = $"DeepSeek API error: {errorContent}" 
                    });
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var deepseekResponse = JsonSerializer.Deserialize<DeepSeekResponse>(responseContent);

                if (deepseekResponse?.Choices == null || !deepseekResponse.Choices.Any())
                {
                    return BadRequest(new ChatResponse 
                    { 
                        Success = false, 
                        Error = "No response from DeepSeek API" 
                    });
                }

                var assistantMessage = deepseekResponse.Choices[0].Message.Content;

                return Ok(new ChatResponse 
                { 
                    Message = assistantMessage, 
                    Success = true 
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ChatResponse 
                { 
                    Success = false, 
                    Error = $"Internal server error: {ex.Message}" 
                });
            }
        }
    }

    // DeepSeek API Response Models
    public class DeepSeekResponse
    {
        public List<Choice>? Choices { get; set; }
    }

    public class Choice
    {
        public Message Message { get; set; } = new Message();
    }

    public class Message
    {
        public string Content { get; set; } = string.Empty;
    }
}
