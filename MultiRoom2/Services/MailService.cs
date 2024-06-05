namespace MultiRoom2.Services;

public class MailService
{
    public void SendMail(string email, string title, string body)
    {
        Console.WriteLine($"Email to {email} title={title}, body={body}");
    }
}