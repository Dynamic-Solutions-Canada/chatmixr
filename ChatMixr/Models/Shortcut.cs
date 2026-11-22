namespace ChatMixr.Models;

/// <summary>Why a recorded shortcut can't be used.</summary>
public enum ShortcutProblem
{
    /// <summary>No Ctrl, Alt, Shift or Windows key: it would take a plain key from every app.</summary>
    NoModifier,

    /// <summary>Only modifier keys were pressed.</summary>
    NoKey,

    /// <summary>It is already the other shortcut.</summary>
    SameAsOther,
}

/// <summary>
/// A keyboard shortcut: modifier keys plus one key, as Windows' RegisterHotKey takes them. Stored in
/// settings and shown with the keyboard's own key names.
/// </summary>
/// <param name="Modifiers">Any of <see cref="Alt"/>, <see cref="Control"/>, <see cref="Shift"/> and <see cref="Win"/>.</param>
/// <param name="Key">The virtual-key code of the other key.</param>
public sealed record Shortcut(uint Modifiers, uint Key)
{
    /// <summary>RegisterHotKey's modifier flags.</summary>
    public const uint Alt = 0x1, Control = 0x2, Shift = 0x4, Win = 0x8;

    /// <summary>Virtual keys that are modifiers themselves (Shift, Ctrl, Alt, either Windows key) and can't be the main key.</summary>
    private static readonly HashSet<uint> ModifierKeys = [0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5];

    /// <summary>The default for moving toward chat: Ctrl+Shift and the main-row Plus (=/+) key.</summary>
    public static Shortcut DefaultTowardChat { get; } = new(Control | Shift, 0xBB);

    /// <summary>The default for moving toward game: Ctrl+Shift and the main-row Minus key.</summary>
    public static Shortcut DefaultTowardGame { get; } = new(Control | Shift, 0xBD);

    /// <summary>True when the key is a modifier on its own, which a shortcut can't end with.</summary>
    public static bool IsModifierKey(uint key) => ModifierKeys.Contains(key);

    /// <summary>
    /// Why a recorded shortcut can't be used, or null when it can: it needs a modifier, so a plain key
    /// is never taken from every app, a real key after it, and it must differ from the other shortcut.
    /// </summary>
    public static ShortcutProblem? Problem(Shortcut candidate, Shortcut? other)
    {
        if ((candidate.Modifiers & (Alt | Control | Shift | Win)) == 0)
        {
            return ShortcutProblem.NoModifier;
        }

        if (candidate.Key == 0 || IsModifierKey(candidate.Key))
        {
            return ShortcutProblem.NoKey;
        }

        return candidate == other ? ShortcutProblem.SameAsOther : null;
    }

    /// <summary>The shortcut as people read it, "Ctrl+Shift+=", in Windows' usual order.</summary>
    /// <param name="keyName">The keyboard's name for a virtual key (it depends on the layout).</param>
    public string Describe(Func<uint, string> keyName)
    {
        var parts = new List<string>();
        if ((Modifiers & Win) != 0)
        {
            parts.Add("Win");
        }

        if ((Modifiers & Control) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((Modifiers & Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((Modifiers & Shift) != 0)
        {
            parts.Add("Shift");
        }

        parts.Add(keyName(Key));
        return string.Join("+", parts);
    }
}
