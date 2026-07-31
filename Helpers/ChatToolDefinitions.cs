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
                            ticket_id = new { type = "string", description = "The ticket ID" },
                            start_date = new { type = "string", description = "Start date in YYYY-MM-DD format, to filter tickets created on or after this date" },
                            end_date = new { type = "string", description = "End date in YYYY-MM-DD format, to filter tickets created on or before this date" }
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
                            },
                            status = new
                            {
                                type = "string",
                                @enum = new[] { "Waiting", "Progress", "Solved" },
                                description = "Filter tickets by status"
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
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "get_ticket_stats",
                    description = "Get ticket statistics: status breakdown (solved/progress/waiting), priority breakdown (low/normal/high/critical), or counts of solved/active tickets. Scoped to the current user's own tickets if they are a customer or technician, or company-wide if cs_agent/admin.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            metric = new
                            {
                                type = "string",
                                @enum = new[] { "status_breakdown", "priority_breakdown", "solved_count", "active_count" },
                                description = "Which statistic to retrieve"
                            },
                            period = new
                            {
                                type = "string",
                                @enum = new[] { "today", "yesterday", "this_week", "last_week", "this_month", "last_month", "this_year", "last_year", "all_time" },
                                description = "Time range to filter tickets by creation date. Defaults to all_time if omitted."
                            },
                            start_date = new { type = "string", description = "Start date in YYYY-MM-DD format, to filter tickets created on or after this date" },
                            end_date = new { type = "string", description = "End date in YYYY-MM-DD format, to filter tickets created on or before this date" }
                        },
                        required = new[] { "metric" }
                    }
                }
            }
        };
    }
}