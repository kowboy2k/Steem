using System.Drawing.Drawing2D;
using System.Text;

namespace Steem;

// `--essai-ecrans dossier` : rejoue les calculs de Geometrie sur des écrans fictifs (résolutions, échelles,
// barre des tâches, plusieurs écrans, Steam non agrandi) et dessine chaque cas, sans rien afficher ni déplacer.
// Vérifie que Steam, la liste et la languette restent dans la zone utile de l'écran.
static class Essai
{
    sealed record Cas(string Nom, Rectangle Ecran, Rectangle Travail, float K, Rectangle? Steam = null);

    static Rectangle Barre(Rectangle e, string cote, int h) => cote switch
    {
        "gauche" => new(e.X + h, e.Y, e.Width - h, e.Height),
        "droite" => new(e.X, e.Y, e.Width - h, e.Height),
        "haut" => new(e.X, e.Y + h, e.Width, e.Height - h),
        _ => new(e.X, e.Y, e.Width, e.Height - h),
    };

    static Cas Ecran(string nom, int w, int h, float k, string barre = "bas", int x = 0, int y = 0, Rectangle? steam = null)
    {
        var e = new Rectangle(x, y, w, h);
        return new Cas(nom, e, Barre(e, barre, (int)(48 * k)), k, steam);
    }

    static readonly Cas[] Liste =
    [
        Ecran("1366×768 (petit portable) 100 %", 1366, 768, 1f),
        Ecran("1920×1080 100 %", 1920, 1080, 1f),
        Ecran("1920×1080 125 % (portable)", 1920, 1080, 1.25f),
        Ecran("1920×1080 barre à gauche", 1920, 1080, 1f, "gauche"),
        Ecran("1920×1080 barre en haut", 1920, 1080, 1f, "haut"),
        Ecran("1920×1080 barre à droite", 1920, 1080, 1f, "droite"),
        Ecran("2560×1440 125 %", 2560, 1440, 1.25f),
        Ecran("3440×1440 100 % (ultra-large)", 3440, 1440, 1f),
        Ecran("3840×2160 150 % (4K)", 3840, 2160, 1.5f),
        Ecran("3840×2160 200 % (4K)", 3840, 2160, 2f),
        Ecran("1080×1920 portrait 100 %", 1080, 1920, 1f),
        Ecran("2e écran à gauche (x = -1920)", 1920, 1080, 1f, "bas", -1920, 0),
        Ecran("1280×1024 (4:3) 100 %", 1280, 1024, 1f),
        Ecran("Steam non agrandi, au centre", 1920, 1080, 1f, steam: new Rectangle(360, 140, 1200, 800)),
        Ecran("Steam non agrandi, collé à droite", 1920, 1080, 1f, steam: new Rectangle(1020, 60, 900, 900)),
        Ecran("Steam petit (1000 px) non agrandi", 1920, 1080, 1f, steam: new Rectangle(200, 100, 1000, 700)),
    ];

    static readonly string[] Modes = ["replié", "ouvert par-dessus", "épinglé (languette)", "épinglé (gouttière)"];

