namespace CRM.Api.Models
{
    public class DashboardStats
    {
        public int TotalCsAgent { get; set; }
        public int TotalTechnician { get; set; }
        public int TotalCustomer { get; set; }
        public int TotalTicket { get; set; }
        public int TotalMyTicket { get; set; }
        public int ActiveTicket { get; set; }
        public int SolvedTicket { get; set; } 
        public TicketByStatus? TicketByStatus { get; set; }
        public double? MyAvgResponseTime { get; set; }
        public double? MyAvgResolutionTime { get; set; }
        public TicketByPriority? TicketByPriority { get; set; }
    }

    public class TicketByStatus
    {
        public int Solved { get; set; }
        public int Progress { get; set; }
        public int Waiting { get; set; }
    }

    public class TicketByPriority
    {
        public int Low { get; set; }  
        public int Normal { get; set; }  
        public int High { get; set; }  
        public int Critical { get; set; }  
    }
}