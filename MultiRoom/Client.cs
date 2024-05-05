using System.Runtime.InteropServices;
using System.Text;
using GLib;
using Gst;
using Gst.Sdp;
using Gst.Video;
using Gst.WebRTC;
using Newtonsoft.Json;
using WebSocketSharp;
using WebSocketSharp.Server;
using static System.Diagnostics.Debug;
using Object = Gst.Object;
using Value = GLib.Value;

namespace MultiRoom;

public class IncomingFlow
{
    public Element webrtcbin;
    public Element decodebin;
    public Element queue;
    public Element videoconvert;
}

public class OutgoingFlow
{
    public MediaType MediaType;
    public Pad srcPad;
    public Element queue;
    public Element encoder;
    public Element payloader;
    public Element filter;
}

public class MediaFlow
{
    public OutgoingFlow? audio;
    public OutgoingFlow? video;
    public Element webrtcbin;
}

public class Client : WebSocketBehavior
{
    public int id;
    private Pipeline _pipeline;
    
    public Element? tee;
    public Element? audioTee;

    private IncomingFlow incoming;

    private Dictionary<int, MediaFlow> outgoingFlows = new();

    public Action<Pad, GLib.SignalArgs, MediaType> OnTrack;
    public Action OnSocketClosed;

    public Client(Pipeline pipeline, int id)
    {
        _pipeline = pipeline;
        this.id = id;
        incoming = new IncomingFlow
        {
            webrtcbin = CreateWebrtcBin(-1)
        };
        _pipeline.Add(incoming.webrtcbin);
    }

    private void OnIncomingDecodeBinStream(object o, GLib.SignalArgs args, int dest)
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
            tee = ElementFactory.Make("tee");

            incoming.queue = q;
            incoming.videoconvert = conv;

            _pipeline.Add(q, conv, tee);
            q.Link(conv);
            conv.Link(tee);
            newPad.Link(q.GetStaticPad("sink"));
            
            OnTrack(conv.GetStaticPad("src"), args, MediaType.VIDEO);

