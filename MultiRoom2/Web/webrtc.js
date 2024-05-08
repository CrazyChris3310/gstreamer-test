/* vim: set sts=4 sw=4 et :
 *
 * Demo Javascript app for negotiating and streaming a sendrecv webrtc stream
 * with a GStreamer app. Runs only in passive mode, i.e., responds to offers
 * with answers, exchanges ICE candidates, and streams.
 *
 * Author: Nirbheek Chauhan <nirbheek@centricular.com>
 */

// Set this to override the automatic detection in websocketServerConnect()
var ws_server;
var ws_port;
// Set this to use a specific peer id instead of a random one
var default_peer_id;
// Override with your own STUN servers if you want
var rtc_configuration = {iceServers: [{urls: "stun:stun.services.mozilla.com"},
                                      {urls: "stun:stun.l.google.com:19302"}]};
// The default constraints that will be attempted. Can be overriden by the user.
var default_constraints = {
    video: {
        width: 1280,
        height: 720
    },
    audio: true
};

var connect_attempts = 0;
var peer_connection;
var send_channel;
var sck;
// Promise for local stream after constraints are approved by the user
var local_stream_promise;

var localMediaStream;
var senders = [];
let videoElements = {};
let isConnected = false;

let incoming = {}


function websocketServerConnect() {
    // setControlsSate(STATE_DISCONNECTED);

    // connect_attempts++;
    // if (connect_attempts > 10) {
    //     addMessage("Too many connection attempts, aborting. Refresh page to try again");
    //     return;
    // }
    // addMessage("Connecting to server...");

    sck = new WebSocket(makeWsUrl('/sck'));
    sck.onopen = (event) => {
        console.log("Connected to server");
    };
    sck.onerror = function (event) {
        console.error("web socket error", event);
    };
    sck.onmessage = function (event) {
        console.log('received ' + event.data);
        var msg = JSON.parse(event.data);
        if (msg.chat) {
            console.log("in chat: " + msg.chat.text);
        } else if (msg.sdp) {
            if (msg.sdp.sdp != null) {
                let connection = getOrCreatePeer(msg.dest);
                connection.setRemoteDescription(msg.sdp.sdp).then(() => {
                    if (msg.sdp.sdp.type === "offer") {
                        connection.createAnswer().then(answer => {
                            connection.setLocalDescription(answer);
                            return answer;
                        }).then(answer => {
                            console.log("Sending answer: " + JSON.stringify({ sdp: { 'sdp': answer }, dest: msg.dest }));
                            sck.send(JSON.stringify({ sdp: { 'sdp': answer }, dest: msg.dest }));
                        }).catch(console.warn)
                    }
                })
            } else if (msg.sdp.ice != null) {
                var candidate = new RTCIceCandidate(msg.sdp.ice);
                console.log("remote ice candidate received: " + JSON.stringify(msg.sdp.ice));
                let connection = getOrCreatePeer(msg.dest);
                connection.addIceCandidate(candidate)
                    .then(() => console.log("Remote candidate added"))
                    .catch(console.error);
            } else {
                handleIncomingError("Unknown incoming JSON: " + msg);
            }
        } else if (msg.control) {
            if (msg.control === 'REMOVE_PEER') {
                incoming[msg.dest].close();
                delete incoming[msg.dest];
                videoElements[msg.dest].remove();
                delete videoElements[msg.dest];
            }
        }
    };
    sck.onclose = function (event) {
        console.warn('Disconnected from server');

        if (peer_connection) {
            peer_connection.close();
            peer_connection = null;
        }
        
        incoming.forEach(it => it.close());
        incoming = [];

        sck = null;
    }

    peer_connection = createPeer(-1);
    
    navigator.mediaDevices.getUserMedia(default_constraints)
        .then(stream => {
            localMediaStream = stream;
            let video = document.querySelector("#local_stream");
            video.srcObject = localMediaStream;
            localMediaStream.getTracks().forEach(track => {
                senders.push(peer_connection.addTrack(track, localMediaStream));
            });
            video.onloadedmetadata = () => console.log("Incoming video ratio is " + video.width + "x" + video.height);
        });
    
    function makeWsUrl(ep) {
        var protocol = 'ws';
        if (window.location.protocol.startsWith('https'))
            protocol = 'wss';

        var host = window.location.hostname;
        var port = window.location.port || (protocol == 'wss' ? 443 : 80);

        var wsUrl = protocol + '://' + host + ':' + port + (ep.startsWith('/') ? ep : ('/' + ep));
        return wsUrl;
    }
}

