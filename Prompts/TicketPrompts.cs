namespace CRM.Api.Prompts
{
    /// <summary>
    /// Prompt templates for Stella AI features on tickets
    /// (Stella Summary and Stella Assist).
    /// </summary>
    public static class TicketPrompts
    {
        // {0} = ticket subject, {1} = ticket status, {2} = conversation history,
        // {3} = ticket description
        public const string Summary = """
            Summarize the following customer support conversation concisely in 2-3 sentences.
            Include the main issue discussed and any key updates.
            Detect the language used across the ticket subject, description, and conversation/comments
            below, and respond in that language. If they use different languages, prioritize the
            language used most recently in the conversation/comments.
            Ticket: {0}
            Status: {1}
            Description: {3}
            Conversation:
            {2}
            Summary:
            """;
        // {0} = ticket id, {1} = ticket subject, {2} = conversation history,
        // {3} = ticket description
        public const string Draft = """
            Act as an expert, empathetic, and highly professional customer support agent
            for Enterprise AI CRM. Based on the following support ticket history, generate
            a draft response to the customer. Keep it concise, professional, acknowledge
            their frustration if present, and outline the next steps. Do not include
            placeholders like [Your Name] or [Company Name] if possible, just write the
            core message. Detect the language used across the ticket subject, description, and
            conversation/comments below, and respond in that language. If they use different
            languages, prioritize the language used most recently in the conversation/comments.
            Ticket ID: {0}
            Ticket Subject: {1}
            Ticket Description: {3}
            Conversation History:
            {2}
            Draft Response:
            """;
    }
}