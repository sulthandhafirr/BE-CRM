using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using CRM.Api.Models;
using CRM.Api.Prompts;
using CRM.Api.Helpers;
using CRM.Api.Services;
using CRM.Api.Data;
using System.Text;
using System.Text.Json;
using System.Security.Claims;

namespace CRM.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : BaseController
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AppDbContext _db;
        private readonly ChatToolService _chatToolService;

        public ChatController(IConfiguration configuration, IHttpClientFactory httpClientFactory, RoleService roleService, AppDbContext db, ChatToolService chatToolService)
            : base(roleService)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _db = db;
            _chatToolService = chatToolService;
        }

        [HttpPost]
        public async Task<ActionResult<ChatResponse>> SendMessage([FromBody] ChatRequest request)
        {
            
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var userContext = await _chatToolService.GetUserContextAsync(userId, role);
            var currentDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var dateContext = $"IMPORTANT: Today's actual date is {currentDate}. Always use this exact date for any date-based reasoning (e.g. 'this month', 'today', 'this week', 'recent'). Do not infer the current date from ticket data - always trust this stated date.";

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
                    new { role = "system", content = ChatPrompts.SystemPrompt + "\n\n" + dateContext + "\n\n" + userContext}
                };

                // Add current message
                messages.Add(new { role = "user", content = request.Message });

                var tools = ChatToolDefinitions.Tools;

                var payload = new
                {
                    model = "deepseek-chat",
                    messages = messages,
                    tools = tools,
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

                if (TryGetPropertyIgnoreCase(messageElement, "tool_calls", out var toolCallsElement)
                    && toolCallsElement.ValueKind == JsonValueKind.Array
                    && toolCallsElement.GetArrayLength() > 0)
                {
                    // DeepSeek wants to call a tool
                    var toolCall = toolCallsElement[0];
                    var toolCallId = toolCall.GetProperty("id").GetString();
                    var funcName = toolCall.GetProperty("function").GetProperty("name").GetString();
                    var funcArgsJson = toolCall.GetProperty("function").GetProperty("arguments").GetString();

                    string toolResult = await _chatToolService.ExecuteToolAsync(funcName!, funcArgsJson, userId, role, companyId);

                    // Send the tool result back to DeepSeek for a final natural language answer
                    var followUpMessages = new List<object>
                    {
                        new { role = "system", content = ChatPrompts.SystemPrompt + "\n\n" + dateContext + "\n\n" + userContext },
                        new { role = "user", content = request.Message },
                        new
                        {
                            role = "assistant",
                            content = (string?)null,
                            tool_calls = new[]
                            {
                                new
                                {
                                    id = toolCallId,
                                    type = "function",
                                    function = new { name = funcName, arguments = funcArgsJson }
                                }
                            }
                        },
                        new
                        {
                            role = "tool",
                            tool_call_id = toolCallId,
                            content = toolResult
                        }
                    };

                    var followUpPayload = new
                    {
                        model = "deepseek-chat",
                        messages = followUpMessages,
                        temperature = 0.7,
                        max_tokens = 500
                    };

                    var followUpContent = new StringContent(JsonSerializer.Serialize(followUpPayload), Encoding.UTF8, "application/json");
                    var followUpResponse = await client.PostAsync("https://api.deepseek.com/v1/chat/completions", followUpContent);
                    var followUpResponseContent = await followUpResponse.Content.ReadAsStringAsync();

                    using var followUpDoc = JsonDocument.Parse(followUpResponseContent);
                    var followUpMessage = followUpDoc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString() ?? "";

                    return Ok(new ChatResponse
                    {
                        Message = MarkdownStripper.Strip(followUpMessage),
                        Success = true
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
