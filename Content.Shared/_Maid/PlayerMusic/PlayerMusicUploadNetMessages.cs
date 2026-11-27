using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Maid.PlayerMusic;

public sealed class PlayerMusicUploadStartNetMessage : NetMessage
{
    public NetEntity Boombox;
    public string Name = string.Empty;
    public int TotalBytes;

    public override MsgGroups MsgGroup => MsgGroups.EntityEvent;

    public override int SequenceChannel => 1;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Boombox = new NetEntity(buffer.ReadInt32());
        Name = buffer.ReadString();
        TotalBytes = buffer.ReadInt32();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Boombox.Id);
        buffer.Write(Name);
        buffer.Write(TotalBytes);
    }

    public override int EstimateBufferSize()
    {
        return sizeof(int) * 2 + Name.Length * 3 + 8;
    }
}

public sealed class PlayerMusicUploadChunkNetMessage : NetMessage
{
    public int Index;
    public byte[] Data = [];

    public override MsgGroups MsgGroup => MsgGroups.EntityEvent;

    public override int SequenceChannel => 1;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Index = buffer.ReadInt32();
        var length = buffer.ReadInt32();
        Data = length > 0 ? buffer.ReadBytes(length) : [];
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Index);
        buffer.Write(Data.Length);
        buffer.Write(Data, 0, Data.Length);
    }

    public override int EstimateBufferSize()
    {
        return sizeof(int) * 2 + Data.Length;
    }
}
