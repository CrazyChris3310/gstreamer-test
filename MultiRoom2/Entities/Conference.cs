namespace MultiRoom2.Entities;

public class Conference
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; } = "";
    public DateTime? CreationStamp { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int MaxUsersOnline { get; set; } = 0;
    public bool isPublic { get; set; } = false;
}