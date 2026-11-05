using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Steem;

// Panneau de l'icône près de l'horloge, aux couleurs de Steam : épinglage, délais, démarrage avec Windows, quitter.
// Tout est dessiné à la main, en pixels logiques (×K à l'écran).
sealed class Panneau : Form
{
    static readonly Color Fond = Color.FromArgb(0x17, 0x1d, 0x25), Survol = Color.FromArgb(0x23, 0x30, 0x40),
                          Piste = Color.FromArgb(0x2a, 0x47, 0x5e), Separateur = Color.FromArgb(0x26, 0x30, 0x3c),
                          Titre = Color.White, Texte = Color.FromArgb(0xc7, 0xd5, 0xe0), Pale = Color.FromArgb(0x8f, 0x98, 0xa0),
                          Accent = Color.FromArgb(0x66, 0xc0, 0xf4);

    const int L = 300, H = 558, Marge = 16;
    // Lignes : 1 épingler, 2 ouverture, 3 fermeture, 4 couleur de la languette, 5 afficher la languette, 6 gouttière, 7 statut volet ouvert, 8 regrouper les chats, 9 démarrage, 10 quitter.
    static readonly (int haut, int bas)[] Lignes = [(0, 0), (66, 120), (120, 180), (180, 240), (240, 286), (286, 330), (330, 374), (374, 418), (418, 462), (462, 506), (516, 554)];
    public static readonly string[] Couleurs = ["171d25", "3d4450", "35383c"];
    const int Pastille = 22, EcartPastilles = 10;
    const int MaxOuverture = 2000, MaxFermeture = 5000;

    readonly Volet volet;
    readonly Reglages reglages;
    readonly string version;
    readonly Bitmap logo = Icone.Charger(64).ToBitmap();
    int survol, glisse, survolCouleur = -1;
    long cacheA;

    public Panneau(Volet v, Reglages r)
    {
        volet = v; reglages = r;
        var ver = typeof(Panneau).Assembly.GetName().Version;
        version = ver == null ? "" : $"{ver.Major}.{ver.Minor}.{ver.Build}";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        BackColor = Fond;
        Text = "Steem";
        KeyPreview = true;
        Size = new Size((int)(L * K), (int)(H * K));
    }

