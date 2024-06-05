// using MultiRoom2.Entities;
// using MultistreamConferenceTestService.Util;
//
// namespace MultiRoom2.Services;
//
// public class ConferenceService
// {
//
//     private DbContext db;
//
//     public ListType GetConferences()
//     {
//         var dbConferences = db.Conferences.ToList()
//             .Select(it => it.TranslateConference()).ToArray();
//
//         return new ListType()
//         {
//             TotalCount = dbConferences.Length,
//             Items = dbConferences,
//             Count = dbConferences.Length,
//             From = 0
//         };
//     }
//
//     ListType GetConferenceParticipants(int roomId)
//     {
//         
//     }
//     
// }