using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using GLib;
using MultiRoom2.Controllers;
using WebSocketSharp.Server;
using Application = Gst.Application;

namespace MultiRoom;

class Program
{
    int _idCounter = 0;

    readonly object _clientsLock = new object();

    readonly HttpServer _httpServer;

    private readonly DispatchController _dispatcher = new();

    // public readonly Pipeline _pipeline;
    // readonly Element _mixer;

    private Dictionary<int, Client> _clients = new();
    private Dictionary<string, Dictionary<int, Client>> roomClients = new();

    private Program()
    {
        roomClients["roomId"] = new();
        _httpServer = new HttpServer(IPAddress.Any, 443, true);
        _httpServer.OnGet += (sender, ea) => _dispatcher.DispatchRequest(ea);
        _httpServer.OnPost += (sender, ea) => _dispatcher.DispatchRequest(ea);
        _httpServer.AddWebSocketService("/sck", () => _dispatcher.DispatchWebSocket());
        _httpServer.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls13;
        _httpServer.SslConfiguration.ServerCertificate =
            new X509Certificate2("C:\\Windows\\System32\\cert.pfx", "danil1209");
        _httpServer.SslConfiguration.ClientCertificateRequired = false;
        _httpServer.SslConfiguration.CheckCertificateRevocation = false;
        _httpServer.Start();
    }

    private static void Main(string[] args)
    {
        Environment.SetEnvironmentVariable("GST_DEBUG", @"*:3");
        // Environment.SetEnvironmentVariable("PATH", @"C:\gstreamer\1.0\msvc_x86_64\bin;" + Environment.GetEnvironmentVariable("PATH"));

        Application.Init(ref args);
        ExceptionManager.UnhandledException += ea => { Console.WriteLine(ea.ExceptionObject.ToString()); };

        var app = new Program();

        /*
         * Host webpage having login form, text chat and userslist.
         * On login make a permanent websocket connection and connect client to the voiceroom.
         */

        // app.RunPipeline();

        // while (true)
        // {
        //     
        // }

        var loop = new MainLoop();
        loop.Run();
    }
}