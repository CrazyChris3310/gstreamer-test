using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Diagnostics.Debug;
using GLib;
using Gst;
using Gst.WebRTC;
using Newtonsoft.Json;
using WebSocketSharp;
using Gst.Sdp;
using WebSocketSharp.Server;
using System.Net;
using System.Threading;
using System.Text.RegularExpressions;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using WebSocketSharp.Net;
using HttpStatusCode = System.Net.HttpStatusCode;
using Value = GLib.Value;

namespace GstreamerChatroomTest
{
    class SdpMsg
    {
        public SdpContent sdp;
        public SdpIceContent ice;
    }

    class SdpContent
    {
        public string type;
        public string sdp;
    }

    class SdpIceContent
    {
        public string candidate;
        public uint sdpMLineIndex;
    }

    class WebMsg
    {
        public SdpMsg sdp;
        public ChatMsg chat;
        public ControlMsg control;
    }

    class ControlMsg
    {
        public bool authenticated;
        public UserlistMsg userlist;
    }

    class UserlistMsg
    {
        public string[] names;
        public int awaiting;
    }

    class ChatMsg
    {
        public string text;
    }

    class WebRTCLayer
    {
        
    }

    class Client
    {
        public class WebSocketHandler : WebSocketBehavior
        {
            private readonly Client _client;
            
            private Pipeline _pipeline;

            private Element _webrtcbin;

            private bool isConnected = false;

            // private Element sink;
            //
            // private Element convert;

            
            public WebSocketHandler(Client client)
            {
                _client = client;
                _pipeline = client._app._pipeline;
                CreateWebrtcBin();
            }

            private void OnIncomingDecodeBinStream(object o, GLib.SignalArgs args)
            {
                var newPad = (Pad)args.Args[0];
                if (!newPad.HasCurrentCaps)
                {
                    Console.WriteLine($"{newPad.Name} has no caps, ignoring");
                    return;
                }

                var caps = newPad.CurrentCaps;
                Assert(!caps.IsEmpty);
                Structure s = caps[0];
                var name = s.Name;

                if (name.StartsWith("video"))
                {
                    var q = ElementFactory.Make("queue");
                    var conv = ElementFactory.Make("videoconvert");

                    _pipeline.Add(q, conv);
                    q.Link(conv);
                    newPad.Link(q.GetStaticPad("sink"));

                    int amount = ++_client._app.clientsCount;
                    int width = 640;
                    int height = 360;
                    int x = ((amount - 1) % 2) * width;
                    int y = ((amount - 1) / 2) * height;
                    var mixer = _pipeline.GetByName("mixer");
                    var sinkTemplate = mixer.PadTemplateList.First(it => it.Name.Contains("sink"));
                    var sinkPad = mixer.RequestPad(sinkTemplate);
                    
                    sinkPad.SetProperty("xpos", new Value(x));
                    sinkPad.SetProperty("ypos", new Value(y));
                    sinkPad.SetProperty("width", new Value(width));
                    sinkPad.SetProperty("height", new Value(height));
                    
                    var convSrcPad = conv.GetStaticPad("src");
                    convSrcPad.Link(sinkPad);

                    q.SetState(Gst.State.Playing);
                    conv.SetState(Gst.State.Playing);
                    
                    // EchoIncomingStream();
                }

            }
            
            int ssrc = 1234134;

