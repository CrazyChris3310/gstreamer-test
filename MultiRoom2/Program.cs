using System.Net;
using System.Net.Mail;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Serialization;
using GLib;
using MultiRoom2;
using MultiRoom2.Controllers;
using WebApplication1;
using WebSocketSharp.Server;
using Application = Gst.Application;

namespace MultiRoom;

class Program
{
    int _idCounter = 0;

    readonly object _clientsLock = new object();

    readonly HttpServer _httpServer;

    private readonly DispatchController _dispatcher;

    // public readonly Pipeline _pipeline;
    // readonly Element _mixer;

    private Dictionary<int, Client> _clients = new();
    private Dictionary<string, Dictionary<int, Client>> roomClients = new();

    private Program(MultistreamConferenceConfiguration config)
    {
        _dispatcher = new DispatchController(config);
        roomClients["roomId"] = new();
        _httpServer = new HttpServer(IPAddress.Any, 443, true);
        _httpServer.OnGet += (sender, ea) => _dispatcher.DispatchRequest(ea);
        _httpServer.OnPost += (sender, ea) => _dispatcher.DispatchRequest(ea);
        _httpServer.OnOptions += (sender, ea) => _dispatcher.DispatchRequest(ea);
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
        if (args.Length > 1)
        {
            Console.WriteLine("Usage: {0} <cfg.xml>", typeof(Program).Assembly.Location);
        }
        else
        {
            var cfgPath = "C:\\Users\\danil\\RiderProjects\\MyGstreamerApp\\MultiRoom2\\Cfg\\cfg.xml";
            var xs = new XmlSerializer(typeof(MultistreamConferenceConfigurationType));
            var cfgXml =
                (MultistreamConferenceConfigurationType)
                xs.Deserialize(new StringReader(File.ReadAllText(cfgPath)))!;

            var cfg = new MultistreamConferenceConfiguration()
            {
                DbFileName = cfgXml.DbFileName,
                ServiceHostUrl = cfgXml.ServiceHostUrl,
                SessionTimeout = cfgXml.SessionTimeout.ToTimeSpan(),
                TokenTimeout = cfgXml.TokenTimeout.ToTimeSpan(),
                DeliveryTimeout = cfgXml.DeliveryTimeout.ToTimeSpan(),

                SmtpServerHost = cfgXml.Smtp.SmtpServerHost,
                SmtpServerPort = (ushort)cfgXml.Smtp.SmtpServerPort,
                SmtpLogin = cfgXml.Smtp.SmtpLogin,
                SmtpPassword = cfgXml.Smtp.SmtpPassword,
                SmtpUseSsl = cfgXml.Smtp.SmtpUseSsl && cfgXml.Smtp.SmtpUseSslSpecified,
                SmtpUseDefaultCredentials = cfgXml.Smtp.SmtpUseDefaultCredentials &&
                                            cfgXml.Smtp.SmtpUseDefaultCredentialsSpecified,
                // SmtpDeliveryMethod = SmtpDeliveryMethod.Network,

                SmtpDeliveryMethod = string.IsNullOrWhiteSpace(cfgXml.Smtp.SmtpPickupDirectoryLocation)
                    ? SmtpDeliveryMethod.Network
                    : SmtpDeliveryMethod.SpecifiedPickupDirectory,
                SmtpPickupDirectoryLocation = cfgXml.Smtp.SmtpPickupDirectoryLocation,

                LinkTemplates = cfgXml.LinkTemplates,

                LogsDirPath = cfgXml.LogsDirPath
            };

            var app = new Program(cfg);

            Environment.SetEnvironmentVariable("GST_DEBUG", @"*:3");
            // Environment.SetEnvironmentVariable("PATH", @"C:\gstreamer\1.0\msvc_x86_64\bin;" + Environment.GetEnvironmentVariable("PATH"));
            
            Application.Init(ref args);
            ExceptionManager.UnhandledException += ea => { Console.WriteLine(ea.ExceptionObject.ToString()); };

            var loop = new MainLoop();
            loop.Run();
        }
    }
}