            q.SetState(Gst.State.Playing);
            conv.SetState(Gst.State.Playing);
            tee.SetState(Gst.State.Playing);
        }
        else if (name.StartsWith("audio"))
        {
            var q = ElementFactory.Make("queue");
            var conv = ElementFactory.Make("audioconvert");
            var resample = ElementFactory.Make("audioresample");
            audioTee = ElementFactory.Make("tee");
            var fakeSink = ElementFactory.Make("fakesink");
            
            _pipeline.Add(q, conv, resample, audioTee, fakeSink);
            Element.Link(q, conv, resample, audioTee);
            newPad.Link(q.GetStaticPad("sink"));

            var srcPad = audioTee.PadTemplateList.First(it => it.Name.Contains("src"));
            var teeSrcPad = audioTee.RequestPad(srcPad);
            teeSrcPad.Link(fakeSink.GetStaticPad("sink"));

            OnTrack(resample.GetStaticPad("src"), args, MediaType.AUDIO);
            
            _pipeline.SyncChildrenStates();
            q.SetState(Gst.State.Playing);
            conv.SetState(Gst.State.Playing);
            resample.SetState(Gst.State.Playing);
            audioTee.SetState(Gst.State.Playing);
            fakeSink.SetState(Gst.State.Playing);
        }

    }

    private OutgoingFlow CreatePeerFlow(Pad srcPad, int dest, string encName, string payName, string caps)
    {
        var queue = ElementFactory.Make("queue");
        var encoder = ElementFactory.Make(encName);
        var payloader = ElementFactory.Make(payName);
        var filter = ElementFactory.Make("capsfilter");
        Util.SetObjectArg(filter, "caps", caps);
        var outgoing = CreateWebrtcBin(dest);

        var outgoingFlow = new OutgoingFlow()
        {
            srcPad = srcPad,
            queue = queue,
            encoder = encoder,
            payloader = payloader,
            filter = filter
        };

        return outgoingFlow;

        _pipeline.Add(queue, encoder, payloader, filter, outgoing);
        Element.Link(queue, encoder, payloader, filter);

        var padLinkReturn = srcPad.Link(queue.GetStaticPad("sink"));
        Console.WriteLine("Result of linking tee with encoder sink: " + padLinkReturn);
        
        var sinkPadTemplate = outgoing.PadTemplateList.First(it => it.Name.Contains("sink"));
        var sinkPad = outgoing.RequestPad(sinkPadTemplate);
        var linkReturn = filter.GetStaticPad("src").Link(sinkPad);
        Console.WriteLine("Result of linking filter with webrtc sink: " + linkReturn);

        _pipeline.SyncChildrenStates();

        encoder.SetState(Gst.State.Playing);
        payloader.SetState(Gst.State.Playing);
        queue.SetState(Gst.State.Playing);
        filter.SetState(Gst.State.Playing);
        outgoing.SetState(Gst.State.Playing);
    }

    public void TryPlayFlow(MediaFlow mediaFlow, int dest)
    {
        if (mediaFlow is { video: not null, audio: not null })
        {
            mediaFlow.webrtcbin = CreateWebrtcBin(dest);
            _pipeline.Add(mediaFlow.webrtcbin);
            PlayFlow(mediaFlow.video, mediaFlow.webrtcbin, "video");
            PlayFlow(mediaFlow.audio, mediaFlow.webrtcbin, "audio");
            mediaFlow.webrtcbin.SetState(Gst.State.Playing);
        }
    }

    private void PlayFlow(OutgoingFlow flow, Element werbtc, string type)
    {
        _pipeline.Add(flow.queue, flow.encoder, flow.payloader, flow.filter);
        Element.Link(flow.queue, flow.encoder, flow.payloader, flow.filter);

        var padLinkReturn = flow.srcPad.Link(flow.queue.GetStaticPad("sink"));
        Console.WriteLine("Result of linking tee with queue sink: " + padLinkReturn + " type " + type);
        
        var sinkPadTemplate = werbtc.PadTemplateList.First(it => it.Name.Contains("sink"));
        var sinkPad = werbtc.RequestPad(sinkPadTemplate);
        var linkReturn = flow.filter.GetStaticPad("src").Link(sinkPad);
        Console.WriteLine("Result of linking filter with webrtc sink: " + linkReturn);

        _pipeline.SyncChildrenStates();

        flow.encoder.SetState(Gst.State.Playing);
        flow.payloader.SetState(Gst.State.Playing);
        flow.queue.SetState(Gst.State.Playing);
        flow.filter.SetState(Gst.State.Playing);
    }

    public void AddAudioPeer(Pad srcPad, int dest)
    {
        string caps = "application/x-rtp,media=audio,encoding-name=OPUS,payload=96";
        var outgoingFlow = CreatePeerFlow(srcPad, dest, "opusenc", "rtpopuspay", caps);
        var mediaFlow = outgoingFlows.GetValueOrDefault(dest, new MediaFlow());
        mediaFlow.audio = outgoingFlow;
        outgoingFlows[dest] = mediaFlow;
        TryPlayFlow(mediaFlow, dest);
    }

    public void AddVideoPeer(Pad srcPad, int dest)
    {
        string caps = "application/x-rtp,media=video,encoding-name=VP8,payload=96";
        var outgoingFlow = CreatePeerFlow(srcPad, dest, "vp8enc", "rtpvp8pay", caps);
        var mediaFlow = outgoingFlows.GetValueOrDefault(dest, new MediaFlow());
        mediaFlow.video = outgoingFlow;
        outgoingFlows[dest] = mediaFlow;
        TryPlayFlow(mediaFlow, dest);
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
            incoming.decodebin = decodeBin;
            decodeBin.Connect("pad-added", (o, args) => OnIncomingDecodeBinStream(o, args, dest));

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

    [DllImport("libgstreamer-1.0-0.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr gst_structure_get_value(IntPtr raw, IntPtr fieldname);

    [DllImport("libgobject-2.0-0.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr g_value_get_pointer(IntPtr val);

    [DllImport("libgobject-2.0-0.dll", CallingConvention = CallingConvention.Cdecl)] 
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
            var peer = dest == -1 ? incoming.webrtcbin : outgoingFlows[dest].webrtcbin;
            peer.Emit("set-local-description", offer, promise);
            promise.Interrupt();

            var sdpMsg = new SdpMsg { sdp = new SdpContent { type = "offer", sdp = offer.Sdp.AsText() } };
            Send(new WebMsg() { sdp = sdpMsg, src = id, dest = dest });
        }
    }

    public void HandleIncomingSdp(SdpMsg msg, int dest)
    {
        var peer = dest == -1 ? incoming.webrtcbin : outgoingFlows[dest].webrtcbin;
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

    public void RemoveOutgoingPeer(int index, Pad otherPeerFlow)
    {
        // todo: remove also audio
        var outgoingFlow = outgoingFlows[index];
        var video = outgoingFlow.video;
        
        video.encoder.SetState(Gst.State.Null);
        video.payloader.SetState(Gst.State.Null);
        video.filter.SetState(Gst.State.Null);
        video.queue.SetState(Gst.State.Null);
        outgoingFlow.webrtcbin.SetState(Gst.State.Null);

        var teeSrc = otherPeerFlow.Peer;
        otherPeerFlow.Unlink(teeSrc);
        tee.ReleaseRequestPad(teeSrc);
        
        _pipeline.Remove(video.queue, video.payloader, video.encoder, video.filter,
            outgoingFlow.webrtcbin);

        outgoingFlows.Remove(index);
        
        Send(new WebMsg() { control = ControlMsg.REMOVE_PEER.ToString(), dest = index });
    }

    public Pad GetOutgoingPeerPad(int index)
    {
        // todo: only video
        var outgoingFlow = outgoingFlows[index].video;
        return outgoingFlow.queue.GetStaticPad("sink");
    }
    
    protected override void OnClose(CloseEventArgs e)
    {
        Console.WriteLine("Closing socket");
        OnSocketClosed();
    }
}