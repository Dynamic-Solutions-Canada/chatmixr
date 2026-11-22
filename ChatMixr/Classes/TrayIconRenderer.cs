using System.Drawing;
using System.Drawing.Drawing2D;
using ChatMixr.Models;
using WinUIAppKit.Tray;

namespace ChatMixr.Classes;

/// <summary>What the tray icon should say at a glance.</summary>
public enum TrayStatus
{
    /// <summary>No playback device at all. A faint headset, no dot.</summary>
    NoHeadset,

    /// <summary>Every device's mix is in the middle, so nothing is turned down: a full headset, no dot.</summary>
    Waiting,

    /// <summary>A wheel or a slider turns something down on at least one device: a green dot.</summary>
    Mixing,

    /// <summary>SteelSeries Sonar is running and takes the apps' audio away from the headset: an amber dot.</summary>
    SonarConflict,
}

/// <summary>
/// The tray icon's artwork: a headset outlined in the taskbar's text colour, plus a status dot. The
/// kit's <see cref="TrayIconRasterizer"/> turns it into PNG bytes at exactly the pixel size the tray
/// uses, for <see cref="TrayIcon.Update"/>.
/// </summary>
internal static class TrayIconRenderer
{
    private static readonly Color Green = Color.FromArgb(34, 197, 94);
    private static readonly Color Amber = Color.FromArgb(245, 158, 11);

    /// <summary>The status a snapshot shows; a Sonar conflict wins over everything else.</summary>
    public static TrayStatus StatusOf(MixSnapshot snapshot)
    {
        if (snapshot.SonarRunning && snapshot.Devices.Any(d => d.HasWheel))
        {
            return TrayStatus.SonarConflict;
        }

        if (snapshot.Devices.Any(MixVisuals.IsBalancing))
        {
            return TrayStatus.Mixing;
        }

        return snapshot.Devices.Count > 0 ? TrayStatus.Waiting : TrayStatus.NoHeadset;
    }

    /// <summary>Renders the icon as PNG bytes.</summary>
    /// <param name="status">What the icon shows at a glance.</param>
    /// <param name="darkTaskbar">From <see cref="TrayIcon.TaskbarIsDark"/>; picks the outline and ring colours.</param>
    /// <param name="size">The icon's pixel size, normally <see cref="TrayIcon.IconPixelSize"/>.</param>
    public static byte[] Render(TrayStatus status, bool darkTaskbar, int size)
    {
        var ink = TrayIconRasterizer.Ink(darkTaskbar);
        var outline = Color.FromArgb(status == TrayStatus.NoHeadset ? 150 : 255, ink);

        return TrayIconRasterizer.Render(size, (g, side) =>
        {
            DrawHeadset(g, side, outline);
            if (status is TrayStatus.Mixing or TrayStatus.SonarConflict)
            {
                DrawDot(g, side, status == TrayStatus.Mixing ? Green : Amber, darkTaskbar);
            }
        });
    }

    /// <summary>A headband arc over two filled ear cups, on a canvas <paramref name="m"/> pixels square.</summary>
    private static void DrawHeadset(Graphics g, float m, Color ink)
    {
        using (var band = new Pen(ink, m * 0.09f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawArc(band, m * 0.14f, m * 0.10f, m * 0.72f, m * 0.80f, 180, 180);
            g.DrawLine(band, m * 0.14f, m * 0.50f, m * 0.14f, m * 0.62f);
            g.DrawLine(band, m * 0.86f, m * 0.50f, m * 0.86f, m * 0.62f);
        }

        using var brush = new SolidBrush(ink);
        using (var left = TrayIconRasterizer.RoundedRectangle(new RectangleF(m * 0.06f, m * 0.52f, m * 0.22f, m * 0.36f), m * 0.08f))
        {
            g.FillPath(brush, left);
        }

        using (var right = TrayIconRasterizer.RoundedRectangle(new RectangleF(m * 0.72f, m * 0.52f, m * 0.22f, m * 0.36f), m * 0.08f))
        {
            g.FillPath(brush, right);
        }
    }

    /// <summary>Status dot in the bottom-right corner, ringed in the taskbar colour so it reads on the outline.</summary>
    private static void DrawDot(Graphics g, float m, Color color, bool darkTaskbar)
    {
        float r = m * 0.20f, cx = m * 0.78f, cy = m * 0.78f;
        using (var b = new SolidBrush(TrayIconRasterizer.Surface(darkTaskbar)))
        {
            g.FillEllipse(b, cx - r - m * 0.05f, cy - r - m * 0.05f, 2 * r + m * 0.1f, 2 * r + m * 0.1f);
        }

        using (var b = new SolidBrush(color))
        {
            g.FillEllipse(b, cx - r, cy - r, 2 * r, 2 * r);
        }
    }
}
