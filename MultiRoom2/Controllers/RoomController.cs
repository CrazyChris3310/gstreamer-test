using System.Text;
using MultiRoom2.Services;
using Newtonsoft.Json;
using WebSocketSharp;
using WebSocketSharp.Net;

namespace MultiRoom2.Controllers;

[ApiController(Path = "/rooms")]
public class RoomController(RoomService roomService, ProfileService profileService) : Controller
{
    // public bool RoomExists(HttpListenerRequest req, HttpListenerResponse resp)
    // {
    //     
    // }

    [HttpPost]
    public void CreateRoom(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var userInfo = WebUtils.GetSessionInfo(req)!;
        
        using var memoryStream = new MemoryStream();
        req.InputStream.CopyTo(memoryStream);
        var body = memoryStream.ToArray();
        var bodyContent = Encoding.UTF8.GetString(body);
        var request = JsonConvert.DeserializeObject<ConferenceInfoType>(bodyContent)!;

        var createdRoomResponse = roomService.CreateRoom(userInfo.Id, request);
        WriteObject(createdRoomResponse, resp);
    }

    [HttpGet]
    public void GetConferences(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var listType = roomService.GetConferences();
        WriteObject(listType, resp);
    }

}