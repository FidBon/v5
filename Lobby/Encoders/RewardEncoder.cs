using GameLogic.Rewards;
using Protocol;

namespace Lobby.Encoders;

public static class ServerCommands
{
    public const int ChangeAvatarName = 201;
    public const int DiamondsAdded = 202;
    public const int GiveDeliveryItems = 203;
    public const int DayChanged = 204;
    public const int AddNotification = 206;
}

public static class RewardEncoder
{
    public static ByteStreamWriter BuildDeliveryItems(BoxResult box)
    {
        var w = new ByteStreamWriter();
        w.WriteVInt(ServerCommands.GiveDeliveryItems);
        w.WriteVInt(box.BoxId);
        w.WriteVInt(box.Rewards.Count);
        foreach (var reward in box.Rewards)
        {
            w.WriteVInt(reward.Rarity);
            w.WriteVInt(reward.Amount);
            w.WriteDataReference(reward.DataRefHigh, reward.DataRefLow);
            w.WriteVInt((int)reward.Kind);
            w.WriteVInt(reward.Upgrade);
        }
        return w;
    }
}
