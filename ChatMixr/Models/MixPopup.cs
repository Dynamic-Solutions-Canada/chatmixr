namespace ChatMixr.Models;

/// <summary>
/// When a ChatMix wheel's change shows the mix popup, and how long the popup holds. A wheel is a worn,
/// analogue part: resting between two steps, it can report a step or two either way, unprompted, which
/// would flash the popup at random. So, with the popup gone, a wheel shows it only once it is
/// <see cref="Threshold"/> steps from where the popup last left it. Once shown, smaller steps keep it
/// following, so a real turn still looks smooth, but only a further move of <see cref="Threshold"/>
/// steps keeps it up longer: a wheel that keeps flickering after a turn can't hold the popup open.
/// The mix itself always follows the wheel exactly; this decides only what is shown.
/// </summary>
public static class MixPopup
{
    /// <summary>How long the mix popup stays after the last change before fading.</summary>
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(1.5);

    /// <summary>How many steps (out of 100, on either side) a wheel must move to show the popup.</summary>
    public const int Threshold = 3;

    /// <summary>A wheel's popup state, kept from one report to the next.</summary>
    /// <param name="Anchor">Where the last move of <see cref="Threshold"/> steps ended; while the popup is up, the next one is measured from here.</param>
    /// <param name="Rest">The last levels the popup showed (or a reading that settled the wheel); once the popup is gone, moves are measured from here.</param>
    /// <param name="OpenUntil">Until when smaller steps still show, on the callers' clock.</param>
    public readonly record struct State(MixLevels Anchor, MixLevels Rest, TimeSpan OpenUntil);

    /// <summary>
    /// A reading that is not a turn (the first one, or the answer to a status request): where moves are
    /// measured from next, with nothing showing.
    /// </summary>
    public static State Settled(MixLevels levels) => new(levels, levels, TimeSpan.Zero);

    /// <summary>
    /// The wheel reported new levels.
    /// </summary>
    /// <param name="state">The wheel's popup state, or null when none is known (then it always shows).</param>
    /// <param name="levels">The levels just reported.</param>
    /// <param name="now">The current time, on any steadily increasing clock.</param>
    /// <returns>Whether to show the popup, and the state to keep for the next report.</returns>
    public static (bool Show, State Next) Turned(State? state, MixLevels levels, TimeSpan now)
    {
        if (state is not { } known)
        {
            return (true, new State(levels, levels, now + Hold));
        }

        // While the popup is up, measure from the last real move, so flicker can't keep extending it.
        // Once it's gone, measure from where it last left the wheel, the end of the turn.
        bool open = now < known.OpenUntil;
        var from = open ? known.Anchor : known.Rest;

        // A real move: show it, and let smaller steps follow for the popup's hold.
        if (Distance(from, levels) >= Threshold)
        {
            return (true, new State(levels, levels, now + Hold));
        }

        // A small step while the popup is up: keep it following, without holding it open any longer.
        if (open)
        {
            return (true, known with { Rest = levels });
        }

        // A small step with the popup gone: noise, or a nudge too small to announce.
        return (false, known);
    }

    /// <summary>How far apart two wheel readings are: the larger difference of the two sides.</summary>
    public static int Distance(MixLevels a, MixLevels b) =>
        Math.Max(Math.Abs(a.Game - b.Game), Math.Abs(a.Chat - b.Chat));
}
