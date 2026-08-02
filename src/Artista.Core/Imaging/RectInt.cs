namespace Artista.Core.Imaging;

/// <summary>Integer rectangle used for regions of interest and dirty tracking.</summary>
public readonly record struct RectInt(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;   // exclusive
    public int Bottom => Y + Height; // exclusive
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public long Area => IsEmpty ? 0 : (long)Width * Height;

    public static readonly RectInt Empty = new(0, 0, 0, 0);

    public static RectInt FromLTRB(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);

    public static RectInt FromPoints(int x0, int y0, int x1, int y1) =>
        FromLTRB(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));

    public bool Contains(int px, int py) => px >= X && py >= Y && px < Right && py < Bottom;

    public RectInt Intersect(RectInt other)
    {
        int l = Math.Max(Left, other.Left);
        int t = Math.Max(Top, other.Top);
        int r = Math.Min(Right, other.Right);
        int b = Math.Min(Bottom, other.Bottom);
        return r > l && b > t ? FromLTRB(l, t, r, b) : Empty;
    }

    public RectInt Union(RectInt other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        return FromLTRB(
            Math.Min(Left, other.Left), Math.Min(Top, other.Top),
            Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }

    public RectInt Inflate(int amount) =>
        new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);

    public bool IntersectsWith(RectInt other) =>
        !IsEmpty && !other.IsEmpty &&
        other.Left < Right && Left < other.Right &&
        other.Top < Bottom && Top < other.Bottom;
}
