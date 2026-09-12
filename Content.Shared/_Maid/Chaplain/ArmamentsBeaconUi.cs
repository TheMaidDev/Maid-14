using Robust.Shared.Serialization;

namespace Content.Shared._Maid.Chaplain;


[Serializable, NetSerializable]
public enum SelectArmorUi
{
    Key
}

[Serializable, NetSerializable]
public class ArmorSelectedEvent : BoundUserInterfaceMessage
{
    public int SelectedIndex;

    public ArmorSelectedEvent(int index)
    {
        SelectedIndex = index;
    }
}
