using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using StarNet.Packets;
using StarNet.Database;
using NHibernate.Linq;
using System.Linq;
using StarNet.ClientHandlers;
using System.Text;
using System.IO;
using StarNet.Packets.Starbound;
using System.Diagnostics;
using StarNet.NetworkHandlers;
using System.Threading.Tasks;
using System.Threading;

namespace StarNet
{
    public class StarNetNode
    {
        public const int ProtocolVersion = 636;

        private const int HeartbeatInterval = 200;

        public delegate void HandleNetworkMessage(BinaryReader stream, IPEndPoint source);

        public SharedDatabase Database { get; set; }
        public TcpListener Listener { get; set; }
        public List<StarboundClient> Clients { get; set; }
        public ServerPool Servers { get; set; }
        public LocalSettings Settings { get; set; }
        public InterNodeNetwork Network { get; set; }

        private object ClientLock = new object();
        private CryptoProvider CryptoProvider { get; set; }
        private uint NextClientId { get; set; }
        private Timer HeartbeatTimer { get; set; }

        public StarNetNode(SharedDatabase database, LocalSettings settings, CryptoProvider crypto, IPEndPoint endpoint)
        {
            Settings = settings;
            Database = database;
            Listener = new TcpListener(endpoint);
            Clients = new List<StarboundClient>();
            CryptoProvider = crypto;
            Network = new InterNodeNetwork(this, crypto);
            RegisterHandlers();
            NextClientId = 0;
            HeartbeatTimer = new Timer(DoHeartbeats);
        }

        private void RegisterHandlers()
        {
            LoginHandlers.Register();
            LocalHandlers.Register();
            UserHandlers.Register();
        }

        public void Start()
        {
            Network.Start();
            Listener.Start();
            Listener.BeginAcceptSocket(AcceptClient, null);
            HeartbeatTimer.Change(HeartbeatInterval, HeartbeatInterval);
            Console.WriteLine("Starbound: Listening on " + Listener.LocalEndpoint);
        }

        public void Shutdown()
        {
            int exitCode = 0;
            try
            {
                Network.Stop();
                Listener.Stop();
            }
            catch (Exception e)
            {
                exitCode = 1;
                Console.WriteLine(e);
            }
            finally
            {
                Environment.Exit(exitCode);
            }
        }

        internal void DropClient(StarboundClient client)
        {
            lock (ClientLock)
            {
                client.DropAsync();
                Clients.Remove(client);
                Console.WriteLine("Dropped client {0}", client.UUID);
            }
        }

        internal uint AllocateClientId()
        {
            return NextClientId++;
        }

        private void DoHeartbeats(object discarded)
        {
            lock (ClientLock)
            {
                foreach (var client in Clients)
                {
                    if (client.SendHeartbeats)
                    {
                        client.PacketQueue.Enqueue(new HeartbeatPacket(client.NextHeartbeatId++));
                        client.FlushPackets();
                    }
                }
            }
        }

        private void AcceptClient(IAsyncResult result)
        {
            var socket = Listener.EndAcceptSocket(result);
            Listener.BeginAcceptSocket(AcceptClient, null);
            Console.WriteLine("New connection from {0}", socket.RemoteEndPoint);
            var client = new StarboundClient(socket);
            lock (ClientLock) Clients.Add(client);
            client.PacketQueue.Enqueue(new ProtocolVersionPacket(ProtocolVersion));
            client.FlushPackets();
            client.Socket.BeginReceive(client.PacketReader.NetworkBuffer, 0, client.PacketReader.NetworkBuffer.Length,
                SocketFlags.None, ClientDataReceived, client);
        }

        private void ClientDataReceived(IAsyncResult result)
        {
            var client = (StarboundClient)result.AsyncState;
            try
            {
                var length = client.Socket.EndReceive(result);
                var packets = client.PacketReader.UpdateBuffer(length);
                if (packets != null && packets.Length > 0)
                {
                    foreach (var packet in packets)
                        PacketReader.HandlePacket(this, client, packet);
                }
                if (!client.Dropped)
                {
                    client.Socket.BeginReceive(client.PacketReader.NetworkBuffer, 0, client.PacketReader.NetworkBuffer.Length,
                        SocketFlags.None, ClientDataReceived, client);
                }
            }
            catch
            {
                // It's generally bad practice to eat all errors, but we do it here to be safe because errors caused by
                // untrusted input shouldn't crash the server.
                DropClient(client);
            }
        }
    }
}

