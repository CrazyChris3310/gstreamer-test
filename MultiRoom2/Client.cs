using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ObjectiveC;
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
using Task = Gst.Task;
using Thread = System.Threading.Thread;
using Value = GLib.Value;

namespace MultiRoom;

public abstract class Flow
{
    public MediaType mediaType;
    public string streamId;
}

public class IncomingFlow : Flow
{
    public Element decodebin;
    public Element queue;
    public Element converter;
    public Element fakesink;
    public Element tee;
}

public class OutgoingFlow : Flow
{
    public IncomingFlow IncomingFlow;
    public Pad srcPad;
    public Element queue;
    public Element encoder;
    public Element payloader;
    public Element filter;
}

public class MediaFlow<T>
{
//     public T? audio;
//     public T? video;
    public List<T> tracks = [];
    public Element webrtcbin;
}

public class Client : WebSocketBehavior
{
    public int id;
    private Pipeline _pipeline;

    private MediaFlow<IncomingFlow> incoming;
    private Dictionary<int, MediaFlow<OutgoingFlow>> outgoingFlows = new();
    private Thread? messageLoopThread = null;

    public Action OnSocketClosed;
    public Action<int, WebMsg> SendMessage;
    public Action<int, SdpMsg> readressSdp;
    public Action<int> OnStreamingStart;

    public bool isStreaming = false;

    public Client(int id, List<Client> otherClients)
    {
        this.id = id;
        incoming = new MediaFlow<IncomingFlow>()
        {
            webrtcbin = CreateWebrtcBin(-1)
        };
        _pipeline = new Pipeline();
        _pipeline.Bus.AddWatch(messageLoop);
        _pipeline.Add(incoming.webrtcbin);
        
        foreach (var otherClient in otherClients)
        {
            if (otherClient.id == id)
            {
                continue;
            }
            outgoingFlows[otherClient.id] = new MediaFlow<OutgoingFlow>() { webrtcbin = CreateWebrtcBin(otherClient.id) };
            _pipeline.Add(outgoingFlows[otherClient.id].webrtcbin);
        }
    }

