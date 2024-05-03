using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace GstreamerTest1Server
{
    internal class Program
    {
        private enum PeerStatus
        {
            Init,
            None,
            Session,
            Room
        }

        private class Peer : WebSocketBehavior
        {
            private readonly Program _app;

            public PeerStatus Status { get; private set; } = PeerStatus.Init;

            public uint Id { get; private set; }

            public Peer(Program app)
            {
                _app = app;
            }

            protected override void OnMessage(MessageEventArgs e)
            {
                Console.WriteLine("Received message from " + Id + ": " + e.Data);
                switch (this.Status)
                {
                    case PeerStatus.Init:
                        {
                            if (e.Data.StartsWith("HELLO"))
                            {
                                var parts = e.Data.Split(' ');
                                var id = uint.Parse(parts.Last());
                                this.Id = id;
                                _app._peers.Add(id, this);

                                this.Status = PeerStatus.None;
                                this.Send("HELLO");
                            }
                        }
                        break;
                    case PeerStatus.None:
                        {
                            if (e.Data.StartsWith("SESSION"))
                            {
                                var parts = e.Data.Split(' ');
                                var callee_id = uint.Parse(parts.Last());
                                if (_app._peers.TryGetValue(callee_id, out var other))
                                {
                                    if (other.Status != PeerStatus.None)
                                        throw new NotImplementedException("peer is busy");

                                    // other.Send("OFFER_REQUEST");
                                    
                                    // this.Send("SESSION_OK");
                                    this.Send("OFFER_REQUEST");
                                    //wsc = self.peers[callee_id][0]
                                    //print('Session from {!r} ({!r}) to {!r} ({!r})'
                                    //      ''.format(uid, raddr, callee_id, wsc.remote_address))
                                    //# Register session

                                    this.Status = PeerStatus.Session;
                                    other.Status = PeerStatus.Session;

                                    _app._sessions[this.Id] = other.Id;
                                    _app._sessions[other.Id] = this.Id;
                                }
                                else
                                {
                                    throw new NotImplementedException("no such user");
                                }
                            }
                            else if (e.Data.StartsWith("ROOM"))
                            {
                                throw new NotImplementedException("");
                            }
                        }
                        break;
                    case PeerStatus.Session:
                        {
                            var otherId = _app._sessions[this.Id];
                            _app._peers[otherId].Send(e.Data);
                        }
                        break;
                    case PeerStatus.Room:
                        {
                            throw new NotImplementedException("");
                        }
                        break;
                    default:
                        throw new NotImplementedException(""); ;
                }


                base.OnMessage(e);
            }

            protected override void OnClose(CloseEventArgs e)
            {
                _app._peers.Remove(this.Id);
            }
        }

        private readonly LinkedList<Peer> _allPeers = new LinkedList<Peer>();
        private readonly Dictionary<uint, Peer> _peers = new Dictionary<uint, Peer>();
        private readonly Dictionary<uint, uint> _sessions = new Dictionary<uint, uint>();
        private readonly Dictionary<uint, List<uint>> _rooms = new Dictionary<uint, List<uint>>();
        private readonly WebSocketServer _server;

        public Program(IPAddress localHost, ushort localPort)
        {
            _server = new WebSocketServer(localHost, localPort);
        }

        //############### Handler functions ###############
        
        private void Run()
        {
            _server.AddWebSocketService("/", () => {
                var peer = new Peer(this);
                _allPeers.AddLast(peer);
                return peer;
            });
            _server.Start();
        }

        private void Stop()
        {

        }

        private static void Main(string[] args)
        {
            var app = new Program(IPAddress.Any, 8443);
            app.Run();

            while (Console.ReadKey().Key != ConsoleKey.Q) ;
        }
    }
}
