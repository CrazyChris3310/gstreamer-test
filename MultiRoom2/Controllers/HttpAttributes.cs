using System.Text;
using Newtonsoft.Json;
using WebSocketSharp;
using WebSocketSharp.Net;

namespace MultiRoom2.Controllers;

public class HttpAttribute : Attribute
{
    public string? Path { get; set; }

    public HttpAttribute() {}

    public HttpAttribute(string path)
    {
        Path = path;
    }
}

public class ApiController : HttpAttribute;

public abstract class Controller
{

    protected static void WriteObject<T>(T obj, HttpListenerResponse resp)
    {
        var data = JsonConvert.SerializeObject(obj)!;
        resp.WriteContent(Encoding.UTF8.GetBytes(data));
        resp.ContentType = "application/json";
        resp.ContentEncoding = Encoding.UTF8;
        resp.StatusCode = 200;
    }
    
}

public class HttpGet : HttpAttribute
{
}

public class HttpPost : HttpAttribute
{
    
}