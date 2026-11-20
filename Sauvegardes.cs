using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace Steem;

// Profils de sauvegarde d'un jeu (0.14.0, Kev 20/11) : chaque sauvegarde Ludusavi est un dossier daté
// Sauvegardes\<jeu>\<JJ-MM-AAAA HHhMM>, restaurable tel quel (ludusavi restore --path <dossier>).
static class Ludusavi
{
    public static string Chemin(Reglages r)
    {
        if (!string.IsNullOrWhiteSpace(r.Ludusavi) && File.Exists(r.Ludusavi)) return r.Ludusavi;
        var aCote = Path.Combine(AppContext.BaseDirectory, "ludusavi.exe");
        if (File.Exists(aCote)) return aCote;
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try { var c = Path.Combine(d.Trim(), "ludusavi.exe"); if (File.Exists(c)) return c; } catch { }
        }
        return null;
    }

    static async Task<string> Lancer(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var sortie = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return await sortie;
    }

    // Nom du jeu dans le manifeste de Ludusavi (null : jeu inconnu).
    public static async Task<string> TrouverJeu(string exe, int appid)
    {
        var r = JsonNode.Parse(await Lancer(exe, "find", "--steam-id", appid.ToString(), "--api"));
        return r?["games"]?.AsObject().Select(p => p.Key).FirstOrDefault();
    }

    public static string DossierJeu(Reglages r, string jeu)
    {
        var racine = string.IsNullOrWhiteSpace(r.DossierSauvegardes)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Steem", "Sauvegardes")
            : r.DossierSauvegardes;
        foreach (var c in Path.GetInvalidFileNameChars()) jeu = jeu.Replace(c, '_');
        return Path.Combine(racine, jeu.Trim().TrimEnd('.'));
    }

    public static string NouveauProfil(string dossierJeu, string suffixe = "")
    {
        var nom = DateTime.Now.ToString("dd-MM-yyyy HH'h'mm") + suffixe;
        var p = Path.Combine(dossierJeu, nom);
        for (int i = 2; Directory.Exists(p); i++) p = Path.Combine(dossierJeu, $"{nom} ({i})");
        return p;
    }

    public sealed record Profil(string Dossier, string Nom, DateTime Date, long Octets);

    public static List<Profil> Profils(string dossierJeu)
    {
        var liste = new List<Profil>();
        if (!Directory.Exists(dossierJeu)) return liste;
        foreach (var d in new DirectoryInfo(dossierJeu).GetDirectories())
        {
            try
            {
                if (!d.GetFiles("mapping.yaml", SearchOption.AllDirectories).Any()) continue;
                long octets = d.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                liste.Add(new Profil(d.FullName, d.Name, d.CreationTime, octets));
            }
            catch { }
        }
        return liste.OrderByDescending(p => p.Date).ToList();
    }

    public sealed record Resultat(int Fichiers, long Octets, string Erreur);

    static Resultat Lire(JsonNode r, string jeu)
    {
        long octets = (long?)r?["overall"]?["processedBytes"] ?? (long?)r?["overall"]?["totalBytes"] ?? 0;
        int fichiers = r?["games"]?[jeu]?["files"]?.AsObject().Count ?? 0;
        string erreur = r?["errors"] is JsonObject e && e.Count > 0 ? e.ToJsonString() : null;
        return new Resultat(fichiers, octets, erreur);
    }

    public static async Task<Resultat> Sauvegarder(string exe, string jeu, string dossier) =>
        Lire(JsonNode.Parse(await Lancer(exe, "backup", "--force", "--api", "--no-cloud-sync", "--format", "zip", "--path", dossier, jeu)), jeu);

    public static async Task<Resultat> Restaurer(string exe, string jeu, string dossier) =>
        Lire(JsonNode.Parse(await Lancer(exe, "restore", "--force", "--api", "--no-cloud-sync", "--path", dossier, jeu)), jeu);

    public static string Taille(long octets) => octets >= 1048576 ? $"{octets / 1048576.0:0.0} Mo" : $"{Math.Max(1, octets / 1024)} Ko";
}

// Base des fenêtres de Steem dans le style de Steam : fond sombre, barre de titre sombre, boutons plats alignés.
class FenetreSombre : Form
{
    public static readonly Color Fond = Color.FromArgb(0x17, 0x1d, 0x25), Texte = Color.FromArgb(0xc7, 0xd5, 0xe0),
                                 Pale = Color.FromArgb(0x8f, 0x98, 0xa0), Accent = Color.FromArgb(0x1a, 0x9f, 0xff),
                                 Bouton = Color.FromArgb(0x2a, 0x33, 0x40), Rouge = Color.FromArgb(0xe5, 0x48, 0x3f);
    protected const int Largeur = 540;
    protected readonly FlowLayoutPanel corps, barre;

