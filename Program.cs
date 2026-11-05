using System.Text;

namespace Steem;

static class Program
{
    public const string SignalQuitter = @"Local\Steem-quitter";

    [STAThread]
    static void Main(string[] args)
    {
        // --quitter : ferme proprement l'instance en cours (la liste de contacts reprend sa place).
        if (args.Length > 0 && args[0] == "--quitter")
        {
            if (EventWaitHandle.TryOpenExisting(SignalQuitter, out var e)) using (e) e.Set();
            // Attend (5 s au plus) que l'instance ait fini : l'installateur peut alors remplacer l'exe.
            if (Mutex.TryOpenExisting(@"Local\Steem", out var m))
                using (m) try { if (m.WaitOne(5000)) m.ReleaseMutex(); } catch (AbandonedMutexException) { m.ReleaseMutex(); }
            return;
        }
        ApplicationConfiguration.Initialize();
        if (args.Length > 0 && args[0] == "--diag")
        {
            Diagnostic(args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "diag.txt"));
            return;
        }

        if (args.Length > 1 && args[0] == "--apercu")
        {
            // Couleur de fond facultative (RRGGBB) pour comparer des teintes de languette.
            if (args.Length > 2) Poignee.Fond = ColorTranslator.FromHtml("#" + args[2]);
            Apercu(args[1]);
            return;
        }
        if (args.Length > 1 && args[0] == "--apercu-statuts")
        {
            // Languette repliée pour chaque statut Steam, nommé dessous (image du README), sans rien afficher.
            ApercuStatuts(args[1]);
            return;
        }
        if (args.Length > 1 && args[0] == "--essai-ecrans")
        {
            // Positions du volet rejouées sur des écrans fictifs (images + rapport), sans rien afficher.
            Essai.Lancer(args[1]);
            return;
        }
        if (args.Length > 1 && args[0] == "--apercu-panneau")
        {
            // Dessine le panneau de l'icône dans une image, sans rien afficher.
            using var p = new Panneau(null, Reglages.Charger());
            using var b = new Bitmap(p.Width, p.Height);
            p.DrawToBitmap(b, new Rectangle(Point.Empty, p.Size));
            b.Save(args[1]);
            return;
        }

        using var verrou = new Mutex(true, @"Local\Steem", out bool seul);
        if (!seul) return;
        using var volet = new Volet(Reglages.Charger());
        Application.Run();
    }

    // Dessine la languette dans ses états (libre, ouvert, épinglé, nouveau message) dans une image, sans rien afficher.
    static void ApercuStatuts(string chemin)
    {
        (int statut, string fr, string en)[] etats = [(1, "En ligne", "Online"), (3, "Absent", "Away"), (2, "Ne pas déranger", "Do not disturb"), (7, "Invisible", "Invisible"), (0, "Hors ligne", "Offline")];
        Poignee.StatutSiOuvert = false; // bonhomme en haut, flèche au milieu (réglage par défaut)
        const int f = 3, col = 120;
        using var image = new Bitmap(etats.Length * col, 115 * f + 95);
        using var g = Graphics.FromImage(image);
        g.Clear(Color.FromArgb(0x17, 0x1d, 0x25));
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var fr = new Font("Segoe UI Semibold", 13, GraphicsUnit.Pixel);
        using var en = new Font("Segoe UI", 11, GraphicsUnit.Pixel);
        var centre = new StringFormat { Alignment = StringAlignment.Center };
        for (int i = 0; i < etats.Length; i++)
        {
            using var p = new Poignee();
            p.Statut = etats[i].statut;
            using var b = p.Image(new Size(19, 115));
            // Languette agrandie (×3) au pixel près, centrée dans sa colonne.
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(b, i * col + col / 2 - b.Width * f + 19 * f / 2, 10, b.Width * f, b.Height * f); // partie visible (19 px, à droite de l'ombre) centrée
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Default;
            float y = 10 + b.Height * f - 6;
            g.DrawString(etats[i].fr, fr, Brushes.White, new RectangleF(i * col, y, col, 18), centre);
            g.DrawString(etats[i].en, en, Brushes.Gray, new RectangleF(i * col, y + 18, col, 16), centre);
        }
        image.Save(chemin);
    }

    static void Apercu(string chemin)
    {
        (bool epingle, bool deplie, bool nonLu, int statut)[] etats = [(false, false, false, 1), (false, true, false, 1), (true, true, false, 1), (false, false, true, 1), (false, false, false, 3), (false, false, false, 2), (false, false, false, 7), (false, false, false, 0)];
        using var image = new Bitmap(etats.Length * 40, 140);
        using var g = Graphics.FromImage(image);
        g.Clear(Color.FromArgb(0x17, 0x1d, 0x25));
        for (int i = 0; i < etats.Length; i++)
        {
            using var p = new Poignee();
            p.Epingle = etats[i].epingle; p.Deplie = etats[i].deplie; p.NonLu = etats[i].nonLu; p.Statut = etats[i].statut;
            using var b = p.Image(new Size(19, 115));
            g.DrawImage(b, i * 40 + 10 - (b.Width - 19), 12 - (b.Height - 115) / 2);
        }
        image.Save(chemin);
    }

    // Lecture seule : liste ce que Steem voit de Steam et où irait le volet, sans rien déplacer.
    static void Diagnostic(string chemin)
    {
        var r = Reglages.Charger();
        var steam = new Steam();
        var sb = new StringBuilder();
        sb.AppendLine($"Steem diagnostic {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        foreach (var f in steam.Lister()) sb.AppendLine("  " + f);
        bool ok = steam.Trouver(r.TitresContacts);
        sb.AppendLine($"Principale = {steam.Principale} ({(ok ? Native.Titre(steam.Principale) : "introuvable")})");
        sb.AppendLine($"Contacts   = {steam.Contacts} ({(steam.Contacts != IntPtr.Zero ? Native.Titre(steam.Contacts) : "introuvable")}) ok={steam.ContactsOk}");
        if (ok)
        {
            var vs = Native.Visuel(steam.Principale);
            float k = Native.Echelle(steam.Principale);
            if (r.Largeur <= 0 && steam.ContactsOk) r.Largeur = Native.Visuel(steam.Contacts).Width;
            var tiroir = Volet.Tiroir(vs, k, r);
            sb.AppendLine($"Steam visible = {vs} échelle={k}");
            sb.AppendLine($"Volet déplié  = {tiroir}");
            if (steam.ContactsOk)
                sb.AppendLine($"Rect contacts à donner = {Native.RectPourVisuel(steam.Contacts, tiroir)} (actuel {Native.Rect(steam.Contacts)})");
        }
        File.WriteAllText(chemin, sb.ToString());
    }
}
