using System.Text;
using MultiRoom;
using MultiRoom2.Entities;
using Newtonsoft.Json;
using WebSocketSharp.Net;

namespace MultiRoom2.Services;

public class AuthService(DbContext db)
{
    private static readonly List<string> AllowedUrls = ["/login.html", "/register", "/auth"];
    
    public void Register(HttpListenerRequest req, HttpListenerResponse resp)
    {
        using var memoryStream = new MemoryStream();
        req.InputStream.CopyTo(memoryStream);
        var body = memoryStream.ToArray();
        var bodyContent = Encoding.UTF8.GetString(body);
        var regData = JsonConvert.DeserializeObject<RegistrationRequest>(bodyContent)!;

        var user = db.Users.FirstOrDefault(it => it.Name == regData.Username);
        if (user != null)
        {
            resp.StatusCode = (int)HttpStatusCode.Conflict;
            return;
        }

        user = new User()
        {
            Name = regData.Username,
            Password = regData.Password
        };
        db.Users.Add(user);
        db.SaveChanges();
        
        resp.StatusCode = (int)HttpStatusCode.OK;
    }
    
    public void Login(HttpListenerRequest req, HttpListenerResponse resp)
    {
        using var memoryStream = new MemoryStream();
        req.InputStream.CopyTo(memoryStream);
        var body = memoryStream.ToArray();
        var bodyContent = Encoding.UTF8.GetString(body);
        var authData = JsonConvert.DeserializeObject<RegistrationRequest>(bodyContent)!;

        var user = db.Users.FirstOrDefault(it => it.Name == authData.Username);
        if (user == null)
        {
            resp.StatusCode = (int)HttpStatusCode.Unauthorized;
            return;
        }
        
        resp.StatusCode = (int)HttpStatusCode.OK;
        resp.AppendCookie(new Cookie("authentication", $"{user.Id}/{user.Name}"));
    }

    public bool IsAccessAllowed(HttpListenerRequest req)
    {
        if (AllowedUrls.Contains(req.Url.AbsolutePath) ||
            req.Url.AbsolutePath.EndsWith(".js") || req.Url.AbsolutePath.EndsWith(".css")) return true;

        var authHeader = req.Headers["Authorization"];
        if (req.Cookies["authentication"]?.Value != null)
        {
            return true;
        }

        if (authHeader == null || !authHeader.StartsWith("Basic: "))
        {
            return false;
        }

        var tokenString = authHeader[7..];
        var credentials = tokenString.Split("/");

        var user = db.Users.FirstOrDefault(it => it.Name == credentials[0]);
        if (user == null || user.Password != credentials[1])
        {
            return false;
        }

        return true;
    }
}