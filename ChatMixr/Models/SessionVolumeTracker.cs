namespace ChatMixr.Models;

/// <summary>
/// Works out each app's volume from the wheel without losing the volume the user chose for it.
/// <para>
/// Windows remembers every app's own volume, per output device, even after the app or ChatMixr
/// exits. So ChatMixr never sets an absolute level: it remembers each app's level before ChatMixr
/// touched it (its <em>baseline</em>) and sets baseline × wheel factor. If the level changes to
/// something ChatMixr did not set, the user moved that app's slider in Windows' volume mixer, and the
/// baseline is worked back out from the new level so the next wheel movement keeps their choice.
/// </para>
/// <para>
/// Baselines are keyed by output device and program name, and are kept in a file by the caller
/// (<see cref="Baselines"/>), so a crash that leaves an app scaled down does not make the scaled
/// level its new baseline on the next start. A clean exit puts every app playing on a headset back to
/// its baseline (<see cref="RestoreTarget"/>) and forgets only those baselines
/// (<see cref="ForgetRestored"/>).
/// </para>
/// Not thread-safe: the mix engine calls it from its one audio thread.
/// </summary>
public sealed class SessionVolumeTracker
{
    /// <summary>Two volumes closer than this are the same; Windows rounds what it stores.</summary>
    public const float Tolerance = 0.005f;

    /// <summary>
    /// Below this factor a user change cannot be divided back into a baseline (the result would be
    /// huge or infinite), so it is left alone instead.
    /// </summary>
    public const float MinFactorForRebase = 0.02f;

    private readonly Dictionary<string, float> _baselines;
    private readonly Dictionary<string, float> _lastSet = new();
    private readonly HashSet<string> _restored = new();

    /// <param name="savedBaselines">Baselines from the previous run that did not exit cleanly, or none.</param>
    public SessionVolumeTracker(IReadOnlyDictionary<string, float>? savedBaselines = null)
    {
        _baselines = savedBaselines != null ? new Dictionary<string, float>(savedBaselines) : new();
    }

    /// <summary>The current baselines, for the caller to save when <see cref="BaselinesChanged"/> is set.</summary>
    public IReadOnlyDictionary<string, float> Baselines => _baselines;

    /// <summary>Set when a baseline was added or changed; the caller saves and clears it.</summary>
    public bool BaselinesChanged { get; set; }

    /// <summary>The key a baseline is stored under: one per output device and program.</summary>
    public static string BaselineKey(string endpointId, string programName) =>
        endpointId + "|" + ChatApps.Normalize(programName).ToLowerInvariant();

    /// <summary>
    /// Decide the volume for one app's audio session.
    /// </summary>
    /// <param name="sessionId">Windows' identifier for this session (stable while the session lives).</param>
    /// <param name="baselineKey">From <see cref="BaselineKey"/>.</param>
    /// <param name="current">The session's volume now, 0 to 1.</param>
    /// <param name="factor">The wheel's factor for this app's side of the mix, 0 to 1.</param>
    /// <returns>The volume to set, or null when the session is already right or should be left alone.</returns>
    public float? Decide(string sessionId, string baselineKey, float current, float factor)
    {
        float baseline;
        if (!_lastSet.TryGetValue(sessionId, out float last))
        {
            // First time this session is seen: an existing baseline (another session of the same
            // program, or the previous run) wins over the current level, which may already be scaled.
            if (!_baselines.TryGetValue(baselineKey, out baseline))
            {
                baseline = current;
                SetBaseline(baselineKey, baseline);
            }
        }
        else if (Math.Abs(current - last) > Tolerance)
        {
            // Someone else moved this app's volume: take it as the user's new choice.
            if (factor < MinFactorForRebase)
            {
                _lastSet[sessionId] = current;
                return null;
            }

            baseline = Math.Clamp(current / factor, 0f, 1f);
            SetBaseline(baselineKey, baseline);
        }
        else
        {
            baseline = _baselines.TryGetValue(baselineKey, out float known) ? known : current;
        }

        float target = Math.Clamp(baseline * factor, 0f, 1f);
        _lastSet[sessionId] = target;
        return Math.Abs(target - current) > Tolerance ? target : null;
    }

    /// <summary>Forget sessions that no longer exist, so their identifiers don't accumulate.</summary>
    /// <param name="liveSessionIds">Every session seen in the latest pass.</param>
    public void Prune(IReadOnlySet<string> liveSessionIds)
    {
        foreach (string id in _lastSet.Keys.Where(id => !liveSessionIds.Contains(id)).ToList())
        {
            _lastSet.Remove(id);
        }
    }

    /// <summary>
    /// The volume to put a session back to on exit, or null when ChatMixr has no baseline for it.
    /// The baseline is marked as restored, for <see cref="ForgetRestored"/>.
    /// </summary>
    public float? RestoreTarget(string baselineKey)
    {
        if (!_baselines.TryGetValue(baselineKey, out float baseline))
        {
            return null;
        }

        _restored.Add(baselineKey);
        return baseline;
    }

    /// <summary>
    /// Forget the baselines handed out by <see cref="RestoreTarget"/>, once their apps have been put
    /// back, so the next start reads those apps' levels afresh. Every other baseline stays: Windows
    /// keeps a closed app's reduced volume, and the next start needs its baseline to undo it, as it does
    /// for apps on a headset that is unplugged.
    /// </summary>
    public void ForgetRestored()
    {
        foreach (string key in _restored)
        {
            if (_baselines.Remove(key))
            {
                BaselinesChanged = true;
            }
        }

        _restored.Clear();
    }

    /// <summary>
    /// Forget the per-session state, keeping the baselines, so every live session is treated as new on
    /// the next pass. Used when the wheel or the chat list changes in a way that must re-apply to all.
    /// </summary>
    public void ForgetSessions()
    {
        _lastSet.Clear();
    }

    private void SetBaseline(string key, float value)
    {
        if (_baselines.TryGetValue(key, out float old) && Math.Abs(old - value) <= Tolerance)
        {
            return;
        }

        _baselines[key] = value;
        BaselinesChanged = true;
    }
}
