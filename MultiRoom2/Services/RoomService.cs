using System.Data.SqlTypes;
using MultiRoom;
using MultiRoom2.Database;
using MultiRoom2.Entities;
using MultistreamConferenceTestService.Util;
using DateTime = System.DateTime;

namespace MultiRoom2.Services;

public class RoomService(DbManager dbContext)
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
            dbContext.Update(db =>
            {
                var conference = db.Conferences.Find(roomId)!;
                conference.MaxUsersOnline += 1;
                db.Update(conference);
            });
            dbContext.Commit();
        };
        return client;
    }

    public ConferenceCreationResponse CreateRoom(long userId, ConferenceInfoType request)
    {
        var room = new Room(Guid.NewGuid().ToString(), (int)userId);
        rooms[room.Id] = room;
        room.LeaveRoom += () =>
        {
            dbContext.Update(db =>
            {
                var conference = db.Conferences.Find(room.Id);
                if (conference != null)
                {
                    conference.MaxUsersOnline -= 1;
                    db.Update(conference);
                }
            });
            dbContext.Commit();
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
            MaxUsersOnline = 0,
            StartTime = request.StartStamp != null ? new DateTime(request.StartStamp.Ticks) : DateTime.UtcNow,
            Title = request.Title
        };
        
        dbContext.Update(db => db.Conferences.Add(conference));
        dbContext.Commit();
        
        return new ConferenceCreationResponse()
        {
            RoomId = room.Id
        };
    }

    public ListType GetConferences()
    {
        var dbConferences = dbContext.Query(db =>
            db.Conferences
                .Where(it => rooms.Keys.Contains(it.Id))
                .Select(it => it.TranslateConference())
                .ToArray()
        );

        return new ListType()
        {
            TotalCount = dbConferences.Length,
            Items = dbConferences,
            Count = dbConferences.Length,
            From = 0
        };
    }

    public ConferenceInfoType? GetConference(string id)
    {
        try
        {
            return dbContext.Query(db => db.Conferences?.Find(id)?.TranslateConference());
        }
        catch (Exception)
        {
            return null;
        }
    }

}