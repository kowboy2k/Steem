namespace Steem;

// Recouvre les boutons Réduire / Agrandir / Fermer que Steam dessine dans sa liste de contacts
// (ils font partie de la page, pas du cadre Windows) et avale leurs clics.
sealed class Cache : Form
{
    public Cache()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(0x1c, 0x23, 0x2c);
        Text = "Steem";
    }

    protected override bool ShowWithoutActivation => true;

    // Coin haut droit arrondi comme celui de la liste (Windows 11), pour ne pas déborder du contour.
    protected override void OnSizeChanged(EventArgs e)
    {
        float r = 7 * DeviceDpi / 96f;
        using var gp = new System.Drawing.Drawing2D.GraphicsPath();
        gp.AddLine(0, 0, Width - r, 0);
        gp.AddArc(Width - 2 * r, 0, 2 * r, 2 * r, 270, 90);
        gp.AddLine(Width, r, Width, Height);
        gp.AddLine(Width, Height, 0, Height);
        gp.CloseFigure();
        Region = new Region(gp);
        base.OnSizeChanged(e);
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80; return cp; } // NOACTIVATE | TOOLWINDOW
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = 3; return; } // WM_MOUSEACTIVATE → MA_NOACTIVATE
        base.WndProc(ref m);
    }

    // Couleur prise juste à gauche des boutons, pour suivre le thème de Steam.
    public void Accorder(IntPtr contacts, float k)
    {
        var r = Native.Rect(contacts);
        if (r.Width <= 0 || r.Height <= 0) return;
        try
        {
            using var b = new Bitmap(r.Width, Math.Min(r.Height, (int)(60 * k)));
            using (var g = Graphics.FromImage(b))
            {
                var dc = g.GetHdc();
                Native.PrintWindow(contacts, dc, 2);
                g.ReleaseHdc(dc);
            }
            int x = Math.Max(0, r.Width - (int)(120 * k)), y = Math.Min(b.Height - 1, (int)(20 * k));
            var c = b.GetPixel(x, y);
            if (c.A == 255 && c.R + c.G + c.B > 0) BackColor = c;
        }
        catch { }
    }
}
