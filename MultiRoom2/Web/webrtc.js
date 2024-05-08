/* vim: set sts=4 sw=4 et :
 *
 * Demo Javascript app for negotiating and streaming a sendrecv webrtc stream
 * with a GStreamer app. Runs only in passive mode, i.e., responds to offers
 * with answers, exchanges ICE candidates, and streams.
 *
 * Author: Nirbheek Chauhan <nirbheek@centricular.com>
 */

let rtc_configuration = {iceServers: [{urls: "stun:stun.services.mozilla.com"},
                                      {urls: "stun:stun.l.google.com:19302"}]};

let default_constraints = {
    video: {
        width: 1280,
        height: 720
    },
    audio: true
};

let peer_connection;
let sck;

let localMediaStream;
let isConnected = false;

let incoming = {}
let videoBlocks = {};

function showPicture(deviceId) {
    console.log("Changed");
    navigator.mediaDevices.getUserMedia({ video: { width: 1280, height: 720, deviceId: deviceId }, audio: false })
        .then(stream => {
            localMediaStream = stream;
            let video = document.querySelector("#local_stream");
            video.srcObject = localMediaStream;
            console.log(stream.getTracks()[0].label);
        });

}

function addNewStream() {
    let selector = document.querySelector("#video-selector");
    let deviceId = selector.value;
    
    navigator.mediaDevices.getUserMedia({video: { width: 1280, height: 720, deviceId: deviceId }})
        .then(stream => {
            let localPlane = document.querySelector("#local-plane");
            let video = document.createElement("video");
            video.muted = true;
            video.srcObject = stream;
            video.playsInline = true;
            video.autoplay = true;
            video.classList.add("videobox")
            localPlane.append(video);
            peer_connection.addTrack(stream.getTracks()[0], stream);
        })
}

function websocketServerConnect() {  
    navigator.mediaDevices.enumerateDevices().then((devices) => {
        console.log(devices);
        let microSelect = document.querySelector("#microphone-selector");
        let videoSelect = document.querySelector("#video-selector");
        devices.forEach(device => {
            let option = document.createElement("option");
            option.value = device.deviceId;
            option.innerHTML = device.label;
            if (device.kind === 'audioinput') {
                microSelect.append(option);
            } else if (device.kind === 'videoinput') {
                videoSelect.append(option);
            }
        });
    })
    
    sck = new WebSocket(makeWsUrl('/sck'));
    sck.onopen = (event) => {
        console.log("Connected to server");
    };
    sck.onerror = function (event) {
        console.error("web socket error", event);
    };
    sck.onmessage = function (event) {
        console.log('received ' + event.data);
        let msg = JSON.parse(event.data);
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
                let candidate = new RTCIceCandidate(msg.sdp.ice);
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
                videoBlocks[msg.dest].block.remove();
                delete videoElements[msg.dest];
            }
        }
    };
    sck.onclose = function () {
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
                peer_connection.addTrack(track, localMediaStream);
            });
        });
    
    function makeWsUrl(ep) {
        let protocol = 'ws';
        if (window.location.protocol.startsWith('https'))
            protocol = 'wss';

        let host = window.location.hostname;
        let port = window.location.port || (protocol === 'wss' ? 443 : 80);

        return protocol + '://' + host + ':' + port + (ep.startsWith('/') ? ep : ('/' + ep));
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
        if (peer_connection.connectionState === 'connected') {
            isConnected = true;
        }
    }

    peer.oniceconnectionstatechange = event => {
        console.log("Ise state of " + dest + " changed: " + peer_connection.iceConnectionState);
    }

    peer.onicecandidateerror = event =>
        console.error(JSON.stringify(event));
    
    peer.ontrack = (event) => {
        let element = getOrCreateBlockElement(dest, event.streams[0].id);
        
        if (element.srcObject !== event.streams[0]) {
            console.log('Incoming stream');
            element.srcObject = event.streams[0];
        }
    }
    
    peer.onnegotiationneeded = () => {
        if (isConnected) {
            console.warn("Need renegotiation");
            peer_connection.createOffer().then(offer => {
                peer_connection.setLocalDescription(offer);
                return offer;
            }).then(offer => {
                sck.send(JSON.stringify({ sdp: { 'sdp': offer }, dest: -1 }));
            })
        }
    }
    
    return peer;
}

function getOrCreateVideoBlock(dest) {
    let block = videoBlocks[dest];
    if (block == null) {
        block = document.createElement("div");
        block.classList.add("block");
        document.querySelector("#remote-streams").append(block);
        videoBlocks[dest] = { userId: dest, block: block, elements: {} }
    }
    return block;
}

function getOrCreateBlockElement(dest, streamId) {
    let block = getOrCreateVideoBlock(dest);
    let element = block.elements[streamId]
    if (element == null) {
        element = document.createElement("video");
        element.playsInline = true;
        element.autoplay = true;
        element.classList.add("videobox")
        block.block.append(element);
        block.elements[streamId] = { streamId: streamId, element: element };
    }
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
    document.querySelector("#local_stream").srcObject = localMediaStream;
    // senders.find(sender => sender.track.kind === 'video').replaceTrack(localMediaStream.getTracks().find(track => track.kind === 'video'));
}
function shareScreen() {
    navigator.mediaDevices.getDisplayMedia({ cursor: true }).then(stream => {
        let screenTrack = stream.getTracks()[0];

        let localPlane = document.querySelector("#local-plane");
        let video = document.createElement("video");
        video.muted = true;
        video.srcObject = stream;
        video.playsInline = true;
        video.autoplay = true;
        video.classList.add("videobox")
        localPlane.append(video);
        peer_connection.addTrack(screenTrack, stream);
    })
}

window.onload = websocketServerConnect;


