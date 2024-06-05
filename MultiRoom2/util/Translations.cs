using MultiRoom2.Entities;

namespace MultistreamConferenceTestService.Util
{
    static class Translations
    {
        public static ConferenceInfoType TranslateConference(this Conference info)
        {
            return new ConferenceInfoType() {
                Id = info.Id,
                Title = info.Title,
                Description = info.Description,
                CreationStamp = info.CreationStamp.TranslateStamp(),
                Participants = info.MaxUsersOnline,
                StartStamp = info.StartTime.TranslateStamp(),
                EndStamp = info.EndTime.TranslateStamp(),
                StateStamp = new[] { info.CreationStamp, info.EndTime, info.StartTime }.Max().TranslateStamp(),
                State = info.PrepareConferenceStateKind()
            };
        }
        
        public static UserInfoType TranslateUser(this UserInfo info)
        {
            return new UserInfoType()
            {
                Id = info.Id,
                Name = info.Login
            };
        }

        public static ConferenceStateKindType PrepareConferenceStateKind(this Conference info)
        {
            var now = DateTime.UtcNow;

            if (info.EndTime < now)
                return ConferenceStateKindType.Finished;
            else if (info.StartTime < now)
                return ConferenceStateKindType.Running;
            else
                return ConferenceStateKindType.Scheduled;
        }

        public static StampType? TranslateStamp(this DateTime? t)
        {
            if (t == null) return null;
            return new StampType() {
                Ticks = ((DateTimeOffset)t).ToUnixTimeMilliseconds()
            };
        }
        public static IEnumerable<TOut> FillSubitems<TIn, TOut, TSubitem>(this IEnumerable<TIn> seq, IEnumerable<TSubitem> subSeq,
                                            Func<TIn, TOut> map, Func<TIn, int> countGetter, Action<TOut, TSubitem[]> setter)
        {
            var subitems = subSeq.GetEnumerator();
            try
            {
                foreach (var item in seq)
                {
                    var result = map(item);

                    var subitemsCount = countGetter(item);
                    var subitemsToSet = new TSubitem[subitemsCount];
                    for (int i = 0; i < subitemsCount && subitems.MoveNext(); i++)
                        subitemsToSet[i] = subitems.Current;

                    setter(result, subitemsToSet);

                    yield return result;
                }
            }
            finally
            {
                subitems.Dispose();
            }
        }
    }
}
