using WebSocketSharp.Net;

namespace MultiRoom2.Controllers;

public class WebUtils
{
    
    public static Dictionary<string, string?> GetQueryParams(HttpListenerRequest req)
    {
        var query = req.Url.Query;
        var queryTokens = new Dictionary<string, string?>();
        if (query != "")
        {
            var tokens = query[1..].Split("&");
            foreach (var token in tokens)
            {
                string?[] keyVal = token.Split("=");
                queryTokens[keyVal[0]] = keyVal[1];
            }
        }

        return queryTokens;
    }
    
    public static Dictionary<string, string> GetPathVariables(string template, HttpListenerRequest req)
    {
        var query = req.Url.Query;
        var queryTokens = new Dictionary<string, string>();

        var queryParts = query.Split("/");
        var parts = template.Split("/");
        
        for (int i = 0; i < parts.Length; ++i)
        {
            if (parts[i].StartsWith('{') && parts[i].EndsWith('}'))
            {
                queryTokens[parts[i].Substring(1, parts[i].Length-2)] = queryParts[i];
            }
        }

        return queryTokens;
    }

    public static UserSessionInfo? GetSessionInfo(HttpListenerRequest req)
    {
        var creds = req.Cookies["authentication"]?.Value;
        if (creds == null)
        {
            return null;
        }
        var strings = creds.Split("/");
        var userId = int.Parse(strings[0]);
        return new UserSessionInfo()
        {
            Id = userId,
            login = strings[1]
        };
    }
}

public class UserSessionInfo
{
    public long Id { get; set; }
    public string login { get; set; }
}