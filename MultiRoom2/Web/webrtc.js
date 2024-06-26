/* vim: set sts=4 sw=4 et :
 *
 * Demo Javascript app for negotiating and streaming a sendrecv webrtc stream
 * with a GStreamer app. Runs only in passive mode, i.e., responds to offers
 * with answers, exchanges ICE candidates, and streams.
 *
 * Author: Nirbheek Chauhan <nirbheek@centricular.com>
 */

let commands = {
    REMOVE_PEER: 0,
    REMOVE_STREAM: 1
}

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

let localVideoBlock = {};
let isConnected = false;

let incoming = {}
let videoBlocks = {};

let username = "Default username";

function initApp() {  
    username = localStorage.getItem("username") || "Default username";
    
    navigator.mediaDevices.enumerateDevices().then((devices) => {
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
    });

    document.querySelector("#chat-input").onkeyup = (event) => {
        if (event.keyCode === 13) {
            sendMessage(event.target.value);
            event.target.value = "";
        }
    }
    
    sck = createWebSocket();
    peer_connection = createPeer(-1);
}

function createWebSocket() {
    function makeWsUrl(ep) {
        let protocol = 'ws';
        if (window.location.protocol.startsWith('https'))
            protocol = 'wss';

        let host = window.location.hostname;
        let port = window.location.port || (protocol === 'wss' ? 443 : 80);

        return protocol + '://' + host + ':' + port + (ep.startsWith('/') ? ep : ('/' + ep));
    }
    
    function removeSocket() {
        if (peer_connection) {
            peer_connection.close();
            peer_connection = null;
        }

        incoming.forEach(it => it.peer.close());
        incoming = [];

        sck = null;
    }

    let pathSegments = window.location.pathname.split("/");
    let roomId = pathSegments[pathSegments.length - 1];
    
    let socket = new WebSocket(makeWsUrl('/sck?roomId=' + roomId));
    socket.onopen = (event) => {
        console.log("Connected to server");
    };
    socket.onerror = function (event) {
        console.error("web socket error", event);
    };
    socket.onmessage = function (event) {
        console.log('received ' + event.data);
        let msg = JSON.parse(event.data);
        if (msg.chat) {
            let element = document.createElement("div");
            element.classList.add("message");
            element.innerHTML = msg.username + ": " + msg.chat.text;
            document.querySelector("#message-block").append(element);
        } else if (msg.sdp) {
            if (msg.sdp.sdp != null) {
                let connection = getOrCreatePeer(msg.dest, msg.username);
                connection.setRemoteDescription(msg.sdp.sdp).then(() => {
                    if (msg.sdp.sdp.type === "offer") {
                        connection.createAnswer().then(answer => {
                            connection.setLocalDescription(answer);
                            return answer;
                        }).then(answer => {
                            console.log("Sending answer: " + JSON.stringify({ sdp: { 'sdp': answer }, dest: msg.dest }));
                            sck.send(JSON.stringify({ sdp: { 'sdp': answer }, dest: msg.dest }));
                            getOrCreateVideoBlock(msg.dest);
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
            if (msg.control.type === commands.REMOVE_PEER) {
                incoming[msg.dest].peer.close();
                delete incoming[msg.dest];
                videoBlocks[msg.dest].block.remove();
                delete videoBlocks[msg.dest];
            } else if (msg.control.type === commands.REMOVE_STREAM) {
                removeVideoElement(msg.dest, msg.control.streamId);
            }
        }
    };
    socket.onclose = function () {
        console.warn('Disconnected from server');
        removeSocket();
    }
    
    return socket;
}

function getOrCreatePeer(dest, destName) {
    let connection;
    if (dest === -1) {
        return peer_connection;
    } else {
        connection = incoming[dest];
        if (connection == null) {
            let remotePeer = createPeer(dest);
            connection = { peer: remotePeer, username: destName };
            incoming[dest] = connection;
        }
    }
    return connection.peer;
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
        if (peer.connectionState === 'connected') {
            isConnected = true;
            document.querySelector("#chat-input").disabled = false;
        }
    }

    peer.oniceconnectionstatechange = event => {
        console.log("Ise state of " + dest + " changed: " + peer_connection.iceConnectionState);
    }

    peer.onicecandidateerror = event =>
        console.error(JSON.stringify(event));
    
    peer.ontrack = (event) => {
        let element = getOrCreateBlockElement(dest, event.streams[0].id);
        
        if (element.video.srcObject !== event.streams[0]) {
            console.log('Incoming stream');
            element.video.srcObject = event.streams[0];
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
        let blockElement = document.createElement("div");
        blockElement.classList.add("block");
        
        let nameBlock = document.createElement("div");
        nameBlock.classList.add("name");
        let label = (dest === -1 ? username : incoming[dest].username) || "Default username";
        nameBlock.innerHTML = label;
        
        let mediaBlock = document.createElement("div");
        mediaBlock.classList.add("media");

        blockElement.append(nameBlock);
        blockElement.append(mediaBlock);
        document.querySelector("#remote-streams").append(blockElement);
        
        block = { userId: dest, block: blockElement, username: username, media: mediaBlock, elements: {} }
        videoBlocks[dest] = block;
    }
    return block;
}

function getOrCreateBlockElement(dest, streamId) {
    let block = getOrCreateVideoBlock(dest);
    let element = block.elements[streamId]
    if (element == null) {
        let div = document.createElement("div");
        
        let videoElement = document.createElement("video");
        videoElement.playsInline = true;
        videoElement.autoplay = true;
        videoElement.classList.add("videobox")
        videoElement.ondblclick = () => {
            document.querySelector("#viewport").srcObject = videoElement.srcObject;
        }
        div.append(videoElement);
        if (dest === -1) {
            videoElement.muted = true;
            let btn = document.createElement("input");
            btn.type = "button";
            btn.value = "Stop";
            btn.classList.add("stopBtn");
            btn.onclick = () => {
                stopSingleStream(streamId);
            }
            div.append(btn);
        }
        
        block.media.append(div);
        element = { streamId: streamId, div: div, video: videoElement, senders: [] };
        block.elements[streamId] = element;
    }
    return element;
}

function stopSingleStream(streamId) {
    let dest = -1;
    videoBlocks[dest].elements[streamId].senders.forEach(it => {
        peer_connection.removeTrack(it);
    });
    removeVideoElement(dest, streamId);
    sck.send(JSON.stringify({ control: { type: commands.REMOVE_STREAM, streamId: streamId } }));
}

function removeVideoElement(dest, streamId) {
    if (videoBlocks[dest].elements[streamId]) {
        videoBlocks[dest].elements[streamId].div.remove();
    }
    delete videoBlocks[dest].elements[streamId];
}

function handleIncomingError(error) {
    console.error("ERROR: " + error);
}

function connect() {
    setModalState("visible");
}

function doConnect() {
    sck.send('HELLO ' + username);
    peer_connection.createOffer().then(offer => {
        peer_connection.setLocalDescription(offer);
        return offer;
    }).then(offer => {
        sck.send(JSON.stringify({ sdp: { 'sdp': offer }, dest: -1 }));
    })
}

function setModalState(state) {
    let modalWindow = document.querySelector("#select-media-window");
    modalWindow.style.visibility = state;
    
    if (state === "visible" && document.querySelector("#local-preview").srcObject == null) {
        showPreview();
    }
}

function showPreview() {
    let videoSelect = document.querySelector("#video-selector");
    
    navigator.mediaDevices.getUserMedia({video: { width: 1280, height: 720, deviceId: videoSelect.value }, audi: false})
        .then(stream => {
            document.querySelector("#local-preview").srcObject = stream;
        })
}

function startStreaming(type) {
    let promise;
    if (type === 'none') {
        getOrCreateVideoBlock(-1);
        if (!isConnected) {
            doConnect();
        }
        setModalState("hidden");
        return;
    } 
    
    if (type === 'screen') {
        promise = getScreenMedia();
    } else {
        promise = getDeviceMedia(type);
    }
    
    promise.then(stream => {
        let element = getOrCreateBlockElement(-1, stream.id);
        element.video.srcObject = stream;
        let senders = [];
        stream.getTracks().forEach(track => senders.push(peer_connection.addTrack(track, stream)));
        element.senders = senders;
        document.querySelector("#local-preview").srcObject = null;
        if (!isConnected) {
            doConnect();
        }
        setModalState("hidden");
    });
}

function getDeviceMedia(type) {
    let microSelect = document.querySelector("#microphone-selector");
    let videoSelect = document.querySelector("#video-selector");
    let options = {}
    if (type === 'video') {
        options = { video: { width: 1280, height: 720, deviceId: videoSelect.value }, audio: false };
    } else if (type === 'audio') {
        options = { video: false, audio: { deviceId: microSelect.value } };
    } else if (type === 'both') {
        options = { video: { width: 1280, height: 720, deviceId: videoSelect.value }, audio: { deviceId: microSelect.value } };
    }
    return navigator.mediaDevices.getUserMedia(options);
}

function getScreenMedia() {
    return navigator.mediaDevices.getDisplayMedia({ cursor: true });
}

function sendMessage(text) {
    if (!isConnected || text == null || text === "") return;
    sck.send(JSON.stringify({ chat: { text: text }, username: username }));
}

window.onload = initApp;