            private void EchoIncomingStream()
            {
                var tee = _pipeline.GetByName("splitter");
                var padTemplate = tee.PadTemplateList.First(it => it.Name.Contains("src"));
                var teePad = tee.RequestPad(padTemplate);

                var vp8enc = ElementFactory.Make("vp8enc");
                var rtppayload = ElementFactory.Make("rtpvp8pay");
                var queue = ElementFactory.Make("queue");
                var filter = ElementFactory.Make("capsfilter");
                Util.SetObjectArg(filter, "caps", "application/x-rtp,media=video,encoding-name=VP8,payload=97");

                _pipeline.Add(vp8enc, rtppayload, queue, filter);
                Element.Link(vp8enc, rtppayload, queue, filter);

                var sinkTemplate = _webrtcbin.PadTemplateList.First(it => it.Name.Contains("sink"));
                var sinkPad = _webrtcbin.RequestPad(sinkTemplate);
                
                var padLinkReturn = teePad.Link(vp8enc.GetStaticPad("sink"));
                Console.WriteLine("Result of linking tee with encoder sink: " + padLinkReturn);
                
                padLinkReturn = filter.GetStaticPad("src").Link(sinkPad);
                Console.WriteLine("Result of linking filter with webrtcbin sink: " + padLinkReturn);

                _pipeline.SyncChildrenStates();
                
                vp8enc.SetState(Gst.State.Playing);
                rtppayload.SetState(Gst.State.Playing);
                queue.SetState(Gst.State.Playing);
                filter.SetState(Gst.State.Playing);
            }

            protected void CreateWebrtcBin()
            {
                _webrtcbin = ElementFactory.Make("webrtcbin");
                
                _webrtcbin.SetProperty("stun-server", new Value("stun://stun.l.google.com:19302"));
                _webrtcbin.SetProperty("bundle-policy", WebRTCBundlePolicy.MaxBundle.AsGLibValue());
                // _webrtcbin.Connect("on-negotiation-needed", this.OnNegotiationNeeded);
                _webrtcbin.Connect("on-ice-candidate", this.OnIceCandidate);
                _webrtcbin.Connect("pad-added", (object o, SignalArgs args) =>
                {
                    Console.WriteLine("Pad has been added");
                    isConnected = true;
                    var newPad = (Pad)args.Args[0];
                    if (!newPad.HasCurrentCaps)
                    {
                        Console.WriteLine($"{newPad.Name} has no caps, ignoring");
                        return;
                    }

                    var decodeBin = ElementFactory.Make("decodebin");
                    decodeBin.Connect("pad-added", OnIncomingDecodeBinStream);

                    _pipeline.Add(decodeBin);
                    var sinkPad = decodeBin.GetStaticPad("sink");
                    var padLinkReturn = newPad.Link(sinkPad);
                    decodeBin.SyncStateWithParent();
                    Console.WriteLine("Pad link result: " + padLinkReturn);
                    // sink.SetState(Gst.State.Playing);
                }); 
                
                _pipeline.Add(_webrtcbin);
            }
            
            static GException GetGError(Structure structure)
            {
                var value = GetStructRawValue(structure, "error");
                var ptr = g_value_get_boxed(value);
                var ex = new GException(ptr);
                return ex;
            }

            [DllImport("gstreamer-1.0-0.dll", CallingConvention = CallingConvention.Cdecl)]
            static extern IntPtr gst_structure_get_value(IntPtr raw, IntPtr fieldname);

            [DllImport("gobject-2.0-0.dll", CallingConvention = CallingConvention.Cdecl)]
            static extern IntPtr g_value_get_pointer(IntPtr val);

            [DllImport("gobject-2.0-0.dll", CallingConvention = CallingConvention.Cdecl)] 
            static extern IntPtr g_value_get_boxed(IntPtr val);

            public static IntPtr GetStructRawValue(Structure structure, string fieldname)
            {
                IntPtr native_fieldname = GLib.Marshaller.StringToPtrGStrdup(fieldname);
                IntPtr raw_ret = gst_structure_get_value(structure.Handle, native_fieldname);
                // GLib.Value ret = (GLib.Value)Marshal.PtrToStructure(raw_ret, typeof(GLib.Value));
                GLib.Marshaller.Free(native_fieldname);
                return raw_ret;
            }
            
            void OnIceCandidate(object o, GLib.SignalArgs args)
            {
                var index = (uint)args.Args[0];
                var cand = (string)args.Args[1];
                var iceMsg = new SdpMsg { ice = new SdpIceContent { sdpMLineIndex = index, candidate = cand } };

                Send(new WebMsg() { sdp = iceMsg });
            }