    protected FenetreSombre(string titreFenetre)
    {
        Text = titreFenetre;
        Icon = Icone.Charger(32);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Fond; ForeColor = Texte;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        TopMost = true;
        corps = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20, 16, 20, 20),
        };
        barre = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 14, 0, 0),
        };
        Controls.Add(corps);
    }

    protected Label Titre(string texte) => Ajouter(new Label
    {
        Text = texte, Font = new Font("Segoe UI Semibold", 13f), ForeColor = Color.White,
        AutoSize = true, MaximumSize = new Size(Largeur, 0), Margin = new Padding(0, 0, 0, 8),
    });

    protected Label Paragraphe(string texte, Color couleur, int hauteurMin = 0) => Ajouter(new Label
    {
        Text = texte, ForeColor = couleur, AutoSize = true,
        MaximumSize = new Size(Largeur, 0), MinimumSize = new Size(Largeur, hauteurMin), Margin = new Padding(0, 0, 0, 4),
    });

    protected T Ajouter<T>(T c) where T : Control { corps.Controls.Add(c); return c; }

    // Boutons de même hauteur, alignés sur une ligne en bas de la fenêtre.
    protected Button BoutonBas(string texte, Color fond)
    {
        var b = new Button
        {
            Text = texte, FlatStyle = FlatStyle.Flat, BackColor = fond, ForeColor = Color.White, Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 38), Padding = new Padding(14, 0, 14, 0), Margin = new Padding(0, 0, 10, 0),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        b.FlatAppearance.BorderSize = 0;
        Couleur(b, fond);
        // Bouton inactif : fond gris, quelle que soit sa couleur.
        b.EnabledChanged += (_, _) => b.BackColor = b.Enabled ? (Color)b.Tag : Bouton;
        barre.Controls.Add(b);
        return b;
    }

    protected static void Couleur(Button b, Color fond)
    {
        b.Tag = fond;
        b.BackColor = b.Enabled ? fond : Bouton;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(fond, 0.15f);
    }

    protected void PoserBarre() => corps.Controls.Add(barre);

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int sombre = 1; DwmSetWindowAttribute(Handle, 20, ref sombre, 4); // barre de titre sombre
    }
}

// Disquette de la page du jeu : sauvegarder maintenant ou restaurer une sauvegarde.
sealed class FenetreSauvegardes : FenetreSombre
{
    readonly Reglages reglages;
    readonly int appid;
    readonly Label etat;
    readonly ListBox liste;
    readonly Button sauver, restaurer, ouvrir, fermer;
    string exe, jeu, dossierJeu;
    bool confirmer;

    public FenetreSauvegardes(Reglages r, int appid, string nom) : base("Steem — Sauvegardes")
    {
        reglages = r; this.appid = appid;
        Titre($"Sauvegardes de « {nom} »");
        Paragraphe("Fichiers du jeu sur ce PC : sauvegardes de partie, configs, binds.", Texte);
        liste = Ajouter(new ListBox
        {
            Size = new Size(Largeur, 6 * 30), BorderStyle = BorderStyle.None, BackColor = Bouton, ForeColor = Texte,
            DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 30, IntegralHeight = false, Margin = new Padding(0, 6, 0, 6),
        });
        liste.DrawItem += Dessiner;
        liste.SelectedIndexChanged += (_, _) => { Desarmer(); Boutons(true); };
        etat = Paragraphe("", Pale, 44);
        sauver = BoutonBas("Sauvegarder maintenant", Accent);
        restaurer = BoutonBas("Restaurer", Bouton);
        ouvrir = BoutonBas("Ouvrir le dossier", Bouton);
        fermer = BoutonBas("Fermer", Bouton);
        PoserBarre();
        sauver.Click += async (_, _) => await Sauvegarder();
        restaurer.Click += async (_, _) => await Restaurer();
        ouvrir.Click += (_, _) => Ouvrir();
        fermer.Click += (_, _) => Close();
        CancelButton = fermer;
        Boutons(false);
        Load += async (_, _) => await Preparer();
    }

