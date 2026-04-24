namespace EleaveAPI.Models;

public class LeaveRequest
{
    public int UserId { get; set; }
    public int LeaveTypeId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}