            void OnNegotiationNeeded(object o, GLib.SignalArgs args)
            {
                if (isConnected)
                {
                    Console.WriteLine("Renegotiation needed event fired");
                    var webRtc = o as Element;
                    Assert(webRtc != null, "not a webrtc object");

                    //_client.CreateSendingChain(_webrtcbin);

                    Promise
                        promise = new Promise(this.OnOfferCreated); //, webrtc.Handle, null); // webRtc.Handle, null);
                    Structure structure = new Structure("struct");
                    webRtc.Emit("create-offer", structure, promise);
                }
            }

            void OnOfferCreated(Promise promise)
            {
                promise.Wait();
                var reply = promise.RetrieveReply();
                if (reply.Name.Contains("error"))
                {
                    Console.WriteLine(GetGError(reply));
                }
                else
                {
                    var gval = reply.GetValue("offer");
                    WebRTCSessionDescription offer = (WebRTCSessionDescription)gval.Val;
                    promise = new Promise();
                    _webrtcbin.Emit("set-local-description", offer, promise);
                    promise.Interrupt();

                    var sdpMsg = new SdpMsg { sdp = new SdpContent { type = "offer", sdp = offer.Sdp.AsText() } };
                    Send(new WebMsg() { sdp = sdpMsg });
                }
            }

            public void HandleIncomingSdp(SdpMsg msg)
            {
                if (msg.sdp != null)
                {
                    var sdp = msg.sdp;
                    string sdpMessage = sdp.sdp;
                    Console.WriteLine($"received sdp:\n{sdpMessage}");
                    SDPMessage.New(out SDPMessage sdpMsg);
                    SDPMessage.ParseBuffer(ASCIIEncoding.Default.GetBytes(sdpMessage), (uint)sdpMessage.Length, sdpMsg);
                    var remoteDescription = WebRTCSessionDescription.New(sdp.type == "offer" ? WebRTCSDPType.Offer : WebRTCSDPType.Answer, sdpMsg);
                    var promise = new Promise(p => {
                        p.Wait();
                        var tp = p.RetrieveReply();
                        if (tp != null && tp.Name.Contains("error"))
                        {
                            Console.WriteLine(GetGError(tp));
                        }
                        else
                        {
                            if (sdp.type == "offer")
                            {
                                Structure structure = new Structure("struct");
                                var pp = new Promise(p2 => { // on answer created
                                    p2.Wait();
                                    var reply = p2.RetrieveReply();
                                    if (reply.HasField("error"))
                                    {
                                        Console.WriteLine(GetGError(reply));
                                    }
                                    else
                                    {
                                        var descr = reply.GetValue("answer");
                                        WebRTCSessionDescription offer = (WebRTCSessionDescription)descr.Val;
                                        var promise3 = new Promise();
                                        _webrtcbin.Emit("set-local-description", offer, promise3);
                                        promise3.Interrupt();
                    
                                        var sdpMsg2 = new SdpMsg { sdp = new SdpContent { type = "answer", sdp = offer.Sdp.AsText() } };
                                        Send(new WebMsg() { sdp = sdpMsg2});
                                    }
                                });
                                _webrtcbin.Emit("create-answer", structure, pp);
                            }
                        }
                    });
                    // var promise = new Promise();
                    _webrtcbin.Emit("set-remote-description", remoteDescription, promise);
                }
                else if (msg.ice != null)
                {
                    var ice = msg.ice;
                    string candidate = ice.candidate;
                    uint sdpMLineIndex = ice.sdpMLineIndex;
                    _webrtcbin.Emit("add-ice-candidate", sdpMLineIndex, candidate);
                }
            }
            
            protected override void OnMessage(MessageEventArgs e)
            {
                if (e.IsText)
                {
                    if (e.Data == "HELLO")
                    {
                        Console.WriteLine("Client said hello, starting pipeline");
                        _pipeline.SetState(Gst.State.Playing);
                    }
                    else
                    {
                        var msg = JsonConvert.DeserializeObject<WebMsg>(e.Data);
                        if (msg.chat != null)
                        {
                            _client._app.PushChatMessage(_client.Name, msg.chat.text);
                        }
                        else if (msg.sdp != null)
                        {
                            //_client.InitiateAudioLink(msg.sdp);
                            HandleIncomingSdp(msg.sdp);
                        }
                    }
                }
                else
                {
                    throw new NotImplementedException("");
                }
            }

