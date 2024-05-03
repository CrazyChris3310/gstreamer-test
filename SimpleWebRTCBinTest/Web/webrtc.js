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
var default_constraints = {video: true, audio: true};

var connect_attempts = 0;
var peer_connection;
var send_channel;
var sck;
// Promise for local stream after constraints are approved by the user
var local_stream_promise;

var localMediaStream;
var senders = [];
let isConnected = false;


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
                peer_connection.setRemoteDescription(msg.sdp.sdp).then(() => {
                    if (msg.sdp.sdp.type === "offer") {
                        peer_connection.createAnswer().then(answer => {
                            peer_connection.setLocalDescription(answer);
                            return answer;
                        }).then(answer => {
                            console.log("Sending answer: " + JSON.stringify(answer));
                            sck.send(JSON.stringify({ sdp: { 'sdp': answer } }));
                        }).catch(console.warn)
                    }
                })
            } else if (msg.sdp.ice != null) {
                var candidate = new RTCIceCandidate(msg.sdp.ice);
                console.log("remote ice candidate received: " + msg.sdp.ice);
                peer_connection.addIceCandidate(candidate)
                    .then(() => console.log("Remote candidate added"))
                    .catch(console.error);
            } else {
                handleIncomingError("Unknown incoming JSON: " + msg);
            }
        }
    };
    sck.onclose = function (event) {
        console.warn('Disconnected from server');

        if (peer_connection) {
            peer_connection.close();
            peer_connection = null;
        }

        sck = null;
    }

    peer_connection = new RTCPeerConnection(rtc_configuration);

    peer_connection.onicecandidate = event => {
        if (event.candidate != null) {
            sck.send(JSON.stringify({ sdp: { ice: event.candidate } }));
        }
    };

    peer_connection.onconnectionstatechange = ev => {
        console.log("Connection state changed to " + peer_connection.connectionState);
        if (peer_connection.connectionState === 'connected') {
            isConnected = true;
        }
    }

    peer_connection.oniceconnectionstatechange = event => {
        console.log("Ise state changed: " + peer_connection.iceConnectionState);
    }

    peer_connection.onicecandidateerror = event =>
        console.error(JSON.stringify(event));

    // peer_connection.onnegotiationneeded = () => {
    //     console.warn("Negotiation needed");
    //     if (isConnected) {
    //         peer_connection.createOffer().then(offer => {
    //             peer_connection.setLocalDescription(offer);
    //             return offer;
    //         }).then(offer => {
    //             sck.send(JSON.stringify({ sdp: { 'sdp': offer } }));
    //         })
    //     }
    // }
    
    navigator.mediaDevices.getUserMedia({ video: true })
        .then(stream => {
            localMediaStream = stream
            document.querySelector("#local_stream").srcObject = localMediaStream;
            localMediaStream.getTracks().forEach(track => {
                senders.push(peer_connection.addTrack(track, localMediaStream));
            });
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

function handleIncomingError(error) {
    console.error("ERROR: " + error);
}
function doConnect() {
    sck.send('HELLO');
    peer_connection.createOffer().then(offer => {
        peer_connection.setLocalDescription(offer);
        return offer;
    }).then(offer => {
        sck.send(JSON.stringify({ sdp: { 'sdp': offer } }));
    })
}
let isScreenShared = false;

function stopScreenShare() {
    document.querySelector("#screen_share").srcObject = null;
    senders.find(sender => sender.track.kind === 'video').replaceTrack(localMediaStream.getTracks().find(track => track.kind === 'video'));
}
function shareScreen() {
    navigator.mediaDevices.getDisplayMedia({ cursor: true }).then(stream => {
        document.querySelector("#screen_share").srcObject = stream;
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


