using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using StarNet.Packets;
using StarNet.Database;
using NHibernate.Linq;
using System.Linq;
using StarNet.ClientHandlers;

namespace StarNet
{
    public class StarNetNode
    {
        public const int NetworkPort = 21024;
        public const int ClientBufferLength = 1024;
        public const int ProtocolVersion = 636;

        public SharedDatabase Database { get; set; }
        public TcpListener Listener { get; set; }
        public UdpClient NetworkClient { get; set; }
        public List<StarboundClient> Clients { get; set; }
        public ServerPool Servers { get; set; }
        public RemoteNode Siblings { get; set; }
        public LocalSettings Settings { get; set; }

        public StarNetNode(SharedDatabase database, LocalSettings settings, IPEndPoint endpoint)
        {
            Settings = settings;
            Database = database;
            Listener = new TcpListener(endpoint);
            Clients = new List<StarboundClient>();
            NetworkClient = new UdpClient(new IPEndPoint(IPAddress.Any, NetworkPort));
            RegisterHandlers();
        }

        private void RegisterHandlers()
        {
            LoginHandlers.Register();
        }

        public void Start()
        {
            Listener.Start();
            Listener.BeginAcceptSocket(AcceptClient, null);
            NetworkClient.BeginReceive(NetworkMessageReceived, null);
            Console.WriteLine("Listening on " + Listener.LocalEndpoint);
            // TODO: Log into the network
        }

        private void AcceptClient(IAsyncResult result)
        {
            var socket = Listener.EndAcceptSocket(result);
            Console.WriteLine("New connection from {0}", socket.RemoteEndPoint);
            var client = new StarboundClient(socket);
            Clients.Add(client);
            client.PacketQueue.Enqueue(new ProtocolVersionPacket(ProtocolVersion));
            client.FlushPackets();
            client.Socket.BeginReceive(client.PacketReader.NetworkBuffer, 0, client.PacketReader.NetworkBuffer.Length,
                SocketFlags.None, ClientDataReceived, client);
        }

        private void ClientDataReceived(IAsyncResult result)
        {
            var client = (StarboundClient)result.AsyncState;
            var length = client.Socket.EndReceive(result);
            var packets = client.PacketReader.UpdateBuffer(length);
            if (packets != null && packets.Length > 0)
            {
                foreach (var packet in packets)
                    PacketReader.HandlePacket(this, client, packet);
            }
            client.Socket.BeginReceive(client.PacketReader.NetworkBuffer, 0, client.PacketReader.NetworkBuffer.Length,
                SocketFlags.None, ClientDataReceived, client);
        }

        private void NetworkMessageReceived(IAsyncResult result)
        {
            IPEndPoint endPoint = default(IPEndPoint);
            var message = NetworkClient.EndReceive(result, ref endPoint);
            Console.WriteLine("Got a network message, which is weird, because we don't have a network yet. Length: " + message.Length);
        }
    }
}