            public void Send(WebMsg msg)
            {
                if (this.State == WebSocketState.Open)
                {
                    var data = JsonConvert.SerializeObject(msg);
                    System.Diagnostics.Debug.Print("Sending " + data);
                    this.Send(data);
                }
            }

            protected override void OnOpen()
            {
                _client._app.PushUserlist();
            }

            protected override void OnError(WebSocketSharp.ErrorEventArgs e)
            {
            }

            protected override void OnClose(CloseEventArgs e)
            {
                _client._app.Disconnect(_client);
            }
        }

        public int Id { get; }
        public string Name { get; private set; }

        System.DateTime _lastActivity;

        readonly Program _app;

        readonly WebSocketHandler _webSocketHandler;
        public WebSocketBehavior WebSocketBehavior { get { return _webSocketHandler; } }

        // WebRtcAudioConnectionPoint _audioConnectionPoint;
        Element[] _ins = null, _outs = null;

        public Client(Program app, int id)
        {
            this.Id = id;

            _app = app;

            _webSocketHandler = new WebSocketHandler(this);            
        }
        
        public void SendChatMessage(ChatMsg chatMsg)
        {
            _webSocketHandler.Send(new WebMsg() { chat = chatMsg });
        }

        public void SendControlMessage(ControlMsg msg)
        {
            _webSocketHandler.Send(new WebMsg() { control = msg });
        }
    }

    class Program
    {
        int _idCounter = 0;

        readonly object _clientsLock = new object();
        readonly Dictionary<int, Client> _unauthenticatedClientsById = new Dictionary<int, Client>();
        readonly Dictionary<int, Client> _clientsById = new Dictionary<int, Client>();

        readonly HttpServer _httpServer;

        public readonly Pipeline _pipeline;
        readonly Element _mixer;

        public Pipeline Pipeline { get { return _pipeline; } }
        public Element Mixer { get { return _mixer; } }

        public int clientsCount = 0;

