namespace CRM.Api.Prompts
{
    /// <summary>
    /// Prompt templates for Stella AI features on tickets
    /// (Stella Summary and Stella Assist).
    /// </summary>
    public static class TicketPrompts
    {
        // {0} = ticket subject, {1} = ticket status, {2} = conversation history,
        // {3} = ticket description, {4} = requester role
        public const string Summary = """
            Summarize the following customer support conversation concisely in 2-3 sentences.
            Include the main issue discussed and any key updates.
            LANGUAGE:
            Detect the language used across the ticket subject, description, and conversation/comments
            below, and respond in that language. Only consider text actually written by the customer
            or agent — ignore automated/system-generated entries (e.g. status-change logs, notification
            templates) when detecting language, since these may be in a fixed system language that does
            not reflect what the participants are actually writing in. If subject, description, and
            conversation/comments use different languages, prioritize the language used most recently
            in a genuine (non-automated) conversation/comment entry; if the conversation/comments
            contain no genuine entries, fall back to the language of the description.
            REQUESTER ROLE:
            This summary is requested by a user with the role: {4}
            Adjust the summary to that audience:
            - If the requester is a customer, use simple, non-technical language and focus on
              what they should know about their issue and what happens next. This is the
              customer's own ticket, so address them directly in second person (e.g. "Anda" /
              "you") instead of referring to them by name in third person.
            - If the requester is an agent, technician, admin, or other staff member, you may
              include operational detail (root issue, actions taken, next steps for the team),
              and refer to the customer by name since staff are reading about someone else's ticket.
            Ticket: {0}
            Status: {1}
            Description: {3}
            Conversation:
            {2}
            EMPTY CONVERSATION:
            If the conversation above is empty (no messages exchanged yet), do NOT make the
            summary consist only of a note like "No messages yet" or "Our team will respond
            shortly". Instead, build the summary from the ticket description above: restate
            the customer's issue in your own words, state that no reply has been sent yet,
            mention the current status ({1}), and close with the next step for the team
            (e.g. respond to the customer). Keep it 2-3 sentences as usual.
            Remember: respond ONLY in the language you detected above from the subject, description,
            and genuine conversation/comment text (not the language of these instructions, and not any
            automated/system-generated text).
            Summary:
            """;
        // {0} = ticket id, {1} = ticket subject, {2} = conversation history,
        // {3} = ticket description, {4} = company name, {5} = requester role
        public const string Draft = """
            Act as an expert, empathetic, and highly professional customer support agent
            for the company named below. Based on the following support ticket history, generate
            a draft response to the customer. Keep it concise, professional, acknowledge
            their frustration if present, and outline the next steps. Do not include
            placeholders like [Your Name]. Detect the language used across the ticket subject,
            description, and conversation/comments below, and respond in that language. If they
            use different languages, prioritize the language used most recently in the
            conversation/comments.
            Output ONLY the raw draft reply text that will be sent directly to the customer.
            Do not include any preamble, meta-commentary, introduction, or explanation such as
            "Here is the draft response:" or its equivalent in any language. Do not wrap the
            output in quotes or markdown. Start directly with the greeting.
            REQUESTER ROLE:
            This draft is being written on behalf of a staff member with the role: {5}
            The reply is always read by the customer, so keep the language simple, friendly, and
            non-technical regardless of this role — do not increase technical depth or jargon for
            a technician or admin requester. Use the role only to naturally frame how the reply is
            voiced (e.g. a technician may briefly mention what was checked or fixed in plain
            terms, an agent may focus more on next steps and reassurance), never to make the
            reply harder for the customer to understand.
            SIGNATURE:
            End the reply with a warm closing appropriate to the detected language (e.g. "Best
            regards," / "Salam hangat," / "Cordialement," / "Saludos cordiales," / "Mit
            freundlichen Grüßen,") followed by a signature line that uses the exact company name
            provided below, naturally localized to that language's convention for a support team
            signature. Always use the exact company name given below verbatim. Never invent a
            company name or use a generic one such as "Enterprise AI CRM".
            Ticket ID: {0}
            Ticket Subject: {1}
            Ticket Description: {3}
            Company: {4}
            Conversation History:
            {2}
            Remember: respond ONLY in the language you detected above from the subject, description,
            and conversation/comments (not the language of these instructions).
            Draft Response:
            """;
    }
}