using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Steem;

// Languette au bord droit de Steam (simplifiée le 05/11) : un seul clic, le chevron épingle / détache le volet
// (Steam se réarrange) ; bonhomme de Steam en bas (vert clignotant sur nouveau message).
// Fenêtre à transparence par pixel (UpdateLayeredWindow) : coins arrondis à gauche seulement,
// angles droits côté liste, et son ombre dessinée à la main (l'ombre DWM arrondirait les quatre coins).
sealed class Poignee : Form
{
    internal static Color Fond = Color.FromArgb(0x35, 0x38, 0x3c);
    static readonly Color FondSurvol = Color.FromArgb(0x2a, 0x47, 0x5e),
                          Trait = Color.FromArgb(0xc7, 0xd5, 0xe0), Accent = Color.FromArgb(0x66, 0xc0, 0xf4),
                          Eteint = Color.FromArgb(0x5c, 0x6b, 0x7a), Message = Color.FromArgb(0xa4, 0xd0, 0x07),
                          EnLigne = Color.FromArgb(0x6d, 0xcf, 0xf6), Gris = Color.FromArgb(0x89, 0x89, 0x89);

    bool deplie, epingle, absent, nonLu, phase;
    int zone; // 0 dehors, 1 sur la languette
    Size corps; // partie visible, sans l'ombre
    readonly ToolTip bulle = new();
    readonly System.Windows.Forms.Timer clignotement = new() { Interval = 500 };

    public event Action ClicEpingle;

    public bool NonLu
    {
        get => nonLu;
        set
        {
            if (nonLu == value) return;
            nonLu = value; phase = true;
            clignotement.Enabled = value;
            Infobulle(); Rendre();
        }
    }

    public bool Deplie { get => deplie; set { if (deplie != value) { deplie = value; Infobulle(); Rendre(); } } }
    public bool Epingle { get => epingle; set { if (epingle != value) { epingle = value; Infobulle(); Rendre(); } } }
    // Statut Steam (StatutSteam) : couleur du bonhomme, œil barré si invisible.
    int statut = StatutSteam.EnLigne;
    public int Statut { get => statut; set { if (statut != value) { statut = value; Rendre(); } } }
    // Gouttière pleine hauteur (épinglé) : rectangle sans coins arrondis ni ombre.
    bool gouttiere;
    public bool Gouttiere { get => gouttiere; set { if (gouttiere != value) { gouttiere = value; Rendre(); } } }
    public bool Absent { get => absent; set { if (absent != value) { absent = value; Infobulle(); Rendre(); } } }

