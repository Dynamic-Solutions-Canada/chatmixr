namespace ChatMixr.Models;

/// <summary>
/// Where a headset's ChatMix wheel sits, as the receiver reports it: the game and chat levels, each
/// 0 to 100. In the middle of the wheel both are 100; turning it toward one side lowers the other.
/// </summary>
/// <param name="Game">Level for everything that is not a chat app, 0 to 100.</param>
/// <param name="Chat">Level for chat apps, 0 to 100.</param>
public readonly record struct MixLevels(int Game, int Chat)
{
    /// <summary>The volume factor for one side of the mix.</summary>
    /// <param name="isChat">True for a chat app's audio, false for everything else.</param>
    /// <returns>A factor from 0 to 1 that scales the app's own volume.</returns>
    public float FactorFor(bool isChat) => MixCurve.Factor(isChat ? Chat : Game);
}

/// <summary>
/// How a wheel level becomes a volume factor. Hearing is closer to logarithmic than linear, so a
/// straight line makes the first half of the wheel's travel sound like almost nothing happens. Squaring
/// the level gives a more even feel. The exponent is a starting point to tune by ear, not a measured
/// value.
/// </summary>
public static class MixCurve
{
    /// <summary>The curve's exponent; 1 would be linear.</summary>
    public const double Exponent = 2.0;

    /// <summary>Turn a 0 to 100 wheel level into a 0 to 1 volume factor; out-of-range levels are clamped.</summary>
    public static float Factor(int level)
    {
        double clamped = Math.Clamp(level, 0, 100) / 100.0;
        return (float)Math.Pow(clamped, Exponent);
    }
}

/// <summary>
/// A device's slider: one position from -100 (all the way toward game) to 100 (all the way toward
/// chat), with 0 in the middle. It behaves like the wheel: in the middle both sides are at 100, and
/// moving toward one side lowers the other. Wheel devices show their wheel on the same slider.
/// </summary>
public static class SliderMix
{
    /// <summary>The slider's ends.</summary>
    public const int Min = -100, Max = 100;

    /// <summary>How close to the middle a dragged slider snaps to it, like a wheel's centre detent.</summary>
    public const int Detent = 5;

    /// <summary>
    /// A dragged position with the centre detent applied: within <see cref="Detent"/> of the middle it
    /// snaps to exactly the middle (both sides at full), so the neutral point is easy to find again.
    /// </summary>
    public static int Snap(int position) => Math.Abs(position) <= Detent ? 0 : Math.Clamp(position, Min, Max);

    /// <summary>How far one keyboard shortcut press moves the slider.</summary>
    public const int KeyStep = 10;

    /// <summary>
    /// The position after one shortcut press: <see cref="KeyStep"/> toward chat (+1) or game (-1),
    /// stopping at the middle on the way through it (the detent), and at either end.
    /// </summary>
    public static int Step(int position, int direction)
    {
        int next = Math.Clamp(position + direction * KeyStep, Min, Max);
        bool crossesMiddle = (position < 0 && next > 0) || (position > 0 && next < 0);
        return crossesMiddle ? 0 : Snap(next);
    }

    /// <summary>
    /// A move made without a pointer (arrow or Page keys, a screen reader): stops at the middle when it
    /// passes through it, like a shortcut press, but never snaps, so a key press always leaves the
    /// middle. Snapping a 10-step key press would pull it straight back.
    /// </summary>
    public static int KeyMove(int from, int to)
    {
        int next = Math.Clamp(to, Min, Max);
        bool crossesMiddle = (from < 0 && next > 0) || (from > 0 && next < 0);
        return crossesMiddle ? 0 : next;
    }

    /// <summary>The levels the slider gives at a position; out-of-range positions are clamped.</summary>
    public static MixLevels LevelsAt(int position)
    {
        int p = Math.Clamp(position, Min, Max);
        int game = 100 - Math.Max(0, p);
        int chat = 100 - Math.Max(0, -p);
        return new MixLevels(game, chat);
    }

    /// <summary>The slider position that shows a wheel's levels; the inverse of <see cref="LevelsAt"/>.</summary>
    public static int PositionOf(MixLevels levels)
    {
        if (levels.Game < 100)
        {
            return 100 - Math.Clamp(levels.Game, 0, 100);
        }

        return -(100 - Math.Clamp(levels.Chat, 0, 100));
    }
}
