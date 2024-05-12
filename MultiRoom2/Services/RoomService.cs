using System.Text;
using Gst;
using Gst.Rtsp;
using Microsoft.EntityFrameworkCore;
using MultiRoom;
using MultiRoom2.Entities;
using Newtonsoft.Json;
using WebSocketSharp.Net;

namespace MultiRoom2.Controllers;

public class RoomService(AppContext db)
{
    private Dictionary<string, Room> rooms = new();

    public bool GetRoom(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var path = req.Url.AbsolutePath;
        var roomId = path[1..].Split("/").Last();
        
        var roomExists = rooms.TryGetValue(roomId, out var room);
        if (!roomExists)
        {
            resp.StatusCode = 404;
            return false;
        }
        else
        {
            resp.StatusCode = 200;
            return true;
        }
    }

    public Client CreateClient()
    {
        var client = new Client();
        client.JoinRoom += (roomId) =>
        {
            rooms[roomId].AddClient(client);
        };
        return client;
    }

    public void CreateRoom(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var creds = req.Cookies["authentication"]?.Value!;
        var userId = int.Parse(creds.Split("/")[0]);
        var room = new Room(Guid.NewGuid().ToString(), userId);
        rooms[room.Id] = room;
        room.LeaveRoom += () =>
        {
            // if (room.clients.Count == 0)
            // {
            //     rooms.Remove(room.Id);
            // }
        };
        var body = JsonConvert.SerializeObject(new CreateRoomResponse { RoomId = room.Id });

        var byteId = Encoding.UTF8.GetBytes(body);
        
        resp.ContentType = "text/plain";
        resp.ContentLength64 = byteId.Length;
        resp.ContentEncoding = Encoding.UTF8;
        resp.OutputStream.Write(byteId, 0, byteId.Length);
        resp.OutputStream.Flush();
        resp.StatusCode = 200;
    }
}