    void Dessiner(object s, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var p = (Ludusavi.Profil)liste.Items[e.Index];
        bool choisi = (e.State & DrawItemState.Selected) != 0;
        using (var f = new SolidBrush(choisi ? Color.FromArgb(0x3d, 0x6c, 0x8d) : Bouton)) e.Graphics.FillRectangle(f, e.Bounds);
        var r = Rectangle.Inflate(e.Bounds, -10, 0);
        TextRenderer.DrawText(e.Graphics, p.Nom, Font, r, Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, Ludusavi.Taille(p.Octets), Font, r, choisi ? Color.White : Pale, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }

    void Boutons(bool actifs)
    {
        sauver.Enabled = actifs && jeu != null;
        restaurer.Enabled = actifs && jeu != null && liste.SelectedItem != null;
        ouvrir.Enabled = actifs && dossierJeu != null;
        fermer.Enabled = true;
    }

    void Desarmer()
    {
        if (!confirmer) return;
        confirmer = false;
        restaurer.Text = "Restaurer";
        Couleur(restaurer, Bouton);
        etat.Text = ""; etat.ForeColor = Pale;
    }

    void Remplir(string choix = null)
    {
        liste.Items.Clear();
        foreach (var p in Ludusavi.Profils(dossierJeu)) liste.Items.Add(p);
        if (liste.Items.Count == 0) { etat.ForeColor = Pale; etat.Text = "Aucune sauvegarde pour l'instant."; return; }
        liste.SelectedIndex = Math.Max(0, liste.Items.Cast<Ludusavi.Profil>().ToList().FindIndex(p => p.Dossier == choix));
    }

    async Task Preparer()
    {
        exe = Ludusavi.Chemin(reglages);
        if (exe == null)
        {
            etat.ForeColor = Rouge;
            etat.Text = "Ludusavi introuvable : sauvegarde impossible (réglage « Ludusavi » de Steem).";
            return;
        }
        etat.Text = "Recherche des fichiers du jeu…";
        try { jeu = await Ludusavi.TrouverJeu(exe, appid); }
        catch (Exception ex) { etat.ForeColor = Rouge; etat.Text = "Ludusavi a échoué : " + ex.Message; return; }
        if (jeu == null)
        {
            etat.ForeColor = Rouge;
            etat.Text = "Ludusavi ne connaît pas les fichiers de ce jeu.";
            return;
        }
        dossierJeu = Ludusavi.DossierJeu(reglages, jeu);
        etat.Text = "";
        Remplir();
        Boutons(true);
    }

    async Task Sauvegarder()
    {
        Desarmer();
        Boutons(false);
        etat.ForeColor = Pale; etat.Text = "Sauvegarde en cours…";
        var dossier = Ludusavi.NouveauProfil(dossierJeu);
        try
        {
            var r = await Ludusavi.Sauvegarder(exe, jeu, dossier);
            if (r.Fichiers == 0 || r.Erreur != null)
            {
                etat.ForeColor = Rouge;
                etat.Text = r.Fichiers == 0 ? "Aucun fichier trouvé pour ce jeu : rien n'a été sauvegardé." : "La sauvegarde a rencontré une erreur : " + r.Erreur;
                if (r.Fichiers == 0) try { Directory.Delete(dossier, true); } catch { }
            }
            else
            {
                Remplir(dossier);
                etat.ForeColor = Texte;
                etat.Text = $"✓ {r.Fichiers} fichier{(r.Fichiers > 1 ? "s" : "")} sauvegardé{(r.Fichiers > 1 ? "s" : "")} ({Ludusavi.Taille(r.Octets)}).";
            }
        }
        catch (Exception ex) { etat.ForeColor = Rouge; etat.Text = "Échec de la sauvegarde : " + ex.Message; }
        Boutons(true);
    }

    async Task Restaurer()
    {
        if (liste.SelectedItem is not Ludusavi.Profil p) return;
        if (!confirmer)
        {
            // Premier clic : on explique, le second clic restaure.
            confirmer = true;
            restaurer.Text = "Confirmer la restauration";
            Couleur(restaurer, Rouge);
            etat.ForeColor = Texte;
            etat.Text = $"Les fichiers actuels du jeu seront remplacés par ceux du {p.Nom}. Une copie de l'état actuel est faite juste avant. Ferme le jeu s'il tourne.";
            return;
        }
        Desarmer();
        Boutons(false);
        etat.ForeColor = Pale; etat.Text = "Copie de l'état actuel…";
        try
        {
            var copie = Ludusavi.NouveauProfil(dossierJeu, " (avant restauration)");
            var c = await Ludusavi.Sauvegarder(exe, jeu, copie);
            if (c.Fichiers == 0) try { Directory.Delete(copie, true); } catch { }
            else if (c.Erreur != null)
            {
                etat.ForeColor = Rouge;
                etat.Text = "La copie de l'état actuel a échoué, restauration annulée : " + c.Erreur;
                Remplir(p.Dossier); Boutons(true);
                return;
            }
            etat.Text = "Restauration en cours…";
            var r = await Ludusavi.Restaurer(exe, jeu, p.Dossier);
            Remplir(p.Dossier);
            if (r.Erreur != null || r.Fichiers == 0)
            {
                etat.ForeColor = Rouge;
                etat.Text = r.Erreur != null ? "La restauration a rencontré une erreur : " + r.Erreur : "Aucun fichier restauré.";
            }
            else
            {
                etat.ForeColor = Texte;
                etat.Text = $"✓ {r.Fichiers} fichier{(r.Fichiers > 1 ? "s" : "")} restauré{(r.Fichiers > 1 ? "s" : "")} depuis le {p.Nom}.";
            }
        }
        catch (Exception ex) { etat.ForeColor = Rouge; etat.Text = "Échec de la restauration : " + ex.Message; }
        Boutons(true);
    }

    void Ouvrir()
    {
        try
        {
            Directory.CreateDirectory(dossierJeu);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dossierJeu}\"") { UseShellExecute = true });
        }
        catch { }
    }
}