    public static void Lancer(string dossier)
    {
        Directory.CreateDirectory(dossier);
        var rapport = new StringBuilder($"Steem — essai des positions sur écrans fictifs, {DateTime.Now:dd/MM/yyyy HH:mm:ss}\r\n");
        rapport.AppendLine("Liste à sa largeur par défaut (470 px × échelle, premier lancement chez un ami).\r\n");
        int problemes = 0;
        const int cw = 300, ch = 200, entete = 26, marge = 10;
        using var planche = new Bitmap(marge + Modes.Length * (cw + marge), Liste.Length * (ch + entete + marge) + 40);
        using var g = Graphics.FromImage(planche);
        g.Clear(Color.FromArgb(0x20, 0x20, 0x20));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var f = new Font("Segoe UI", 10, FontStyle.Bold);
        using var fp = new Font("Segoe UI", 8);
        for (int m = 0; m < Modes.Length; m++) g.DrawString(Modes[m], f, Brushes.Silver, marge + m * (cw + marge), 10);

        for (int i = 0; i < Liste.Length; i++)
        {
            var c = Liste[i];
            int y0 = 40 + i * (ch + entete + marge);
            rapport.AppendLine($"{c.Nom} — écran {c.Ecran}, zone utile {c.Travail}, échelle {c.K * 100:0} %");
            var erreursCas = new List<string>();
            for (int m = 0; m < Modes.Length; m++)
            {
                var r = new Reglages { Gouttiere = m == 3 };
                bool cote = m >= 2, deplie = m >= 1, agrandi = c.Steam == null;
                var steamAvant = c.Steam ?? c.Travail;
                var vs = cote ? Geometrie.SteamEpingle(c.Travail, steamAvant, agrandi, c.K, r) : steamAvant;
                // Comme Volet : si la liste dépasse à droite, Steam est rétréci de l'excédent.
                int trop = cote ? Geometrie.Excedent(vs, c.Travail, c.K, r) : 0;
                if (trop > 1) { if (vs.Width - trop >= (int)(500 * c.K)) vs.Width -= trop; else vs.X -= trop; }
                var (tiroir, languette) = Geometrie.Disposer(vs, c.K, r, cote, deplie);
                var visibleLanguette = m == 3 ? new Rectangle(vs.Right, vs.Top, Geometrie.LargeurLanguette(c.K), vs.Height) : languette;

                var erreurs = new List<string>();
                void Verifier(string quoi, Rectangle x) { if (!c.Travail.Contains(x)) erreurs.Add($"{quoi} hors de la zone utile ({x})"); }
                Verifier("Steam", vs);
                if (deplie) Verifier("liste", tiroir);
                Verifier(m == 3 ? "gouttière" : "languette", visibleLanguette);
                if (vs.Width < (int)(500 * c.K) - 1) erreurs.Add($"Steam trop étroit ({vs.Width} px)");
                if (deplie && tiroir.Width < (int)(260 * c.K) - 1) erreurs.Add($"liste trop étroite ({tiroir.Width} px)");
                foreach (var e in erreurs) erreursCas.Add($"   ✗ {Modes[m]} : {e}");
                problemes += erreurs.Count;

                // Dessin : écran à l'échelle de la case.
                int x0 = marge + m * (cw + marge);
                float s = Math.Min((cw - 8f) / c.Ecran.Width, (ch - 8f) / c.Ecran.Height);
                float ox = x0 + (cw - c.Ecran.Width * s) / 2, oy = y0 + entete + (ch - c.Ecran.Height * s) / 2;
                RectangleF V(Rectangle x) => new(ox + (x.X - c.Ecran.X) * s, oy + (x.Y - c.Ecran.Y) * s, x.Width * s, x.Height * s);
                using (var b = new SolidBrush(Color.FromArgb(0x0e, 0x0e, 0x0e))) g.FillRectangle(b, V(c.Ecran));
                using (var b = new SolidBrush(Color.FromArgb(0x2c, 0x2c, 0x34))) g.FillRectangle(b, V(c.Ecran));
                using (var b = new SolidBrush(Color.FromArgb(0x10, 0x10, 0x14))) g.FillRectangle(b, V(c.Travail));
                using (var b = new SolidBrush(Color.FromArgb(0x1b, 0x28, 0x38))) g.FillRectangle(b, V(vs));
                if (deplie) using (var b = new SolidBrush(Color.FromArgb(0x3d, 0x6e, 0x9a))) g.FillRectangle(b, V(tiroir));
                using (var b = new SolidBrush(Color.FromArgb(0xf0, 0xa8, 0x30))) g.FillRectangle(b, V(visibleLanguette));
                using (var p = new Pen(erreurs.Count > 0 ? Color.Red : Color.FromArgb(0x50, 0xc0, 0x60), 2)) g.DrawRectangle(p, Rectangle.Round(V(c.Ecran)));
            }
            g.DrawString(c.Nom + (erreursCas.Count > 0 ? "  ✗ problème" : "  ✓"), f, erreursCas.Count > 0 ? Brushes.OrangeRed : Brushes.LightGreen, marge, y0 + 4);
            if (erreursCas.Count == 0) rapport.AppendLine("   ✓ tout tient dans l'écran");
            foreach (var e in erreursCas) rapport.AppendLine(e);
            rapport.AppendLine();
        }
        rapport.AppendLine(problemes == 0 ? "Résultat : aucun problème." : $"Résultat : {problemes} problème(s).");
        planche.Save(Path.Combine(dossier, "essai-ecrans.png"));
        File.WriteAllText(Path.Combine(dossier, "essai-ecrans.txt"), rapport.ToString(), new UTF8Encoding(true));
    }
}
