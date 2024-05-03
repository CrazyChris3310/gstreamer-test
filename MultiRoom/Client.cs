using System.Runtime.InteropServices;
using System.Text;
using GLib;
using Gst;
using Gst.Sdp;
using Gst.WebRTC;
using Newtonsoft.Json;
using WebSocketSharp;
using WebSocketSharp.Server;
using static System.Diagnostics.Debug;
using Object = Gst.Object;
using Value = GLib.Value;

namespace MultiRoom;

public class Client : WebSocketBehavior
{
    private int id;
    private Pipeline _pipeline;

    private Element incomingWebrtc;

    private Dictionary<int, Element> outgoingWebrtc = new();

    private Element outgoingPeer;

    public Action<Pad, GLib.SignalArgs> OnTrack;

    public Client(Pipeline pipeline, int id)
    {
        _pipeline = pipeline;
        this.id = id;
        incomingWebrtc = CreateWebrtcBin(-1);
        _pipeline.Add(incomingWebrtc);
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
            
            OnTrack(conv.GetStaticPad("src"), args);

            q.SetState(Gst.State.Playing);
            conv.SetState(Gst.State.Playing);
        }

    }

    public void AddPeer(Pad srcPad, int dest)
    {
        // var source = ElementFactory.Make("videotestsrc");
        // source.SetProperty("pattern", new Value(18));
        var queue = ElementFactory.Make("queue");
        var vp8enc = ElementFactory.Make("vp8enc");
        var rtppayload = ElementFactory.Make("rtpvp8pay");
        var filter = ElementFactory.Make("capsfilter");
        Util.SetObjectArg(filter, "caps", "application/x-rtp,media=video,encoding-name=VP8,payload=96");
        
        var outgoing = CreateWebrtcBin(dest);
        // outgoing.Connect("on-new-transceiver", (o, args) =>
        // {
        //     var transceiver = args.Args[0] as Object;
        //     Console.WriteLine($"transceiver added: direction={transceiver.GetProperty("direction").Val}, " +
        //                       $"kind={transceiver.GetProperty("kind").Val}, " +
        //                       $"mid={transceiver.GetProperty("mid").Val}, " +
        //                       $"mlineindex={transceiver.GetProperty("mlineindex").Val}");
        // });
        // outgoing.Emit("add-transceiver", WebRTCRTPTransceiverDirection.Sendonly,
        //     Caps.FromString("application/x-rtp,media=video,encoding-name=VP8/9000,payload=96")
        // );
        outgoingWebrtc[dest] = outgoing;
        // outgoingPeer = outgoing;
        
        _pipeline.Add(queue, vp8enc, rtppayload, filter, outgoing);
        Element.Link(queue, vp8enc, rtppayload, filter);

        var padLinkReturn = srcPad.Link(queue.GetStaticPad("sink"));
        Console.WriteLine("Result of linking tee with encoder sink: " + padLinkReturn);
        
        var sinkPadTemplate = outgoing.PadTemplateList.First(it => it.Name.Contains("sink"));
        var sinkPad = outgoing.RequestPad(sinkPadTemplate);
        var linkReturn = filter.GetStaticPad("src").Link(sinkPad);
        Console.WriteLine("Result of linking filter with webrtc sink: " + linkReturn);

        //
        // _pipeline.Add(eleemnt);
        // _pipeline.SyncChildrenStates();

        _pipeline.SyncChildrenStates();

        // source.SetState(Gst.State.Playing);
        vp8enc.SetState(Gst.State.Playing);
        rtppayload.SetState(Gst.State.Playing);
        queue.SetState(Gst.State.Playing);
        filter.SetState(Gst.State.Playing);
        outgoing.SetState(Gst.State.Playing);
    }

    private Element CreateWebrtcBin(int dest)
    {
        var webrtc = ElementFactory.Make("webrtcbin", $"peer_{id}_{dest}");
        
        webrtc.SetProperty("stun-server", new Value("stun://stun.l.google.com:19302"));
        webrtc.SetProperty("bundle-policy",  new Value(WebRTCBundlePolicy.MaxBundle));
        if (dest > 0)
        {
            webrtc.Connect("on-negotiation-needed", (o, args) => OnNegotiationNeeded(o, args, dest));
        }
        webrtc.Connect("pad-added", (object o, SignalArgs args) =>
        {
            Console.WriteLine("Pad has been added");
            var newPad = (Pad)args.Args[0];
            if (newPad.Direction == PadDirection.Sink)
            {
                Console.WriteLine("Sink padd added on element " + (o as Object).Name);
                return;
            }
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
        
        webrtc.Connect("on-ice-candidate", (o, args) => OnIceCandidate(o, args, dest));
        
        return webrtc;
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

    void OnIceCandidate(object o, GLib.SignalArgs args, int dest)
    {
        var index = (uint)args.Args[0];
        var cand = (string)args.Args[1];
        var iceMsg = new SdpMsg { ice = new SdpIceContent { sdpMLineIndex = index, candidate = cand } };

        Send(new WebMsg() { sdp = iceMsg, src = id, dest = dest});
    }

    void OnNegotiationNeeded(object o, GLib.SignalArgs args, int dest)
    {
        Console.WriteLine("Renegotiation needed event fired");
        var webRtc = o as Element;
        Assert(webRtc != null, "not a webrtc object");

        //_client.CreateSendingChain(_webrtcbin);

        var promise = new Promise((promise) => OnOfferCreated(promise, dest)); //, webrtc.Handle, null); // webRtc.Handle, null);
        Structure structure = new Structure("struct");
        webRtc.Emit("create-offer", structure, promise);
    }

    void OnOfferCreated(Promise promise, int dest)
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
            var peer = dest == -1 ? incomingWebrtc : outgoingWebrtc[dest];
            peer.Emit("set-local-description", offer, promise);
            promise.Interrupt();

            var sdpMsg = new SdpMsg { sdp = new SdpContent { type = "offer", sdp = offer.Sdp.AsText() } };
            Send(new WebMsg() { sdp = sdpMsg, src = id, dest = dest });
        }
    }

    public void HandleIncomingSdp(SdpMsg msg, int dest)
    {
        var peer = dest == -1 ? incomingWebrtc : outgoingWebrtc[dest];
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
                                peer.Emit("set-local-description", offer, promise3);
                                promise3.Interrupt();
            
                                var sdpMsg2 = new SdpMsg { sdp = new SdpContent { type = "answer", sdp = offer.Sdp.AsText() } };
                                Send(new WebMsg() { sdp = sdpMsg2, dest = dest });
                            }
                        });
                        peer.Emit("create-answer", structure, pp);
                    }
                }
            });
            // var promise = new Promise();
            peer.Emit("set-remote-description", remoteDescription, promise);
        }
        else if (msg.ice != null)
        {
            var ice = msg.ice;
            string candidate = ice.candidate;
            uint sdpMLineIndex = ice.sdpMLineIndex;
            peer.Emit("add-ice-candidate", sdpMLineIndex, candidate);
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
                if (msg.sdp != null)
                {
                    //_client.InitiateAudioLink(msg.sdp);
                    HandleIncomingSdp(msg.sdp, msg.dest);
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

    // protected override void OnOpen()
    // {
    //     _client._app.PushUserlist();
    // }
    //
    // protected override void OnError(WebSocketSharp.ErrorEventArgs e)
    // {
    // }
    //
    // protected override void OnClose(CloseEventArgs e)
    // {
    //     _client._app.Disconnect(_client);
    // }
}