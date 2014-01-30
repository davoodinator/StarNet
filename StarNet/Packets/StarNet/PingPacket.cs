using System;
using System.IO;
using System.Net;

namespace StarNet.Packets.StarNet
{
    public class PingPacket : StarNetPacket
    {
        public override void Read(BinaryReader stream)
        {
        }

        public override void Write(BinaryWriter stream)
        {
        }

        public override MessageFlags Flags
        {
            get
            {
                return MessageFlags.ConfirmationRequired;
            }
        }
    }
}