    float K => DeviceDpi / 96f;

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } // TOOLWINDOW : ni barre des tâches ni Alt+Tab
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int coins = 2, bord = 0x005e472a; // coins arrondis ; liseré #2a475e
        DwmSetWindowAttribute(Handle, 33, ref coins, 4);
        DwmSetWindowAttribute(Handle, 34, ref bord, 4);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Size = new Size((int)(L * K), (int)(H * K));
    }

    // Ouvre au-dessus de l'icône (ou referme) ; un clic sur l'icône qui vient de le fermer ne le rouvre pas.
    public void Basculer()
    {
        if (Visible) { Hide(); return; }
        if (Environment.TickCount64 - cacheA < 300) return;
        Size = new Size((int)(L * K), (int)(H * K));
        var c = Cursor.Position;
        var z = Screen.FromPoint(c).WorkingArea;
        int m = (int)(10 * K);
        int x = Math.Clamp(c.X - Width / 2, z.Left + m, z.Right - Width - m);
        int y = c.Y >= z.Bottom ? z.Bottom - Height - m : c.Y <= z.Top ? z.Top + m : Math.Clamp(c.Y - Height - m, z.Top + m, z.Bottom - Height - m);
        Location = new Point(x, y);
        survol = 0;
        Show();
        Activate();
        Native.Activer(Handle);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        glisse = 0;
        Hide();
        cacheA = Environment.TickCount64;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Hide(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public static string Duree(int ms) => ms == 0 ? "instantané" : $"{ms / 1000.0:0.0} s".Replace('.', ',');

    int Ligne(Point p)
    {
        float y = p.Y / K;
        for (int i = 1; i < Lignes.Length; i++) if (y >= Lignes[i].haut && y < Lignes[i].bas) return i;
        return 0;
    }

    // Pastilles de couleur alignées à droite, dans l'ordre de Couleurs ; -1 : aucune sous la souris.
    static float XPastille(int i) => L - Marge - (Couleurs.Length - i) * Pastille - (Couleurs.Length - 1 - i) * EcartPastilles;

    int Nuance(Point p)
    {
        float x = p.X / K, y = p.Y / K;
        if (y < Lignes[4].haut + 8 || y > Lignes[4].haut + 16 + Pastille) return -1;
        for (int i = 0; i < Couleurs.Length; i++) if (x >= XPastille(i) - 4 && x <= XPastille(i) + Pastille + 4) return i;
        return -1;
    }

    // Valeur du curseur sous la souris, au pas de 0,1 s.
    void Glisser(int x)
    {
        float t = Math.Clamp((x / K - Marge) / (L - 2 * Marge), 0, 1);
        if (glisse == 2) reglages.DelaiSurvolMs = (int)Math.Round(t * MaxOuverture / 100) * 100;
        else reglages.DelaiRepliMs = (int)Math.Round(t * MaxFermeture / 100) * 100;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (glisse != 0) { Glisser(e.X); return; }
        int l = Ligne(e.Location), n = l == 4 ? Nuance(e.Location) : -1;
        if (l != survol || n != survolCouleur)
        {
            survol = l; survolCouleur = n;
            Cursor = l == 0 || (l == 4 && n < 0) ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { if (glisse == 0) { survol = 0; survolCouleur = -1; Invalidate(); } base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        int l = Ligne(e.Location);
        if (l == 2 || l == 3) { glisse = l; Capture = true; Glisser(e.X); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (glisse != 0) { glisse = 0; Capture = false; reglages.Enregistrer(); Invalidate(); return; }
        if (e.Button != MouseButtons.Left) return;
        switch (Ligne(e.Location))
        {
            case 1: volet.BasculerEpingle(); break;
            case 4:
                int n = Nuance(e.Location);
                if (n < 0) break;
                reglages.CouleurLanguette = Couleurs[n];
                reglages.Enregistrer();
                volet.Recolorer();
                break;
            case 5: reglages.AfficherLanguette = !reglages.AfficherLanguette; reglages.Enregistrer(); break;
            case 6: volet.BasculerGouttiere(); break;
            case 7: reglages.StatutSiOuvert = !reglages.StatutSiOuvert; reglages.Enregistrer(); volet.Recolorer(); break;
            case 8: reglages.RegrouperChats = !reglages.RegrouperChats; reglages.Enregistrer(); volet.AppliquerRegroupement(); break;
            case 9: Volet.BasculerDemarrage(); break;
            case 10: Hide(); volet.Quitter(); return;
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Fond);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.ScaleTransform(K, K);

        using var fTitre = new Font("Segoe UI Semibold", 16, GraphicsUnit.Pixel);
        using var fTexte = new Font("Segoe UI", 13, GraphicsUnit.Pixel);
        using var fPetit = new Font("Segoe UI", 11, GraphicsUnit.Pixel);
        using var fIcone = new Font(FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets", 13, GraphicsUnit.Pixel);
        using var bTitre = new SolidBrush(Titre);
        using var bTexte = new SolidBrush(Texte);
        using var bPale = new SolidBrush(Pale);
        using var bAccent = new SolidBrush(Accent);
        var aDroite = new StringFormat { Alignment = StringAlignment.Far };

        // En-tête : logo (le même que l'icône), nom, rôle, version.
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(logo, Marge - 1, 15, 30, 30);
        g.DrawString("Steem", fTitre, bTitre, Marge + 36, 13);
        g.DrawString("Volet de discussion Steam", fPetit, bPale, Marge + 37, 34);
        g.DrawString(version, fPetit, bPale, new RectangleF(0, 17, L - Marge, 16), aDroite);
        Trait(g, 60);

        // Survol des lignes cliquables.
        if (survol is 1 or >= 5)
        {
            using var b = new SolidBrush(Survol);
            using var gp = Arrondi(new RectangleF(6, Lignes[survol].haut + 2, L - 12, Lignes[survol].bas - Lignes[survol].haut - 4), 6);
            g.FillPath(b, gp);
        }

        // 1. Épingler.
        g.DrawString("Épingler le volet", fTexte, bTexte, Marge, Lignes[1].haut + 10);
        g.DrawString("Steam se range à côté de la liste", fPetit, bPale, Marge, Lignes[1].haut + 29);
        Interrupteur(g, L - Marge - 40, Lignes[1].haut + 17, reglages.Epingle);

        // 2 et 3. Délais.
        Curseur(g, 2, "Ouverture au survol du bord", reglages.DelaiSurvolMs, MaxOuverture, fTexte, fPetit, bTexte, bAccent, aDroite);
        Curseur(g, 3, "Fermeture après le départ", reglages.DelaiRepliMs, MaxFermeture, fTexte, fPetit, bTexte, bAccent, aDroite);

        // 4. Couleur de la languette : pastilles, la choisie cerclée de bleu.
        g.DrawString("Couleur de la languette", fTexte, bTexte, Marge, Lignes[4].haut + 13);
        for (int i = 0; i < Couleurs.Length; i++)
        {
            var r = new RectangleF(XPastille(i), Lignes[4].haut + 12, Pastille, Pastille);
            bool choisie = string.Equals(reglages.CouleurLanguette, Couleurs[i], StringComparison.OrdinalIgnoreCase);
            using (var gp = Arrondi(r, 6)) using (var b = new SolidBrush(ColorTranslator.FromHtml("#" + Couleurs[i]))) g.FillPath(b, gp);
            using (var gp = Arrondi(r, 6)) using (var p = new Pen(Piste, 1)) g.DrawPath(p, gp);
            if (choisie || survolCouleur == i)
            {
                using var gp = Arrondi(RectangleF.Inflate(r, 3, 3), 8);
                using var p = new Pen(choisie ? Accent : Pale, 2);
                g.DrawPath(p, gp);
            }
        }

        // 5. Languette visible ou non (le survol du bord et l'icône suffisent).
        g.DrawString("Afficher la languette", fTexte, bTexte, Marge, Lignes[5].haut + 13);
        Interrupteur(g, L - Marge - 40, Lignes[5].haut + 12, reglages.AfficherLanguette);

        // 6. Gouttière pleine hauteur entre Steam et la liste quand c'est épinglé (à la place de la languette).
        g.DrawString("Gouttière quand épinglé", fTexte, bTexte, Marge, Lignes[6].haut + 4);
        g.DrawString("pleine hauteur, sans poignée Sweech", fPetit, bPale, Marge, Lignes[6].haut + 23);
        Interrupteur(g, L - Marge - 40, Lignes[6].haut + 12, reglages.Gouttiere);

        // 7. Icône de statut Steam aussi quand le volet est ouvert.
        g.DrawString("Statut Steam volet ouvert", fTexte, bTexte, Marge, Lignes[7].haut + 4);
        g.DrawString("couleur du bonhomme sur la languette", fPetit, bPale, Marge, Lignes[7].haut + 23);
        Interrupteur(g, L - Marge - 40, Lignes[7].haut + 12, reglages.StatutSiOuvert);

        // 8. Fenêtres de discussion regroupées sous leur propre bouton de barre des tâches.
        g.DrawString("Regrouper les chats", fTexte, bTexte, Marge, Lignes[8].haut + 13);
        Interrupteur(g, L - Marge - 40, Lignes[8].haut + 12, reglages.RegrouperChats);

        // 9. Démarrage.
        g.DrawString("Lancer avec Windows", fTexte, bTexte, Marge, Lignes[9].haut + 13);
        Interrupteur(g, L - Marge - 40, Lignes[9].haut + 12, Volet.AuDemarrage());
        Trait(g, 511);

        // 10. Quitter.
        var bQuitter = survol == 10 ? bTitre : bPale;
        g.DrawString(((char)0xE7E8).ToString(), fIcone, bQuitter, Marge, Lignes[10].haut + 12);
        g.DrawString("Quitter Steem", fTexte, bQuitter, Marge + 22, Lignes[10].haut + 10);
        g.DrawString("la liste reprend sa place", fPetit, bPale, new RectangleF(0, Lignes[10].haut + 12, L - Marge, 16), aDroite);
    }

    void Curseur(Graphics g, int ligne, string titre, int ms, int max, Font fTexte, Font fPetit, Brush bTexte, Brush bAccent, StringFormat aDroite)
    {
        float y = Lignes[ligne].haut;
        g.DrawString(titre, fTexte, bTexte, Marge, y + 8);
        g.DrawString(Duree(ms), fTexte, bAccent, new RectangleF(0, y + 8, L - Marge, 18), aDroite);
        float x0 = Marge, x1 = L - Marge, yp = y + 40, t = Math.Clamp(ms / (float)max, 0, 1), xv = x0 + t * (x1 - x0);
        using (var p = new Pen(Piste, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, x0, yp, x1, yp);
        if (xv > x0) using (var p = new Pen(Accent, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, x0, yp, xv, yp);
        float r = glisse == ligne || survol == ligne ? 8 : 7;
        using var b = new SolidBrush(glisse == ligne ? Accent : Titre);
        g.FillEllipse(b, xv - r, yp - r, 2 * r, 2 * r);
    }

    static void Interrupteur(Graphics g, float x, float y, bool actif)
    {
        using var gp = Arrondi(new RectangleF(x, y, 40, 20), 10);
        if (actif)
        {
            using var b = new SolidBrush(Accent);
            g.FillPath(b, gp);
            using var rond = new SolidBrush(Fond);
            g.FillEllipse(rond, x + 24, y + 4, 12, 12);
        }
        else
        {
            using var p = new Pen(Pale, 1.5f);
            g.DrawPath(p, gp);
            using var rond = new SolidBrush(Pale);
            g.FillEllipse(rond, x + 5, y + 5, 10, 10);
        }
    }

    static void Trait(Graphics g, float y)
    {
        using var p = new Pen(Separateur, 1);
        g.DrawLine(p, Marge, y, L - Marge, y);
    }

    static GraphicsPath Arrondi(RectangleF r, float rayon)
    {
        var gp = new GraphicsPath();
        float d = 2 * rayon;
        gp.AddArc(r.X, r.Y, d, d, 180, 90);
        gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        gp.CloseFigure();
        return gp;
    }
}