        public Program()
        {
            _httpServer = new HttpServer(IPAddress.Any, 443, true);
            _httpServer.OnGet += (sender, ea) => this.OnWebGet(ea);
            _httpServer.OnPost += (sender, ea) => this.OnWebPost(ea);
            _httpServer.AddWebSocketService("/sck", () => {
                var client = new Client(this, Interlocked.Increment(ref _idCounter));
                lock (_clientsLock)
                {
                    _unauthenticatedClientsById.Add(client.Id, client);
                }
                return client.WebSocketBehavior;
            });
            _httpServer.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls13;
            _httpServer.SslConfiguration.ServerCertificate = new X509Certificate2("C:\\Windows\\System32\\cert.pfx", "danil1209");
            _httpServer.SslConfiguration.ClientCertificateRequired = false;
            _httpServer.SslConfiguration.CheckCertificateRevocation = false;
            _httpServer.Start();

            _pipeline = new Pipeline();

            var mixer = ElementFactory.Make("compositor", "mixer");
            var filter = ElementFactory.Make("capsfilter");
            Util.SetObjectArg(filter, "caps", "video/x-raw, width=1280, height=720");
            var convert = ElementFactory.Make("videoconvert");
            var queue = ElementFactory.Make("queue");
            var tee = ElementFactory.Make("tee", "splitter");
            var videoSink = ElementFactory.Make("autovideosink");
            _pipeline.Add(mixer, filter, convert, queue, tee, videoSink);
            Element.Link(mixer, filter, convert, queue, tee);

            var padTemplate = tee.PadTemplateList.First(it => it.Name.Contains("src"));
            var teePad = tee.RequestPad(padTemplate);
            teePad.Link(videoSink.GetStaticPad("sink"));

            // _mixer = ElementFactory.Make("audiomixer");
            // _pipeline.Add(_mixer);

            // _pipeline.SetState(State.Playing);
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

                var location = "C:\\Users\\danil\\RiderProjects\\MyGstreamerApp\\SimpleWebRTCBinTest";

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

        public void Authenticate(int clientId)
        {
            Client client;

            lock (_clientsLock)
            {
                if (_unauthenticatedClientsById.TryGetValue(clientId, out client))
                {
                    _clientsById.Add(clientId, client);
                    _unauthenticatedClientsById.Remove(clientId);
                }
                else
                {
                    client = null;
                }
            }

            if (client != null)
            {
                client.SendControlMessage(new ControlMsg() { authenticated = true });
                // client.InitiateAudioLink();
                this.PushUserlist();
            }
        }

        public void PushChatMessage(string name, string text)
        {
            Client[] clients;
            lock (_clientsLock)
            {
                clients = _clientsById.Values.Concat(_unauthenticatedClientsById.Values).ToArray();
            }

            var chatMsg = new ChatMsg() {
                text = $"[{name}] - {text}"
            };
            clients.ForEach(c => c.SendChatMessage(chatMsg));
        }

        public void PushUserlist()
        {
            Client[] clients;
            int awaiting;
            lock (_clientsLock)
            {
                clients = _clientsById.Values.Concat(_unauthenticatedClientsById.Values).ToArray();
                awaiting = _unauthenticatedClientsById.Count;
            }

            var userlist = new ControlMsg() {
                userlist = new UserlistMsg() {
                    names = clients.Where(c => c.Name != null).Select(c => c.Name).ToArray(),
                    awaiting = awaiting
                }
            };
            clients.ForEach(c => c.SendControlMessage(userlist));
        }

        public void Disconnect(Client client)
        {
            lock (_clientsLock)
            {
                (client.Name == null ? _unauthenticatedClientsById : _clientsById).Remove(client.Id);
            }

            this.PushUserlist();
        }

        public void RunPipeline()
        {
            // Wait until error, EOS or State Change
            var bus = _pipeline.Bus;
            bool terminate = false;
            do
            {
                var msg = bus.TimedPopFiltered(Gst.Constants.SECOND, MessageType.Error | MessageType.Eos | MessageType.StateChanged);
                // Parse message
                if (msg != null)
                {
                    switch (msg.Type)
                    {
                        case MessageType.Error:
                            string debug;
                            GLib.GException exc;
                            msg.ParseError(out exc, out debug);
                            Console.WriteLine("Error received from element {0}: {1}", msg.Src.Name, exc.Message);
                            Console.WriteLine("Debugging information: {0}", debug != null ? debug : "none");
                            terminate = true;
                            break;
                        case MessageType.Eos:
                            Console.WriteLine("End-Of-Stream reached.\n");
                            terminate = true;
                            break;
                        case MessageType.StateChanged:
                            // We are only interested in state-changed messages from the pipeline
                            if (msg.Src == _pipeline)
                            {
                                State oldState, newState, pendingState;
                                msg.ParseStateChanged(out oldState, out newState, out pendingState);
                                Console.WriteLine("Pipeline state changed from {0} to {1}:",
                                    Element.StateGetName(oldState), Element.StateGetName(newState));
                            }
                            break;
                        default:
                            // We should not reach here because we only asked for ERRORs, EOS and STATE_CHANGED
                            Console.WriteLine("Unexpected message received.");
                            break;
                    }
                }
            } while (!terminate);
        }


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

            app.RunPipeline();
        }
    }

    static class Extensions
    {
        public static GLib.Value AsGLibValue<T>(this T obj)
            where T : struct
        {
            return new GLib.Value(obj);
        }

        public static GLib.Value AsGRefValue<T>(this T obj)
            where T : class
        {
            return new GLib.Value(obj);
        }

        public static void ForEach<T>(this IEnumerable<T> seq, Action<T> act)
        {
            foreach (var item in seq)
            {
                act(item);
            }
        }
    }
}