    public Poignee()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "Steem";
        clignotement.Tick += (_, _) => { phase = !phase; Rendre(); };
        Infobulle();
    }

    float K => DeviceDpi / 96f;
    float Ecart => 43.6f * K;
    // Ombre diffuse portée vers la gauche et le bas ; valeurs à 100 %, réglables (Reglages.Ombre*).
    internal static float OmbreTaille = 4.5f, OmbreDx = 2, OmbreDy = 1.5f;
    internal static int OmbreOpacite = 6;
    internal static bool StatutSiOuvert; // Reglages.StatutSiOuvert
    int Ombre => (int)Math.Ceiling((OmbreTaille + Math.Max(OmbreDx, OmbreDy)) * K) + 1; // marge de la fenêtre
    Rectangle dernierVisuel;
    public void Replacer() { if (!dernierVisuel.IsEmpty) Placer(dernierVisuel); }

    // Zone visée, en coordonnées de la fenêtre (ombre comprise) ; 0 hors de la partie visible.
    int Zone(Point p)
    {
        int m = Ombre;
        if (p.X < m || p.Y < m || p.Y >= m + corps.Height) return 0;
        return 1;
    }

    void Infobulle()
    {
        string texte = absent ? "Liste de contacts fermée · clic : l'ouvrir"
            : epingle ? "Détacher le volet" : "Épingler le volet (Steam se réarrange)";
        if (nonLu) texte = "Nouveau message · " + texte;
        bulle.SetToolTip(this, texte);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80 | 0x80000; return cp; } // NOACTIVATE | TOOLWINDOW | LAYERED
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = 3; return; } // WM_MOUSEACTIVATE → MA_NOACTIVATE
        base.WndProc(ref m);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    // Place la partie visible sur « visuel » (écran) ; la fenêtre déborde de l'ombre à gauche, en haut et en bas.
    public void Placer(Rectangle visuel)
    {
        dernierVisuel = visuel;
        int m = Ombre;
        corps = visuel.Size;
        origine = new Point(visuel.X - m, visuel.Y - m);
        // Position, taille et image en un seul appel : déplacer puis redessiner à part saccade.
        Rendre();
        if (!Native.IsWindowVisible(Handle)) Native.ShowWindow(Handle, 4); // SW_SHOWNOACTIVATE
    }

    Point? origine;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int z = Zone(e.Location);
        if (z != zone) { zone = z; Infobulle(); Rendre(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { zone = 0; Rendre(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int z = Zone(e.Location);
        if (e.Button == MouseButtons.Left && z != 0)
        {
            ClicEpingle?.Invoke();
            Infobulle();
        }
        base.OnMouseClick(e);
    }

    [StructLayout(LayoutKind.Sequential)] struct Taille { public int L, H; }
    [StructLayout(LayoutKind.Sequential)] struct Point32 { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Fusion { public byte Op, Drapeaux, Alpha, Format; }
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dcEcran, IntPtr pos, ref Taille t, IntPtr dcSource, ref Point32 src, int cle, ref Fusion f, int drapeaux);
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dcEcran, ref Point32 pos, ref Taille t, IntPtr dcSource, ref Point32 src, int cle, ref Fusion f, int drapeaux);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

    // Redessine et pousse l'image (alpha par pixel) dans la fenêtre.
    public void Rendre()
    {
        if (!IsHandleCreated || corps.IsEmpty) return;
        using var image = Image(corps);
        IntPtr ecran = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(ecran), hb = image.GetHbitmap(Color.FromArgb(0)), ancien = SelectObject(mem, hb);
        var t = new Taille { L = image.Width, H = image.Height };
        var src = new Point32();
        var f = new Fusion { Op = 0, Drapeaux = 0, Alpha = 255, Format = 1 }; // AC_SRC_ALPHA
        if (origine is Point o)
        {
            var pos = new Point32 { X = o.X, Y = o.Y };
            UpdateLayeredWindow(Handle, ecran, ref pos, ref t, mem, ref src, 0, ref f, 2); // ULW_ALPHA
        }
        else UpdateLayeredWindow(Handle, ecran, IntPtr.Zero, ref t, mem, ref src, 0, ref f, 2);
        SelectObject(mem, ancien);
        DeleteObject(hb);
        DeleteDC(mem);
        ReleaseDC(IntPtr.Zero, ecran);
    }

    // Contour de la partie visible : coins arrondis à gauche, angles droits à droite.
    static GraphicsPath Forme(RectangleF r, float rayon)
    {
        var gp = new GraphicsPath();
        if (rayon <= 0) { gp.AddRectangle(r); return gp; }
        float d = 2 * rayon;
        gp.AddLine(r.Right, r.Y, r.X + rayon, r.Y);
        gp.AddArc(r.X, r.Y, d, d, 270, -90);
        gp.AddArc(r.X, r.Bottom - d, d, d, 180, -90);
        gp.AddLine(r.X + rayon, r.Bottom, r.Right, r.Bottom);
        gp.CloseFigure();
        return gp;
    }

    // Image complète (ombre + languette) pour une partie visible de taille « c ».
    public Bitmap Image(Size c)
    {
        float k = K;
        int m = Ombre;
        var b = new Bitmap(c.Width + m, c.Height + 2 * m, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float rayon = gouttiere ? 0 : 6 * k;
        var r = new RectangleF(m, m, c.Width, c.Height);

        // Ombre diffuse décalée vers la gauche et le bas : rien au-dessus de la languette.
        int couches = gouttiere ? 0 : (int)Math.Round(OmbreTaille * k);
        float dx = OmbreDx * k, dy = OmbreDy * k;
        for (int i = couches; i >= 1; i--)
        {
            using var gp = Forme(new RectangleF(m - dx - i, m + dy, c.Width + dx + i, c.Height + i), rayon + i);
            using var bo = new SolidBrush(Color.FromArgb(OmbreOpacite, 0, 0, 0));
            g.FillPath(bo, gp);
        }

        using var forme = Forme(r, rayon);
        // Gouttière : la couleur de la languette assombrie de moitié (#35383c → #1b1c1e).
        var fond = gouttiere ? Color.FromArgb(Fond.R / 2, Fond.G / 2, Fond.B / 2) : Fond;
        using (var bf = new SolidBrush(fond)) g.FillPath(bf, forme);

        g.TranslateTransform(m, m);
        var etat = g.Save();
        using (var clip = Forme(new RectangleF(0, 0, c.Width, c.Height), rayon)) g.SetClip(clip);
        Dessiner(g, c.Width, c.Height, k);
        g.Restore(etat);
        return b;
    }

    void Dessiner(Graphics g, int w, int h, float k)
    {
        float cx = w / 2f, cy = h / 2f, d = 4.2f * k, s = deplie ? -1 : 1;

        // Survol : toute la languette s'éclaire.
        if (zone != 0) using (var b = new SolidBrush(FondSurvol)) g.FillRectangle(b, 0, 0, w, h);

        // Option « Statut Steam volet ouvert » coupée : bonhomme en haut, chevron toujours au milieu.
        // Activée : icône et chevron groupés au milieu, l'icône au-dessus ; sans icône, le chevron seul est centré.
        bool icone = !(deplie && !StatutSiOuvert && !nonLu);
        float yIcone = StatutSiOuvert ? cy - 11 * k : cy - Ecart;
        float yChevron = StatutSiOuvert && icone ? cy + 9 * k : cy;
        float cyIcone = yIcone;
        cy = yChevron;

        using (var p = new Pen(absent ? Eteint : epingle || deplie ? Accent : Trait, 2 * k)
            { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            g.DrawLines(p, new PointF[] { new(cx + s * d / 2, cy - 1.6f * d), new(cx - s * d / 2, cy), new(cx + s * d / 2, cy + 1.6f * d) });

        // Bonhomme de Steam (au-dessus du chevron), couleur du statut Steam (bleu en ligne, orange absent, rouge ne pas déranger, gris hors ligne),
        // œil barré si invisible ; vert clignotant sur nouveau message.
        var couleur = nonLu && phase ? Message : statut switch
        {
            StatutSteam.EnLigne => EnLigne,
            StatutSteam.Absent => Color.FromArgb(0xf0, 0xa8, 0x30),
            StatutSteam.NePasDeranger => Color.FromArgb(0xe5, 0x48, 0x3f),
            StatutSteam.Invisible => Trait,
            _ => Gris,
        };
        // Volet ouvert (languette dépliée ou gouttière) : pas d'icône de statut, sauf option du panneau ;
        // le bonhomme vert d'un nouveau message reste.
        if (!icone) return;
        using (var v = new SolidBrush(couleur))
        {
            if (statut == StatutSteam.Invisible && !(nonLu && phase)) OeilBarre(g, v, cx, cyIcone, 14 * k);
            else using (var forme = Bonhomme(cx, cyIcone, 13 * k)) g.FillPath(v, forme);
        }
    }

    // Œil barré (glyphe « Hide » de Segoe Fluent Icons / MDL2), centré sur son encre.
    static void OeilBarre(Graphics g, Brush b, float cx, float cy, float taille)
    {
        var police = FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        using var famille = new FontFamily(police);
        using var gp = new GraphicsPath();
        gp.AddString("", famille, 0, taille, PointF.Empty, StringFormat.GenericTypographic);
        var bo = gp.GetBounds();
        using var mt = new Matrix();
        mt.Translate(cx - bo.X - bo.Width / 2, cy - bo.Y - bo.Height / 2);
        gp.Transform(mt);
        g.FillPath(b, gp);
    }

    // Buste (tête + épaules) dans une boîte de 20 unités, tête détachée des épaules.
    static GraphicsPath Bonhomme(float cx, float cy, float taille)
    {
        var gp = new GraphicsPath(FillMode.Winding);
        float u = taille / 20f, x0 = cx - taille / 2, y0 = cy - taille / 2;
        gp.AddEllipse(x0 + 6 * u, y0 + 0.6f * u, 8 * u, 8 * u);
        float g = x0 + 3.5f * u, d = x0 + 16.5f * u, haut = y0 + 10.5f * u, bas = y0 + 20 * u, r = 4.5f * u;
        gp.StartFigure();
        gp.AddArc(g, haut, 2 * r, 2 * r, 180, 90);
        gp.AddArc(d - 2 * r, haut, 2 * r, 2 * r, 270, 90);
        gp.AddLine(d, haut + r, d, bas); gp.AddLine(d, bas, g, bas);
        gp.CloseFigure();
        return gp;
    }
}