function getOrCreatePeer(dest) {
    let connection;
    if (dest === -1) {
        connection = peer_connection;
    } else {
        connection = incoming[dest];
        if (connection == null) {
            connection = createPeer(dest);
            incoming[dest] = connection;
        }
    }
    return connection;
}

function createPeer(dest) {
    let peer = new RTCPeerConnection(rtc_configuration);

    peer.onicecandidate = event => {
        if (event.candidate != null) {
            sck.send(JSON.stringify({ sdp: { ice: event.candidate }, dest: dest}));
        }
    };

    peer.onconnectionstatechange = ev => {
        console.log("Connection state of " + dest + " changed to " + peer_connection.connectionState);
        // if (peer_connection.connectionState === 'connected') {
        //     isConnected = true;
        // }
    }

    peer.oniceconnectionstatechange = event => {
        console.log("Ise state of " + dest + " changed: " + peer_connection.iceConnectionState);
    }

    peer.onicecandidateerror = event =>
        console.error(JSON.stringify(event));
    
    peer.ontrack += (event) => {
        let element = getOrCreateVideoElement(dest);
        if (element.srcObject !== event.streams[0]) {
            console.log('Incoming stream');
            element.srcObject = event.streams[0];
        }
    }

    let settled = false;
    const interval = setInterval(() => {
        if (incoming[dest]) {
            let remoteStream = incoming[dest].getRemoteStreams()[0];
            let element = getOrCreateVideoElement(dest);
            console.log('Incoming stream');
            element.srcObject = remoteStream
            settled = true;
        }

        if (settled) {
            clearInterval(interval);
        }
    }, 500);
    
    return peer;
}

function getOrCreateVideoElement(dest) {
    let element = videoElements[dest];
    if (element == null) {
        element = document.createElement("video");
        element.autoplay = true;
        element.playsInline = true;
        element.classList.add("videobox");
        document.querySelector("#remote-streams").append(element);
    }
    videoElements[dest] = element;
    return element;
}

function handleIncomingError(error) {
    console.error("ERROR: " + error);
}
function doConnect() {
    sck.send('HELLO');
    peer_connection.createOffer().then(offer => {
        peer_connection.setLocalDescription(offer);
        return offer;
    }).then(offer => {
        sck.send(JSON.stringify({ sdp: { 'sdp': offer }, dest: -1 }));
    })
}
let isScreenShared = false;

function stopScreenShare() {
    document.querySelector("#screen_share").srcObject = null;
    senders.find(sender => sender.track.kind === 'video').replaceTrack(localMediaStream.getTracks().find(track => track.kind === 'video'));
}
function shareScreen() {
    navigator.mediaDevices.getDisplayMedia({ cursor: true }).then(stream => {
        // document.querySelector("#screen_share").srcObject = stream;
        let screenTrack = stream.getTracks()[0];
        // peer_connection.addTrack(screenTrack);
        senders.find(sender => sender.track.kind === 'video').replaceTrack(screenTrack);
        isScreenShared = true;
        screenTrack.onended = () => {
            // peer_connection.removeTrack(screenTrack);
            console.log("Removed screen share track from peer connection");
            senders.find(sender => sender.track.kind === 'video').replaceTrack(localMediaStream.getTracks().find(track => track.kind === 'video'));
        }
    })
}

window.onload = websocketServerConnect;


