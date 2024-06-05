using System.Text;
using MultiRoom2.Services;
using Newtonsoft.Json;
using WebApplication1;
using WebSocketSharp;
using WebSocketSharp.Net;

namespace MultiRoom2.Controllers;

[ApiController(Path = "/profile")]
public class ProfileController(
    ProfileService profileService)
    : Controller
{

    [HttpPost]
    public void PostAction(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var queryParams = WebUtils.GetQueryParams(req);
        string? action = queryParams.GetValueOrDefault("action");
        
        using var memoryStream = new MemoryStream();
        req.InputStream.CopyTo(memoryStream);
        var body = memoryStream.ToArray();
        var bodyContent = Encoding.UTF8.GetString(body);

        var userInfo = WebUtils.GetSessionInfo(req);
        
        if (action == "register")
        {
            var regData = JsonConvert.DeserializeObject<RegisterSpecType>(bodyContent)!;
            profileService.Register(regData);
        } else if (action == "restore")
        {
            var resetPasswordSpec = JsonConvert.DeserializeObject<ResetPasswordSpecType>(bodyContent)!;
            profileService.RequestAccess(resetPasswordSpec);
        } else if (action == "activate")
        {
            var activationSpec = JsonConvert.DeserializeObject<RequestActivationSpecType>(bodyContent)!;
            profileService.RequestActivation(activationSpec, userInfo.Id);
        } else if (action == "delete")
        {
            profileService.DeleteProfile(userInfo.Id);
        } else if (action == "login")
        {
            var logData = JsonConvert.DeserializeObject<LoginSpecType>(bodyContent)!;
            long userId = profileService.Login(logData);
            var cookie = new Cookie("authentication", $"{userId}/{logData.Login}")
            {
                // cookie.Path = "https://localhost";
                Domain = "localhost",
                Port = "\"443\"",
                Secure = true
            };
            // resp.AppendCookie(cookie);
            resp.AddHeader("Set-Cookie", $"authentication={userId}/{logData.Login}; SameSite=None; Secure");
        } else if (action == "set-password")
        {
            var changePassword = JsonConvert.DeserializeObject<ChangePasswordSpecType>(bodyContent)!;
            profileService.SetPassword(userInfo.Id, changePassword);
        } else if (action == "set-email")
        {
            var changeEmail = JsonConvert.DeserializeObject<ChangeEmailSpecType>(bodyContent)!;
            profileService.SetEmail(userInfo.Id, changeEmail);
        }

        resp.StatusCode = 200;
    }
    
    [HttpGet]
    public void GetAction(HttpListenerRequest req, HttpListenerResponse resp) 
    {
        var userInfo = WebUtils.GetSessionInfo(req);
        
        var queryParams = WebUtils.GetQueryParams(req);
        var action = queryParams.GetValueOrDefault("action");

        if (action == "activate")
        {
            var key = queryParams["key"];
            profileService.Activate(key);
        } else if (action == "restore")
        {
            var key = queryParams["key"];
            profileService.RestoreAccess(key);
        } else if (string.IsNullOrEmpty(action))
        {
            if (userInfo == null)
            {
                resp.StatusCode = 401;
                return;
            }

            var profileFootprintInfoType = profileService.GetProfileFootprint(userInfo.Id);
            var data = JsonConvert.SerializeObject(profileFootprintInfoType)!;
            resp.WriteContent(Encoding.UTF8.GetBytes(data));
            resp.ContentType = "application/json";
            resp.ContentEncoding = Encoding.UTF8;
        }

        resp.StatusCode = 200;
    }

    [HttpGet(Path = "/all")]
    public void GetAllUsers(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var allUsers = profileService.GetAllUsers();
        WriteObject(allUsers, resp);
    }
}