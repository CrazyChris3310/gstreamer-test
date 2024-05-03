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

    class WebRtcAudioConnectionPoint
    {
        const string PIPELINE_DESC = @"webrtcbin name=sendrecv bundle-policy=max-bundle
 videotestsrc is-live=true pattern=circular ! videoconvert ! queue ! vp8enc deadline=1 ! rtpvp8pay !
 queue ! application/x-rtp,media=video,encoding-name=VP8,payload=97 ! sendrecv.
 audiotestsrc is-live=true wave=red-noise ! audioconvert ! audioresample ! queue ! opusenc ! rtpopuspay !
 queue ! application/x-rtp,media=audio,encoding-name=OPUS,payload=96 ! sendrecv.";

        readonly Client _client;

        readonly Element _webrtcbin;

        public Element WebRtcBin { get { return _webrtcbin; } }

        public event Action<SdpMsg> OnIceCandidateToDeliver = delegate { };
        public event Action<SdpMsg> OnSdpOfferToDeliver = delegate { };

        public event Action<Pad> OnStreamPad = delegate { };

        public WebRtcAudioConnectionPoint(Client client)
        {
            _client = client;

            _webrtcbin = ElementFactory.Make("webrtcbin");
            _webrtcbin.SetProperty("stun-server", new Value("stun://stun.l.google.com:19302"));
            _webrtcbin.SetProperty("bundle-policy", WebRTCBundlePolicy.MaxBundle.AsGLibValue());
            _webrtcbin.Connect("on-negotiation-needed", this.OnNegotiationNeeded);
            _webrtcbin.Connect("on-ice-candidate", this.OnIceCandidate);
            _webrtcbin.Connect("pad-added", this.OnIncomingStream);
            //webrtc = pipe.GetByName("sendrecv");
        }

        public void Start()
        {
            _webrtcbin.SetState(State.Playing);
        }

        public void HandleIncomingSdp(SdpMsg msg)
        {
            if (msg.sdp != null)
            {
                var sdp = msg.sdp;
                string sdpAns = sdp.sdp;
                Console.WriteLine($"received answer:\n{sdpAns}");
                SDPMessage.New(out SDPMessage sdpMsg);
                SDPMessage.ParseBuffer(ASCIIEncoding.Default.GetBytes(sdpAns), (uint)sdpAns.Length, sdpMsg);
                var answer = WebRTCSessionDescription.New(WebRTCSDPType.Answer, sdpMsg);
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
                                if (reply.Name.Contains("error"))
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
                                    this.OnSdpOfferToDeliver(sdpMsg2);
                                }
                            });
                            _webrtcbin.Emit("create-answer", structure, pp);
                        }
                    }
                });
                // var promise = new Promise();
                _webrtcbin.Emit("set-remote-description", answer, promise);
            }
            else if (msg.ice != null)
            {
                var ice = msg.ice;
                string candidate = ice.candidate;
                uint sdpMLineIndex = ice.sdpMLineIndex;
                _webrtcbin.Emit("add-ice-candidate", sdpMLineIndex, candidate);
            }
        }

        void OnIceCandidate(object o, GLib.SignalArgs args)
        {
            var index = (uint)args.Args[0];
            var cand = (string)args.Args[1];
            var iceMsg = new SdpMsg { ice = new SdpIceContent { sdpMLineIndex = index, candidate = cand } };

            this.OnIceCandidateToDeliver(iceMsg);
        }

        void OnNegotiationNeeded(object o, GLib.SignalArgs args)
        {
            var webRtc = o as Element;
            Assert(webRtc != null, "not a webrtc object");
            
            //_client.CreateSendingChain(_webrtcbin);

            Promise promise = new Promise(this.OnOfferCreated);//, webrtc.Handle, null); // webRtc.Handle, null);
            Structure structure = new Structure("struct");
            webRtc.Emit("create-offer", structure, promise);
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
                this.OnSdpOfferToDeliver(sdpMsg);
            }
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


        void OnIncomingStream(object o, GLib.SignalArgs args)
        {
            var pad = args.Args[0] as Pad;
            this.OnStreamPad(pad);
        }
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

    class Client
    {
        public class WebSocketHandler : WebSocketBehavior
        {
            private readonly Client _client;

            public WebSocketHandler(Client client)
            {
                _client = client;
            }

            protected override void OnMessage(MessageEventArgs e)
            {
                if (e.IsText)
                {
                    if (_client.Name != null)
                    {
                        var msg = JsonConvert.DeserializeObject<WebMsg>(e.Data);
                        if (msg.chat != null)
                        {
                            _client._app.PushChatMessage(_client.Name, msg.chat.text);
                        }
                        else if (msg.sdp != null)
                        {
                            //_client.InitiateAudioLink(msg.sdp);
                            _client._audioConnectionPoint.HandleIncomingSdp(msg.sdp);
                        }
                    }
                    else
                    {
                        _client.Authenticate(e.Data);
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

        WebRtcAudioConnectionPoint _audioConnectionPoint;
        Element[] _ins = null, _outs = null;

        public Client(Program app, int id)
        {
            this.Id = id;

            _app = app;

            _webSocketHandler = new WebSocketHandler(this);            
        }

        private void Authenticate(string name)
        {
            this.Name = name;
            _app.Authenticate(this.Id);
            this.InitiateAudioLink();
        }

        public void Renew()
        {
            _lastActivity = System.DateTime.Now;
        }

        private void AudioConnectionPoint_OnIncomingStreamToDecode(Pad pad)
        {
            switch (pad.Direction)
            {
                case PadDirection.Src:
                    {
                        var decodebin = ElementFactory.Make("decodebin");
                        decodebin.Connect("pad-added", this.OnIncomingDecodebinStream);
                        _app.Pipeline.Add(decodebin);
                        decodebin.SyncStateWithParent();
                        _audioConnectionPoint.WebRtcBin.Link(decodebin);
                    }
                    break;
                case PadDirection.Sink:
                    {
                    }
                    break;
                case PadDirection.Unknown:
                default:
                    throw new NotImplementedException("");
            }
        }

        public void InitiateAudioLink()
        {
            _audioConnectionPoint = new WebRtcAudioConnectionPoint(this);
            _audioConnectionPoint.OnStreamPad += this.AudioConnectionPoint_OnIncomingStreamToDecode;
            _audioConnectionPoint.OnSdpOfferToDeliver += s => _webSocketHandler.Send(new WebMsg() { sdp = s });
            _audioConnectionPoint.OnIceCandidateToDeliver += s => _webSocketHandler.Send(new WebMsg() { sdp = s });
            this.CreateSendingChain(_audioConnectionPoint.WebRtcBin);

            ///_audioConnectionPoint.HandleIncomingSdp(sdp);
            if (_app.Pipeline.CurrentState != State.Playing)
                _app.Pipeline.SetState(State.Playing);

            _audioConnectionPoint.Start();
        }

        void OnIncomingDecodebinStream(object o, SignalArgs args)
        {
            var pad = (Pad)args.Args[0];
            if (!pad.HasCurrentCaps)
            {
                Console.WriteLine($"{pad.Name} has no caps, ignoring");
                return;
            }

            var caps = pad.CurrentCaps;
            Assert(!caps.IsEmpty);
            Structure s = caps[0];
            var name = s.Name;
            if (name.StartsWith("audio"))
            {
                switch (pad.Direction)
                {
                    case PadDirection.Src:
                        {
                            if (_ins != null)
                                _ins.ForEach(e => e.Dispose());

                            var q = ElementFactory.Make("queue");
                            var conv = ElementFactory.Make("audioconvert");
                            var resample = ElementFactory.Make("audioresample");
                            _app.Pipeline.Add(q, conv, resample);
                            _app.Pipeline.SyncChildrenStates();
                            pad.Link(q.GetStaticPad("sink"));
                            Element.Link(q, conv, resample, _app.Mixer);
                            _ins = new[] { q, conv, resample };
                            _ins.ForEach(x => x.SetState(State.Playing));
                        }
                        break;
                    case PadDirection.Sink:
                        {
                            // this.CreateSendingChain();
                        }
                        break;
                    case PadDirection.Unknown:
                    default:
                        throw new NotImplementedException("");
                }
            }
            else
            {
                throw new NotImplementedException("");
            }
        }

        public void CreateSendingChain(Element target)
        {
            if (_outs != null)
                _outs.ForEach(e => e.Dispose());

            var resample = ElementFactory.Make("audioresample");
            var queue1 = ElementFactory.Make("queue");
            var opusenc = ElementFactory.Make("opusenc");
            var rtpopuspay = ElementFactory.Make("rtpopuspay");
            var queue2 = ElementFactory.Make("queue");

            //audioconvert !audioresample !queue !opusenc !rtpopuspay ! queue ! application/x-rtp,media=audio,encoding-name=OPUS,payload=96 ! sendrecv.
            opusenc.SetProperty("max-payload-size", 96.AsGLibValue());

            _app.Pipeline.Add(resample, queue1, opusenc, rtpopuspay, queue2);
            _app.Pipeline.SyncChildrenStates();
            Element.Link(_app.Mixer, resample, queue1, opusenc, rtpopuspay, queue2);
            // queue2.GetStaticPad("src").Link(target);
            _outs = new[] { resample, queue1, opusenc, rtpopuspay, queue2, target };
            _outs.ForEach(x => x.SetState(State.Playing));
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

        readonly Pipeline _pipeline;
        readonly Element _mixer;

        public Pipeline Pipeline { get { return _pipeline; } }
        public Element Mixer { get { return _mixer; } }

        public Program()
        {
            _httpServer = new HttpServer(IPAddress.Any, 80, false);
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
            _httpServer.Start();

            _pipeline = new Pipeline();

            _mixer = ElementFactory.Make("audiomixer");
            _pipeline.Add(_mixer);

            {
                // audiotestsrc is-live = true wave = red - noise !audioconvert !audioresample !queue !opusenc !rtpopuspay !
                var testsrc = ElementFactory.Make("audiotestsrc");
                testsrc.SetProperty("is-live", true.AsGLibValue());
                testsrc.SetProperty("wave", "red-noise".AsGRefValue());
                var conv = ElementFactory.Make("audioconvert");
                var resample = ElementFactory.Make("audioresample");
                var queue = ElementFactory.Make("queue");
                _pipeline.Add(testsrc, conv, resample, queue, _mixer);
                _pipeline.SyncChildrenStates();
                Element.Link(testsrc, conv, resample, queue, _mixer);
            }
            {
                var q = ElementFactory.Make("queue");
                var conv = ElementFactory.Make("audioconvert");
                var resample = ElementFactory.Make("audioresample");
                var sink = ElementFactory.Make("autoaudiosink");
                _pipeline.Add(q, conv, resample, sink);
                _pipeline.SyncChildrenStates();
                Element.Link(_mixer, q, conv, resample, sink);
            }

            _pipeline.SetState(State.Playing);
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

                var location = "C:\\Users\\danil\\RiderProjects\\MyGstreamerApp\\GstreamerDynamic";

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
