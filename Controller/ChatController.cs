using Microsoft.AspNetCore.Mvc;
using CRM.Api.Models;
using CRM.Api.Prompts;
using CRM.Api.Helpers;
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
                    new { role = "system", content = ChatPrompts.SystemPrompt }
                };

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
                using var document = JsonDocument.Parse(responseContent);

                if (!TryGetPropertyIgnoreCase(document.RootElement, "choices", out var choicesElement)
                    || choicesElement.ValueKind != JsonValueKind.Array
                    || choicesElement.GetArrayLength() == 0)
                {
                    return BadRequest(new ChatResponse
                    {
                        Success = false,
                        Error = "No response from DeepSeek API"
                    });
                }

                var firstChoice = choicesElement[0];
                if (!TryGetPropertyIgnoreCase(firstChoice, "message", out var messageElement)
                    || messageElement.ValueKind != JsonValueKind.Object)
                {
                    return BadRequest(new ChatResponse
                    {
                        Success = false,
                        Error = "Invalid response shape from DeepSeek API"
                    });
                }

                if (!TryGetPropertyIgnoreCase(messageElement, "content", out var contentElement))
                {
                    return BadRequest(new ChatResponse
                    {
                        Success = false,
                        Error = "DeepSeek message content is missing"
                    });
                }

                var assistantMessage = contentElement.GetString() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(assistantMessage))
                {
                    return BadRequest(new ChatResponse
                    {
                        Success = false,
                        Error = "DeepSeek returned empty content"
                    });
                }

                // Strip markdown formatting from the response
                assistantMessage = MarkdownStripper.Strip(assistantMessage);

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

        private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }
}
