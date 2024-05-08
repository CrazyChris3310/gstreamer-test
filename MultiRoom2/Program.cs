using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using GLib;
using Gst;
using Gst.Audio;
using WebSocketSharp;
using WebSocketSharp.Server;
using HttpStatusCode = WebSocketSharp.Net.HttpStatusCode;
using Task = System.Threading.Tasks.Task;
using Thread = System.Threading.Thread;

namespace MultiRoom;

class Program
    {
        int _idCounter = 0;

        readonly object _clientsLock = new object();

        readonly HttpServer _httpServer;

        // public readonly Pipeline _pipeline;
        // readonly Element _mixer;
        
        private Dictionary<int, Client> _clients = new();

        public Program()
        {
            _httpServer = new HttpServer(IPAddress.Any, 443, true);
            _httpServer.OnGet += (sender, ea) => this.OnWebGet(ea);
            _httpServer.OnPost += (sender, ea) => this.OnWebPost(ea);
            _httpServer.AddWebSocketService("/sck", () =>
            {
                var client = new Client( Interlocked.Increment(ref _idCounter), _clients.Values.ToList());
                client.onoffercreated += (i, msg) => _clients[i].Send(msg);
                client.readressSdp += (i, msg) => _clients[i].HandleSdp(msg, client.id);
                client.OnStreamingStart += id =>
                {
                    foreach (var (clientId, otherClient) in _clients)
                    {
                        if (clientId == client.id)
                        {
                            continue;
                        }
                        if (otherClient.isStreaming)
                        {
                            Console.WriteLine("Adding peer " + client.id + " to " + otherClient.id);
                            otherClient.AddPeer(client.id);
                        }
                    }
                };
                client.OnSocketClosed += () =>
                {
                    _clients.Remove(client.id);
                    foreach (var (clientId, otherClient) in _clients)
                    {
                        if (otherClient.isStreaming)
                        {
                            otherClient.DisconnectPeer(client.id);
                        }
                    }
                };
               
                _clients[client.id] = client;
                return client;
            });
            _httpServer.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls13;
            _httpServer.SslConfiguration.ServerCertificate = new X509Certificate2("C:\\Windows\\System32\\cert.pfx", "danil1209");
            _httpServer.SslConfiguration.ClientCertificateRequired = false;
            _httpServer.SslConfiguration.CheckCertificateRevocation = false;
            _httpServer.Start();

            // _pipeline = new Pipeline();
            //
            // var mixer = ElementFactory.Make("compositor", "mixer");
            // var filter = ElementFactory.Make("capsfilter");
            // Util.SetObjectArg(filter, "caps", "video/x-raw, width=1280, height=720");
            // var convert = ElementFactory.Make("videoconvert");
            // var queue = ElementFactory.Make("queue");
            // var tee = ElementFactory.Make("tee", "splitter");
            // var videoSink = ElementFactory.Make("autovideosink");
            // _pipeline.Add(mixer, filter, convert, queue, tee, videoSink);
            // Element.Link(mixer, filter, convert, queue, tee);
            //
            // var padTemplate = tee.PadTemplateList.First(it => it.Name.Contains("src"));
            // var teePad = tee.RequestPad(padTemplate);
            // teePad.Link(videoSink.GetStaticPad("sink"));
            //
            // var audioMixer = ElementFactory.Make("audiomixer", "audiomixer");
            // convert = ElementFactory.Make("audioconvert");
            // queue = ElementFactory.Make("queue");
            // var tee2 = ElementFactory.Make("tee");
            // var audiosink = ElementFactory.Make("autoaudiosink");
            // _pipeline.Add(audioMixer, convert, queue, audiosink);
            // Element.Link(audioMixer, convert, queue, audiosink);
            
            // padTemplate = tee2.PadTemplateList.First(it => it.Name.Contains("src"));
            // var teePad2 = tee2.RequestPad(padTemplate);
            // teePad2.Link(audiosink.GetStaticPad("sink"));
        }

        private void OnWebGet(HttpRequestEventArgs ea)
        {
            if (ea.Request.Url.Segments.Any(s => s == ".."))
            {
                ea.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                ea.Response.Close();
            }
            else
            {
                // var location = typeof(Program).Assembly.Location;
                // if (string.IsNullOrWhiteSpace(location))
                //     location = Environment.GetCommandLineArgs()[0];
                // else
                //     location = Path.GetDirectoryName(location);
                // if (string.IsNullOrWhiteSpace(location))
                //     location = Environment.CurrentDirectory;

                var location = "C:\\Users\\danil\\RiderProjects\\MyGstreamerApp\\MultiRoom2";

                var path = Path.Combine(new[] { location, "Web" }.Concat(ea.Request.Url.Segments.SkipWhile(s => s == "/")).ToArray());
                if (File.Exists(path))
                {
                    ea.Response.ContentLength64 = new System.IO.FileInfo(path).Length;
                    ea.Response.ContentType = "text/html";
                    using (var localFile = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        localFile.CopyTo(ea.Response.OutputStream);
                    }
                    ea.Response.Close();
                }
                else
                {
                    ea.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    ea.Response.Close();
                }
            }
        }

        private void OnWebPost(HttpRequestEventArgs ea)
        {
            ea.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            ea.Response.Close();
        }

        private string MakeRedirect(System.Uri uri)
        {
            var builder = new UriBuilder(uri);
            builder.Port = 443;
            builder.Scheme = "https";
            return builder.Uri.ToString();
        }
        
        // public void RunPipeline()
        // {
        //     // Wait until error, EOS or State Change
        //     var bus = _pipeline.Bus;
        //     bool terminate = false;
        //     do
        //     {
        //         var msg = bus.TimedPopFiltered(Gst.Constants.SECOND, MessageType.Error | MessageType.Eos | MessageType.StateChanged);
        //         // Parse message
        //         if (msg != null)
        //         {
        //             switch (msg.Type)
        //             {
        //                 case MessageType.Error:
        //                     string debug;
        //                     GLib.GException exc;
        //                     msg.ParseError(out exc, out debug);
        //                     Console.WriteLine("Error received from element {0}: {1}", msg.Src.Name, exc.Message);
        //                     Console.WriteLine("Debugging information: {0}", debug != null ? debug : "none");
        //                     terminate = true;
        //                     break;
        //                 case MessageType.Eos:
        //                     Console.WriteLine("End-Of-Stream reached.\n");
        //                     terminate = true;
        //                     break;
        //                 case MessageType.StateChanged:
        //                     // We are only interested in state-changed messages from the pipeline
        //                     if (msg.Src == _pipeline)
        //                     {
        //                         State oldState, newState, pendingState;
        //                         msg.ParseStateChanged(out oldState, out newState, out pendingState);
        //                         Console.WriteLine("Pipeline state changed from {0} to {1}:",
        //                             Element.StateGetName(oldState), Element.StateGetName(newState));
        //                     }
        //                     break;
        //                 default:
        //                     // We should not reach here because we only asked for ERRORs, EOS and STATE_CHANGED
        //                     Console.WriteLine("Unexpected message received.");
        //                     break;
        //             }
        //         }
        //     } while (!terminate);
        // }


        static void Main(string[] args)
        {
            Environment.SetEnvironmentVariable("GST_DEBUG", @"*:3");
            // Environment.SetEnvironmentVariable("PATH", @"C:\gstreamer\1.0\msvc_x86_64\bin;" + Environment.GetEnvironmentVariable("PATH"));

            Gst.Application.Init(ref args);
            GLib.ExceptionManager.UnhandledException += ea => {
                Console.WriteLine(ea.ExceptionObject.ToString());
            };

            var app = new Program();

            /*
             * Host webpage having login form, text chat and userslist.
             * On login make a permanent websocket connection and connect client to the voiceroom.
             */

            // app.RunPipeline();

            while (true)
            {
                
            }
        }
    }