    private void OnIncomingDecodeBinStream(object o, GLib.SignalArgs args, string streamId)
    {
        var decodeBin = (Element)o;
        
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
            var fakeSink = ElementFactory.Make("fakesink");
            var tee = ElementFactory.Make("tee");

            var incomingTrack = new IncomingFlow()
            {
                streamId = streamId,
                mediaType = MediaType.VIDEO,
                queue = q,
                converter = conv,
                decodebin = decodeBin,
                fakesink = fakeSink,
                tee = tee
            };
            incoming.tracks.Add(incomingTrack);

            _pipeline.Add(q, conv, tee, fakeSink);
            q.Link(conv);
            conv.Link(tee);
            newPad.Link(q.GetStaticPad("sink"));
            
            var srcPad = tee.PadTemplateList.First(it => it.Name.Contains("src"));
            var teeSrcPad = tee.RequestPad(srcPad);
            teeSrcPad.Link(fakeSink.GetStaticPad("sink"));
            Broadcast(incomingTrack);

            q.SetState(Gst.State.Playing);
            conv.SetState(Gst.State.Playing);
            tee.SetState(Gst.State.Playing);
            fakeSink.SetState(Gst.State.Playing);
        }
        else if (name.StartsWith("audio"))
        {
            var q = ElementFactory.Make("queue");
            var conv = ElementFactory.Make("audioconvert");
            var resample = ElementFactory.Make("audioresample");
            var fakeSink = ElementFactory.Make("fakesink");
            var audioTee = ElementFactory.Make("tee");

            var incomingTrack = new IncomingFlow()
            {
                streamId = streamId,
                mediaType = MediaType.AUDIO,
                decodebin = decodeBin,
                queue = q,
                converter = conv,
                fakesink = fakeSink,
                tee = audioTee
                // resample = resample,
            };
            incoming.tracks.Add(incomingTrack);
            
            _pipeline.Add(q, conv, resample, audioTee, fakeSink);
            Element.Link(q, conv, resample, audioTee);
            newPad.Link(q.GetStaticPad("sink"));

            var srcPad = audioTee.PadTemplateList.First(it => it.Name.Contains("src"));
            var teeSrcPad = audioTee.RequestPad(srcPad);
            teeSrcPad.Link(fakeSink.GetStaticPad("sink"));

            Broadcast(incomingTrack);
            
            _pipeline.SyncChildrenStates();
            q.SetState(Gst.State.Playing);
            conv.SetState(Gst.State.Playing);
            resample.SetState(Gst.State.Playing);
            audioTee.SetState(Gst.State.Playing);
            fakeSink.SetState(Gst.State.Playing);
        }
    }

    private void Broadcast(IncomingFlow flow)
    {
        foreach (var (clientId, outgoingFlow) in outgoingFlows)
        {
            AddStream(flow, clientId, outgoingFlow);
        }
    }

    public void AddPeer(int clientId)
    {
        new Thread(() =>
        {
            int retries = 10;

            var outgoingFlow = new MediaFlow<OutgoingFlow>() { webrtcbin = CreateWebrtcBin(clientId)};
            _pipeline.Add(outgoingFlow.webrtcbin);

            outgoingFlows[clientId] = outgoingFlow;

            foreach (var track in incoming.tracks)
            {
                AddStream(track, clientId, outgoingFlow);
            }

        }).Start();
    }

    private void AddStream(IncomingFlow flow, int clientId, MediaFlow<OutgoingFlow> outgoingFlow)
    {   
        var padTemplate = flow.tee.PadTemplateList.First(it => it.Name.Contains("src"));
        var newPad = flow.tee.RequestPad(padTemplate);
        if (flow.mediaType == MediaType.VIDEO)
        {
            AddVideoStream(newPad, clientId, outgoingFlow, flow.streamId);
        }
        else
        {
            AddAudioStream(newPad, clientId, outgoingFlow, flow.streamId);
        }
    }
    
    private OutgoingFlow CreatePeerFlow(Pad srcPad, int dest, string encName, string payName, string caps)
    {
        var queue = ElementFactory.Make("queue");
        var encoder = ElementFactory.Make(encName);
        var payloader = ElementFactory.Make(payName);
        var filter = ElementFactory.Make("capsfilter");
        Util.SetObjectArg(filter, "caps", caps);

        var outgoingFlow = new OutgoingFlow()
        {
            srcPad = srcPad,
            queue = queue,
            encoder = encoder,
            payloader = payloader,
            filter = filter
        };

        return outgoingFlow;
    }

    public void TryPlayFlow(MediaFlow<OutgoingFlow> mediaFlow, int dest, OutgoingFlow outgoingFlow)
    {
        PlayFlow(outgoingFlow, mediaFlow.webrtcbin);
        mediaFlow.webrtcbin.SetState(Gst.State.Playing);

        // if (mediaFlow is { video: not null, audio:not null})
        // {
        //     mediaFlow.webrtcbin = CreateWebrtcBin(dest);
        //     _pipeline.Add(mediaFlow.webrtcbin);
        //     PlayFlow(mediaFlow.video, mediaFlow.webrtcbin);
        //     PlayFlow(mediaFlow.audio, mediaFlow.webrtcbin);
        //     mediaFlow.webrtcbin.SetState(Gst.State.Playing);
        // }
    }
    
    private void PlayFlow(OutgoingFlow flow, Element werbtc)
    {
        _pipeline.Add(flow.queue, flow.encoder, flow.payloader, flow.filter);
        Element.Link(flow.queue, flow.encoder, flow.payloader, flow.filter);

        var padLinkReturn = flow.srcPad.Link(flow.queue.GetStaticPad("sink"));
        Console.WriteLine("Result of linking tee with queue sink: " + padLinkReturn + " type " + flow.mediaType);
        
        var sinkPadTemplate = werbtc.PadTemplateList.First(it => it.Name.Contains("sink"));
        var sinkPad = werbtc.RequestPad(sinkPadTemplate);
        sinkPad.SetProperty("msid", new Value(flow.streamId));
        var linkReturn = flow.filter.GetStaticPad("src").Link(sinkPad);
        Console.WriteLine("Result of linking filter with webrtc sink: " + linkReturn);

        _pipeline.SyncChildrenStates();

        flow.encoder.SetState(Gst.State.Playing);
        flow.payloader.SetState(Gst.State.Playing);
        flow.queue.SetState(Gst.State.Playing);
        flow.filter.SetState(Gst.State.Playing);
    }

    public void AddAudioStream(Pad srcPad, int dest, MediaFlow<OutgoingFlow> mediaFlow, string streamId)
    {
        string caps = "application/x-rtp,media=audio,encoding-name=OPUS,payload=96";
        var outgoingFlow = CreatePeerFlow(srcPad, dest, "opusenc", "rtpopuspay", caps);
        outgoingFlow.streamId = streamId;
        mediaFlow.tracks.Add(outgoingFlow);
        TryPlayFlow(mediaFlow, dest, outgoingFlow);
    }

    public void AddVideoStream(Pad srcPad, int dest, MediaFlow<OutgoingFlow> mediaFlow, string streamId)
    {
        string caps = "application/x-rtp,media=video,encoding-name=VP8,payload=96";
        var outgoingFlow = CreatePeerFlow(srcPad, dest, "vp8enc", "rtpvp8pay", caps);
        outgoingFlow.streamId = streamId;
        mediaFlow.tracks.Add(outgoingFlow);
        TryPlayFlow(mediaFlow, dest, outgoingFlow);
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
            if (!newPad.HasCurrentCaps)
            {
                Console.WriteLine($"{newPad.Name} has no caps, ignoring");
                return;
            }
            var streamId = newPad.GetProperty("msid").Val.ToString();
            if (newPad.Direction == PadDirection.Sink)
            {
                Console.WriteLine("Sink padd added on element " + (o as Object).Name +  ", streamId = " + streamId);
                return;
            }

            Console.WriteLine("Src pad added, streamId = " + streamId);
            
            var decodeBin = ElementFactory.Make("decodebin");
            decodeBin.Connect("pad-added", (o, args) => OnIncomingDecodeBinStream(o, args, streamId));
            
            newPad.AddProbe(PadProbeType.EventDownstream, (pad, info) => EventProbeCallback(pad, info, streamId));

            _pipeline.Add(decodeBin);
            var sinkPad = decodeBin.GetStaticPad("sink");
            var padLinkReturn = newPad.Link(sinkPad);
            decodeBin.SyncStateWithParent();
            Console.WriteLine("Pad link result: " + padLinkReturn);
        }); 
        webrtc.Connect("pad-removed", (o, args) =>
        {
            Console.WriteLine($"Removed pad from {id} to {dest}");
        });
        webrtc.Connect("on-ice-candidate", (o, args) => OnIceCandidate(o, args, dest));
        
        return webrtc;
    }

    private void RemoveStream(string streamId)
    {
        foreach (var (peerId, flow) in outgoingFlows)
        {
            // flow.webrtcbin.SetState(Gst.State.Null);
            // foreach (var track in flow.tracks)
            // {
            //     if (track.streamId != streamId) continue;
            //     track.queue.SetState(Gst.State.Null);
            //     track.encoder.SetState(Gst.State.Null);
            //     track.payloader.SetState(Gst.State.Null);
            //     track.filter.SetState(Gst.State.Null);
            //
            //     var tee = track.srcPad.ParentElement;
            //     tee.Unlink(track.queue);
            //     tee.ReleaseRequestPad(track.srcPad.Peer);
            //     
            //     track.filter.Unlink(flow.webrtcbin);
            //     flow.webrtcbin.ReleaseRequestPad(track.filter.GetStaticPad("src").Peer);
            //
            //     _pipeline.Remove(track.queue, track.encoder, track.payloader, track.filter);
            // }
            // flow.tracks = flow.tracks.Where(it => it.streamId != streamId).ToList();
            // flow.webrtcbin.SetState(Gst.State.Playing);
            
            SendMessage(peerId,
                new WebMsg()
                    { dest = id, control = new ControlMsg() { type = ControlMsgType.REMOVE_STREAM, streamId = streamId } });
        }
    }

    private PadProbeReturn EventProbeCallback(Pad pad, PadProbeInfo info, string streamId)
    {
        if (info.Event.Type != EventType.Eos)
            return PadProbeReturn.Ok;
        
        pad.RemoveProbe(info.Id);
        
        Console.WriteLine("EOS event received on client " + id);
        RemoveStream(streamId);
        
        return PadProbeReturn.Drop;
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

        if (dest == -1)
        {
            Send(new WebMsg() { sdp = iceMsg, src = id, dest = dest });
        }
        else
        {
            SendMessage(dest, new WebMsg() { sdp = iceMsg, src = id, dest = id });
        }
    }

    private void OnNegotiationNeeded(object o, GLib.SignalArgs args, int dest)
    {
        Console.WriteLine("Renegotiation needed event fired from " + id + " to " + dest);
        var webRtc = o as Element;
        Assert(webRtc != null, "not a webrtc object");

        //_client.CreateSendingChain(_webrtcbin);

        var promise = new Promise((promise) => OnOfferCreated(promise, dest)); //, webrtc.Handle, null); // webRtc.Handle, null);
        Structure structure = new Structure("struct");
        webRtc.Emit("create-offer", structure, promise);
    }

    private void OnOfferCreated(Promise promise, int dest)
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
            if (dest == -1)
            {
                Send(new WebMsg() { sdp = sdpMsg, src = id, dest = dest });
            } else {
                SendMessage(dest, new WebMsg() { sdp = sdpMsg, src = id, dest = id });
            }
        }
    }

    public void HandleSdp(SdpMsg msg, int dest)
    {
        var peer = dest == -1 ? incoming.webrtcbin : outgoingFlows[dest].webrtcbin;
        if (msg.sdp != null)
        {
            var sdp = msg.sdp;
            string sdpMessage = sdp.sdp;
            Console.WriteLine($"received sdp");
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
                                if (dest == -1)
                                {
                                    Send(new WebMsg() { sdp = sdpMsg2, dest = dest });
                                }
                                else
                                {
                                    SendMessage(dest, new WebMsg() { sdp = sdpMsg2, dest = id });
                                }
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

    private void HandleIncomingSdp(SdpMsg msg, int dest)
    {
        if (dest != -1)
        {
            readressSdp(dest, msg);
        }
        else
        {
            HandleSdp(msg, dest);
        }
        
    }

    protected override void OnMessage(MessageEventArgs e)
    {
        if (e.IsText)
        {
            if (e.Data == "HELLO")
            {
                if (!isStreaming)
                {
                    Console.WriteLine("Client said hello, starting pipeline");
                    isStreaming = true;
                    OnStreamingStart(id);
                    _pipeline.SetState(Gst.State.Playing);
                    if (messageLoopThread == null)
                    {
                        messageLoopThread = new Thread(() =>
                        {
                            while (true)
                            {
                                
                            }
                        });
                        messageLoopThread.IsBackground = true;
                        messageLoopThread.Start();
                    }
                } 
            }
            else
            {
                var msg = JsonConvert.DeserializeObject<WebMsg>(e.Data); 
                if (msg.sdp != null)
                {
                    //_client.InitiateAudioLink(msg.sdp);
                    HandleIncomingSdp(msg.sdp, msg.dest);
                } else if (msg.control != null)
                {
                    if (msg.control.type == ControlMsgType.REMOVE_STREAM)
                    {
                        // RemoveStream(msg.control.streamId);
                    }
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

    public void DisconnectPeer(int clientId) {
        if (!isStreaming) return;

        if (!outgoingFlows.ContainsKey(clientId)) return;

        var flow = outgoingFlows[clientId];
        flow.webrtcbin.SetState(Gst.State.Null);

        outgoingFlows.Remove(clientId);

        foreach (var track in flow.tracks)
        {
            track.encoder.SetState(Gst.State.Null);
            track.payloader.SetState(Gst.State.Null);
            track.filter.SetState(Gst.State.Null);
            track.queue.SetState(Gst.State.Null);

            _pipeline.Remove(track.encoder, track.payloader, track.filter, track.queue);
        }

        _pipeline.Remove(flow.webrtcbin);
    }

    private void RemoveOutgoingPeers()
    {
        if (!isStreaming) return;

        isStreaming = false;
        
        foreach (var (clientId, flow) in outgoingFlows)
        {
            flow.webrtcbin.SetState(Gst.State.Null);
            foreach (var track in flow.tracks)
            {
                track.encoder.SetState(Gst.State.Null);
                track.payloader.SetState(Gst.State.Null);
                track.filter.SetState(Gst.State.Null);
                track.queue.SetState(Gst.State.Null);

                _pipeline.Remove(track.encoder, track.payloader, track.filter, track.queue);
            }

            SendMessage(clientId, new WebMsg() { control = new ControlMsg() { type = ControlMsgType.REMOVE_PEER }, dest = id });
        }

        incoming.webrtcbin.SetState(Gst.State.Null);
        foreach (var track in incoming.tracks)
        {
            track.queue.SetState(Gst.State.Null);
            track.converter.SetState(Gst.State.Null);
            track.decodebin.SetState(Gst.State.Null);
            track.tee.SetState(Gst.State.Null);
            track.fakesink.SetState(Gst.State.Null);
        }
        
        _pipeline.Remove();
        _pipeline.Dispose();

        outgoingFlows = [];
    }
    
    protected override void OnClose(CloseEventArgs e)
    {
        Console.WriteLine("Closing socket");
        RemoveOutgoingPeers();
        OnSocketClosed();
        // messageLoopThread!.Interrupt();
    }

    private bool messageLoop(Bus bus, Message msg)
    {
        string debug;
        GLib.GException exc;
        switch (msg.Type)
        {
            case MessageType.Error:
                msg.ParseError(out exc, out debug);
                Console.WriteLine("Error received from element {0}: {1}", msg.Src.Name, exc.Message);
                Console.WriteLine("Error Debugging information: {0}", debug != null ? debug : "none");
                break;
            case MessageType.Warning:
                msg.ParseError(out exc, out debug);
                Console.WriteLine("Warning received from element {0}: {1}", msg.Src.Name, exc.Message);
                Console.WriteLine("Warning Debugging information: {0}", debug != null ? debug : "none");
                break;
            case MessageType.Eos:
                Console.WriteLine("End-Of-Stream reached.\n");
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
            case MessageType.Latency:
                _pipeline.RecalculateLatency();
                break;
            default:
                // We should not reach here because we only asked for ERRORs, EOS and STATE_CHANGED
                // Console.WriteLine("Unexpected message received.");
                break;
        }

        return true;
    }
}