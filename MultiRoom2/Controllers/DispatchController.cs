using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Gst;
using MultiRoom;
using MultiRoom2.Database;
using MultiRoom2.Services;
using WebApplication1;
using WebSocketSharp;
using WebSocketSharp.Net;
using WebSocketSharp.Server;

namespace MultiRoom2.Controllers;

public class DispatchController
{
    private const string Location = @"C:\Users\danil\RiderProjects\MyGstreamerApp\MultiRoom2\Web";
    
    private readonly DbManager dbManager = new(new DbContext());
    
    private readonly RoomService RoomService;
    private readonly MailService _mailService;
    private readonly ProfileService _profileService;

    private readonly List<Controller> _controllers = [];

    public DispatchController(MultistreamConferenceConfiguration config)
    {
        RoomService = new RoomService(dbManager);
        _mailService = new MailService();
        _profileService = new ProfileService(dbManager, config, _mailService);
        
        _controllers.Add(new ProfileController(_profileService));
        _controllers.Add(new RoomController(RoomService, _profileService));
    }

    public void DispatchRequest(HttpRequestEventArgs ea)
    {
        DoDispatch(ea.Request, ea.Response);
        // ea.Response.Close();
    }

    private void DoDispatch(HttpListenerRequest req, HttpListenerResponse response)
    {
        DisableCors(req, response);
        
        if (req.HttpMethod == "OPTIONS")
        {
            Console.WriteLine("Options");
            response.StatusCode = 200;
            return;
        }

        
        Type searchAttributeType;
        switch (req.HttpMethod)
        {
            case "GET":
                searchAttributeType = typeof(HttpGet);
                break;
            case "POST":
                searchAttributeType = typeof(HttpPost);
                break;
            default:
                response.StatusCode = (int) HttpStatusCode.MethodNotAllowed;
                return;
        }
        
        foreach (var controller in _controllers)
        {
            var controllerType = controller.GetType();
            var controllerAttr = controllerType.GetCustomAttribute(typeof(ApiController)) as ApiController;
            string generalPath = "";
            if (controllerAttr != null)
            {
                generalPath = controllerAttr.Path;
            }
            var methods = controllerType.GetMethods();
            foreach (var method in methods)
            {
                var searchAttribute = method.GetCustomAttribute(searchAttributeType) as HttpAttribute;
                if (searchAttribute == null)
                {
                    continue;
                }
                string? path = searchAttribute.Path;
                if (path.IsNullOrEmpty())
                {
                    path = "/";
                }

                string totalPath = generalPath + path;

                if (PathPatternMatch(totalPath, req.Url.AbsolutePath))
                {
                    try
                    {
                        method.Invoke(controller, [req, response]);
                        return;
                    }
                    catch (ApplicationException e)
                    {
                        response.StatusCode = 500;
                        response.WriteContent(Encoding.UTF8.GetBytes(e.Message));
                        throw;
                    } 
                }
            }
        }

        if (searchAttributeType == typeof(HttpGet))
        {
            GetStaticFile(req.Url.AbsolutePath, response);
        }
        else
        {
            response.StatusCode = 404;
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

    private bool PathPatternMatch(string template, string path)
    {
        var templateParts = template.Split("/").Where(it => it != "").ToList();
        var parts = path.Split("/").Where(it => it != "").ToList();

        if (templateParts.Count != parts.Count) return false;
        
        for (int i = 0; i < parts.Count; ++i)
        {
            if (templateParts[i].StartsWith('{') && templateParts[i].EndsWith('}'))
            {
                continue;
            } else if (templateParts[i] != parts[i])
            {
                return false;
            }
        }

        return true;
    }

    private void DisableCors(HttpListenerRequest req, HttpListenerResponse response)
    {
        response.AddHeader("Access-Control-Allow-Headers", "Origin, X-Requested-With, Content-Type, Accept");
        response.AddHeader("Access-Control-Allow-Methods", "*");
        response.AddHeader("Access-Control-Max-Age", "1728000");
        response.AddHeader("Access-Control-Allow-Credentials", "true");
        response.AppendHeader("Access-Control-Allow-Origin", req.Url.Scheme + "://" + req.UserHostName + ":3000");
    }
}