using Gst;
using MultiRoom;
using MultiRoom2.Services;
using WebSocketSharp.Net;
using WebSocketSharp.Server;

namespace MultiRoom2.Controllers;

public class DispatchController
{
    private const string Location = @"C:\Users\danil\RiderProjects\MyGstreamerApp\MultiRoom2\Web";
    
    private readonly AppContext db = new();

    private readonly AuthService AuthService;
    private readonly RoomService RoomService;

    public DispatchController()
    {
        AuthService = new AuthService(db);
        RoomService = new RoomService(db);
    }

    public void DispatchRequest(HttpRequestEventArgs ea)
    {
        DoDispatch(ea.Request, ea.Response);
        ea.Response.Close();
    }

    private void DoDispatch(HttpListenerRequest req, HttpListenerResponse response)
    {
        if (!AuthService.IsAccessAllowed(req))
        {
            response.StatusCode = 401;
            return;
        }

        switch (req.HttpMethod)
        {
            case "POST":
                OnPost(req, response);
                break;
            case "GET":
                OnGet(req, response);
                break;
            default:
                response.StatusCode = (int) HttpStatusCode.MethodNotAllowed;
                return;
        }
    }

    private void OnGet(HttpListenerRequest req, HttpListenerResponse response)
    {
        var urlPath = req.Url.AbsolutePath;

        if (urlPath.StartsWith("/room/"))
        {
            var exists = RoomService.GetRoom(req, response);
            if (!exists) return;
            GetStaticFile("/room.html", response);
        }
        else if (urlPath.Length == 0 || urlPath == "/")
        {
            GetStaticFile("/index.html", response);
        }
        else {
            GetStaticFile(req.Url.AbsolutePath, response);
        }
    }

    private void OnPost(HttpListenerRequest req, HttpListenerResponse response)
    {
        var urlPath = req.Url.AbsolutePath;

        switch (urlPath)
        {
            case "/room/create":
                RoomService.CreateRoom(req, response);
                break;
            case "/auth":
                AuthService.Login(req, response);
                break;
            case "/register":
                AuthService.Register(req, response);
                break;
            default:
                response.StatusCode = (int)HttpStatusCode.NotFound;
                break;
        }
    }

    private static void GetStaticFile(string urlPath, HttpListenerResponse response)
    {
        var path = Location + urlPath.Replace("/", "\\");
        
        if (File.Exists(path))
        {
            response.ContentLength64 = new FileInfo(path).Length;
            if (path.EndsWith(".css"))
            {
                response.ContentType = "text/css";
            }
            else if (path.EndsWith(".js"))
            {
                response.ContentType = "application/javascript";
            }
            else if (path.EndsWith(".mp4"))
            {
                response.ContentType = "video/mp4";
                response.SendChunked = false;
                response.AddHeader("Content-disposition", "attachment; filename=" + Path.GetFileName(path));
                using FileStream videoStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                byte[] buffer = new byte[64 * 1024];
                int bytesRead = 0;
                while ((bytesRead = videoStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    response.OutputStream.Write(buffer, 0, bytesRead);
                    response.OutputStream.Flush();
                }

                return;
            }
            else
            {
                response.ContentType = "text/html";
            }

            using var localFile = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            localFile.CopyTo(response.OutputStream);
        }
        else
        {
            response.StatusCode = 404;
        }
    }

    public Client DispatchWebSocket()
    {
        return RoomService.CreateClient();
    }

    public Dictionary<string, string> GetQueryParams(HttpListenerRequest req)
    {
        var query = req.Url.Query;
        var queryTokens = new Dictionary<string, string>();
        if (query != "")
        {
            var tokens = query[1..].Split("&");
            foreach (var token in tokens)
            {
                var keyVal = token.Split("=");
                queryTokens[keyVal[0]] = keyVal[1];
            }
        }

        return queryTokens;
    }
}