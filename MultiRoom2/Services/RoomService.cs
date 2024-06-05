using System.Data.SqlTypes;
using System.Text;
using Gst;
using Gst.Rtsp;
using Microsoft.EntityFrameworkCore;
using MultiRoom;
using MultiRoom2.Entities;
using MultistreamConferenceTestService.Util;
using Newtonsoft.Json;
using WebSocketSharp.Net;
using DateTime = System.DateTime;

namespace MultiRoom2.Controllers;

public class RoomService(DbContext db)
{
    private Dictionary<string, Room> rooms = new();
    
    public bool RoomExists(string roomId)
    {
        var roomExists = rooms.TryGetValue(roomId, out var room);
        return roomExists;
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

    public ConferenceCreationResponse CreateRoom(long userId, ConferenceInfoType request)
    {
        var room = new Room(Guid.NewGuid().ToString(), (int)userId);
        rooms[room.Id] = room;
        room.LeaveRoom += () =>
        {
            // if (room.clients.Count == 0)
            // {
            //     rooms.Remove(room.Id);
            // }
        };

        var conference = new Conference()
        {
            Id = room.Id,
            CreationStamp = DateTime.UtcNow,
            Description = request.Description,
            EndTime = SqlDateTime.MinValue.Value,
            isPublic = true,
            MaxUsersOnline = 100,
            StartTime = request.StartStamp != null ? new DateTime(request.StartStamp.Ticks) : DateTime.UtcNow,
            Title = request.Title
        };
        db.Conferences.Add(conference);
        db.SaveChanges();
        
        return new ConferenceCreationResponse()
        {
            RoomId = room.Id
        };
    }

    public ListType GetConferences()
    {
        var dbConferences = db.Conferences
            .Where(it => rooms.Keys.Contains(it.Id))
            .Select(it => it.TranslateConference(rooms[it.Id].streamingCLients.Count))
            .ToArray();

        return new ListType()
        {
            TotalCount = dbConferences.Length,
            Items = dbConferences,
            Count = dbConferences.Length,
            From = 0
        };
    }

}