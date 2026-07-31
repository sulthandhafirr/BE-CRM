namespace CRM.Api.Prompts
{
    public static class ChatToolDefinitions
    {
        public static readonly object[] Tools = new object[]
        {
            new
            {
                type = "function",
                function = new
                {
                    name = "get_ticket_status",
                    description = "Get the status, priority, SLA info, and assigned agent (if any) of a specific ticket by its ID",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            ticket_id = new { type = "string", description = "The ticket ID" }
                        },
                        required = new[] { "ticket_id" }
                    }
                }
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "get_tickets",
                    description = "Get a list of tickets filtered by scope. Use 'mine' for tickets assigned to the current user, 'unassigned' for tickets waiting in queue with no agent, 'others' for tickets assigned to other agents, or 'all' for all tickets in the company.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            scope = new
                            {
                                type = "string",
                                @enum = new[] { "mine", "unassigned", "others", "all" },
                                description = "Which subset of tickets to retrieve"
                            }
                        },
                        required = new[] { "scope" }
                    }
                }
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "get_customer_tier",
                    description = "Get the tier of a customer. Staff (cs agents, technicians, admins, ultra users) can look up any customer by name. Customers can only ask about their own tier.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            customer_name = new
                            {
                                type = "string",
                                description = "The name of the customer to look up. Omit if the user is asking about their own tier."
                            }
                        },
                        required = new string[] { }
                    }
                }
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "get_users",
                    description = "Get the list of users in the company, with their name and role. Optionally filter by role (cs_agent, technician, customer, admin, ultrauser). Use this for questions about who works here, who the customers are, how many of a given role there are, or a breakdown of users by role.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            role = new
                            {
                                type = "string",
                                @enum = new[] { "cs_agent", "technician", "customer", "admin", "ultrauser" },
                                description = "The role to filter by. Omit to get all users regardless of role."
                            }
                        },
                        required = new string[] { }
                    }
                }
            }
        };
    }
}