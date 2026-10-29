using System.Numerics;

namespace Content.Goobstation.UIKit._Maid.Utils;

public static class UIBox2Utils
{

    public static UIBox2 CreateSafe(float left, float top, float right, float bottom)
    {
        if (top > bottom)
            (top, bottom) = (bottom, top);

        if (left > right)
            (left, right) = (right, left);

        return new(left, top, right, bottom);
    }

    public static UIBox2 CreateSafe(Vector2 topLeft, Vector2 bottomRight)
    {
        return CreateSafe(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
    }
}
