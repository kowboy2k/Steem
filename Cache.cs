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

    // Fond copié de l'en-tête de la liste, juste à gauche des boutons : une colonne de pixels étirée sur toute la
    // largeur. L'en-tête de Steam est en dégradé (haut plus clair) : une couleur unie laissait voir la zone cachée (16/11).
    public void Accorder(IntPtr contacts, Rectangle zone, float k)
    {
        var r = Native.Rect(contacts);
        if (r.Width <= 0 || r.Height <= 0 || zone.Width <= 0 || zone.Height <= 0) return;
        try
        {
            using var b = new Bitmap(r.Width, Math.Min(r.Height, zone.Bottom - r.Top + 2));
            using (var g = Graphics.FromImage(b))
            {
                var dc = g.GetHdc();
                Native.PrintWindow(contacts, dc, 2);
                g.ReleaseHdc(dc);
            }
            int x = Math.Clamp(zone.Left - r.Left - (int)(6 * k), 0, b.Width - 1);
            var fond = new Bitmap(zone.Width, zone.Height);
            for (int y = 0; y < zone.Height; y++)
            {
                var c = b.GetPixel(x, Math.Clamp(zone.Top - r.Top + y, 0, b.Height - 1));
                if (c.A != 255 || c.R + c.G + c.B == 0) { fond.Dispose(); return; } // capture ratée : on garde l'ancien fond
                for (int i = 0; i < zone.Width; i++) fond.SetPixel(i, y, c);
            }
            BackColor = fond.GetPixel(0, zone.Height / 2);
            var avant = BackgroundImage;
            BackgroundImage = fond;
            BackgroundImageLayout = ImageLayout.None;
            avant?.Dispose();
        }
        catch { }
    }
}
