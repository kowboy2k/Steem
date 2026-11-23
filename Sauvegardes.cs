using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

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

    // Réglages propres à Steem (0.15.2) : les jeux ajoutés par Steem n'encombrent pas le Ludusavi de l'utilisateur.
    static string DossierConfig => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Steem", "Ludusavi");

    static async Task<string> Lancer(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("--config"); psi.ArgumentList.Add(DossierConfig);
        psi.ArgumentList.Add("--try-manifest-update");
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var sortie = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return await sortie;
    }

    // Jeu pour Ludusavi : Nom = son nom ; Dossiers = null s'il est dans le manifeste de Ludusavi, sinon les dossiers
    // trouvés par Steem et déclarés comme jeu personnalisé. null : rien de connu ni trouvé.
    public sealed record Jeu(string Nom, string[] Dossiers);

    public static async Task<Jeu> TrouverJeu(string exe, int appid, string nomSteam)
    {
        var r = JsonNode.Parse(await Lancer(exe, "find", "--steam-id", appid.ToString(), "--api"));
        var connu = r?["games"]?.AsObject().Select(p => p.Key).FirstOrDefault();
        if (connu != null) return new Jeu(connu, null);
        if (string.IsNullOrWhiteSpace(nomSteam)) return null;

        // Jeu absent du manifeste (20/11) : déjà déclaré par Steem, sinon recherche de ses dossiers.
        var config = JsonNode.Parse(await Lancer(exe, "config", "show", "--api"))!.AsObject();
        if (config["customGames"] is not JsonArray perso) config["customGames"] = perso = new JsonArray();
        var deja = perso.FirstOrDefault(g => (string)g?["name"] == nomSteam);
        if (deja != null) return new Jeu(nomSteam, deja["files"]?.AsArray().Select(f => (string)f).ToArray() ?? []);
        var dossiers = Chercher(nomSteam, appid);
        if (dossiers.Count == 0) return null;
        perso.Add(new JsonObject
        {
            ["name"] = nomSteam, ["integration"] = "override",
            ["files"] = new JsonArray(dossiers.Select(d => (JsonNode)d).ToArray()),
            ["registry"] = new JsonArray(), ["installDir"] = new JsonArray(), ["winePrefix"] = new JsonArray(),
        });
        // Le JSON est du YAML valide : Ludusavi le relit tel quel.
        Directory.CreateDirectory(DossierConfig);
        File.WriteAllText(Path.Combine(DossierConfig, "config.yaml"),
            config.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        return new Jeu(nomSteam, dossiers.ToArray());
    }

    static string Cle(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    // Dossiers portant le nom du jeu (1 ou 2 niveaux) là où les jeux rangent leurs fichiers, plus son dossier Steam Cloud.
    static List<string> Chercher(string nom, int appid)
    {
        var res = new List<string>();
        var cle = Cle(nom);
        if (cle.Length < 4) return res;   // nom trop court : trop de faux positifs
        var profil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string[] racines =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(profil, "AppData", "LocalLow"),
            documents, Path.Combine(documents, "My Games"), Path.Combine(profil, "Saved Games"),
        ];
        string[] exclus = ["Temp", "Steem", "Packages", "Microsoft", "Google", "Steam"];
        foreach (var racine in racines)
        {
            if (!Directory.Exists(racine)) continue;
            IEnumerable<DirectoryInfo> n1;
            try { n1 = new DirectoryInfo(racine).GetDirectories(); } catch { continue; }
            foreach (var d1 in n1)
            {
                if (exclus.Contains(d1.Name, StringComparer.OrdinalIgnoreCase)) continue;
                if (Cle(d1.Name) == cle) { res.Add(d1.FullName); continue; }
                try { foreach (var d2 in d1.GetDirectories()) if (Cle(d2.Name) == cle) res.Add(d2.FullName); } catch { }
            }
        }
        var steam = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (steam != null && Directory.Exists(Path.Combine(steam, "userdata")))
            foreach (var u in Directory.GetDirectories(Path.Combine(steam, "userdata")))
            {
                var distant = Path.Combine(u, appid.ToString(), "remote");
                if (Directory.Exists(distant)) res.Add(distant);
            }
        return res.Distinct(StringComparer.OrdinalIgnoreCase).Select(d => Path.GetFullPath(d).Replace('\\', '/')).ToList();
    }

    public static string Racine(Reglages r) => string.IsNullOrWhiteSpace(r.DossierSauvegardes)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Steem", "Sauvegardes")
        : r.DossierSauvegardes;

    public static string DossierJeu(Reglages r, string jeu)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) jeu = jeu.Replace(c, '_');
        return Path.Combine(Racine(r), jeu.Trim().TrimEnd('.'));
    }

    // Le dossier d'un jeu garde son numéro Steam (steem-appid.txt) : la disquette de Steam retrouve ainsi sa dernière sauvegarde.
    const string FichierAppid = "steem-appid.txt";

    // Dernière sauvegarde de chaque jeu par numéro Steam : { t: secondes Unix, + compte rendu resume.json s'il existe }
    // (LED et infobulle de la disquette, 0.15.0 / 0.16.0).
    public static Dictionary<int, JsonObject> Dernieres(Reglages r)
    {
        var res = new Dictionary<int, JsonObject>();
        try
        {
            foreach (var jeu in new DirectoryInfo(Racine(r)).GetDirectories())
            {
                try
                {
                    var f = Path.Combine(jeu.FullName, FichierAppid);
                    if (!File.Exists(f) || !int.TryParse(File.ReadAllText(f).Trim(), out var appid)) continue;
                    var dernier = jeu.GetDirectories().Where(EstProfil).OrderByDescending(d => d.CreationTime).FirstOrDefault();
                    if (dernier == null) continue;
                    var o = new JsonObject();
                    try
                    {
                        var rf = Path.Combine(dernier.FullName, FichierResume);
                        if (File.Exists(rf) && JsonNode.Parse(File.ReadAllText(rf)) is JsonObject lu) o = lu;
                    }
                    catch { }
                    o["t"] = new DateTimeOffset(dernier.CreationTime).ToUnixTimeSeconds();
                    o["dossier"] = dernier.FullName;
                    res[appid] = o;
                }
                catch { }
            }
        }
        catch { }
        return res;
    }

    public static string NouveauProfil(string dossierJeu, string suffixe = "")
    {
        var nom = DateTime.Now.ToString("dd-MM-yyyy HH'h'mm") + suffixe;
        var p = Path.Combine(dossierJeu, nom);
        for (int i = 2; Directory.Exists(p); i++) p = Path.Combine(dossierJeu, $"{nom} ({i})");
        return p;
    }

    // Fichiers = sauvegarde Ludusavi restaurable ; Mods = nombre de mods de la liste (0 : aucune).
    public sealed record Profil(string Dossier, string Nom, DateTime Date, long Octets, bool Fichiers, int Mods)
    {
        public override string ToString() => Nom;   // nom lu par l'accessibilité de Windows
        public string PageMods => Path.Combine(Dossier, Steem.Mods.FichierPage);
    }

    static bool EstProfil(DirectoryInfo d) =>
        File.Exists(Path.Combine(d.FullName, Steem.Mods.FichierJson)) || d.GetFiles("mapping.yaml", SearchOption.AllDirectories).Length > 0;

    public static List<Profil> Profils(string dossierJeu)
    {
        var liste = new List<Profil>();
        if (!Directory.Exists(dossierJeu)) return liste;
        foreach (var d in new DirectoryInfo(dossierJeu).GetDirectories())
        {
            try
            {
                bool fichiers = d.GetFiles("mapping.yaml", SearchOption.AllDirectories).Length > 0;
                int mods = Steem.Mods.Compter(d.FullName);
                if (!fichiers && mods == 0) continue;
                long octets = d.GetFiles("*", SearchOption.AllDirectories)
                               .Where(f => f.Name != Steem.Mods.FichierJson && f.Name != Steem.Mods.FichierPage).Sum(f => f.Length);
                liste.Add(new Profil(d.FullName, d.Name, d.CreationTime, octets, fichiers, mods));
            }
            catch { }
        }
        return liste.OrderByDescending(p => p.Date).ToList();
    }

    // Une sauvegarde complète : fichiers de partie (Ludusavi, si le jeu est connu) + liste des mods.
    public sealed record Bilan(Resultat Sauvegarde, int Mods, string Dossier)
    {
        public bool Vide => (Sauvegarde?.Fichiers ?? 0) == 0 && Mods == 0;
        public string Erreur => Sauvegarde?.Erreur;
        public string Resume()
        {
            var parts = new List<string>();
            int f = Sauvegarde?.Fichiers ?? 0;
            if (f > 0) parts.Add($"{f} fichier{(f > 1 ? "s" : "")} sauvegardé{(f > 1 ? "s" : "")} ({Taille(Sauvegarde.Octets)})");
            if (Mods > 0) parts.Add($"liste de {Mods} mod{(Mods > 1 ? "s" : "")}");
            return "✓ " + string.Join(" + ", parts);
        }
    }

    public static async Task<Bilan> SauvegarderTout(Reglages r, string exe, Jeu jeu, string nomSteam, int appid, string suffixe = "")
    {
        var dossierJeu = DossierJeu(r, jeu?.Nom ?? nomSteam);
        var dossier = NouveauProfil(dossierJeu, suffixe);
        Resultat res = null;
        if (exe != null && jeu != null) res = await Sauvegarder(exe, jeu.Nom, dossier, appid);
        int mods = 0;
        try { mods = await Steem.Mods.Ecrire(appid, nomSteam, dossier); } catch { }
        var b = new Bilan(res, mods, dossier);
        if (b.Vide) { try { if (Directory.Exists(dossier)) Directory.Delete(dossier, true); } catch { } }
        else
        {
            try { File.WriteAllText(Path.Combine(dossierJeu, FichierAppid), appid.ToString()); } catch { }
            try { File.WriteAllText(Path.Combine(dossier, FichierResume), Resume(res?.Chemins ?? [], mods, dossier).ToJsonString()); } catch { }
        }
        return b;
    }

    public sealed record Resultat(int Fichiers, long Octets, string Erreur, string[] Chemins);

    static Resultat Lire(JsonNode r, string jeu)
    {
        long octets = (long?)r?["overall"]?["processedBytes"] ?? (long?)r?["overall"]?["totalBytes"] ?? 0;
        var chemins = r?["games"]?[jeu]?["files"]?.AsObject().Select(p => p.Key).ToArray() ?? [];
        string erreur = r?["errors"] is JsonObject e && e.Count > 0 ? e.ToJsonString() : null;
        return new Resultat(chemins.Length, octets, erreur, chemins);
    }

    // Compte rendu d'une sauvegarde (resume.json, infobulle de la disquette, 0.16.0) : les fichiers rangés
    // d'après leur chemin. Ludusavi ne les classe pas : c'est une estimation (binds, puis parties, puis configs).
    public const string FichierResume = "resume.json";
    static readonly Regex Binds = new(@"bind|keymap|keyboard|input|control|actionmap|hotkey", RegexOptions.IgnoreCase);
    static readonly Regex Parties = new(@"save|\.(sav|ess|fos|sl2)$", RegexOptions.IgnoreCase);
    static readonly Regex Configs = new(@"config|setting|option|pref|\.(ini|cfg|conf|json|xml|ya?ml|toml)$", RegexOptions.IgnoreCase);

    static JsonObject Resume(string[] chemins, int mods, string dossier)
    {
        // Sources de la liste des mods (infobulle : chacune cliquable, ouvre sa liste et l'import).
        var sources = new JsonArray(Steem.Mods.Lire(dossier).Select(s => (JsonNode)new JsonObject { ["titre"] = s.Titre, ["n"] = s.Mods.Count }).ToArray());
        int b = 0, p = 0, c = 0, a = 0;
        foreach (var x in chemins)
        {
            var f = x.Replace('\\', '/');
            if (Binds.IsMatch(Path.GetFileName(f)) || Binds.IsMatch(f)) b++;
            else if (Parties.IsMatch(f)) p++;
            else if (Configs.IsMatch(f)) c++;
            else a++;
        }
        return new JsonObject { ["parties"] = p, ["binds"] = b, ["configs"] = c, ["autres"] = a, ["mods"] = mods, ["sources"] = sources, ["dossier"] = dossier };
    }

    public static async Task<Resultat> Sauvegarder(string exe, string jeu, string dossier, int appid)
    {
        return Lire(JsonNode.Parse(await Lancer(exe, "backup", "--force", "--api", "--no-cloud-sync", "--format", "zip", "--path", dossier, jeu)), jeu);
    }

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
    protected const int Largeur = 600;
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

// Disquette de la page du jeu : sauvegarder maintenant, restaurer une sauvegarde, voir la liste des mods.
sealed class FenetreSauvegardes : FenetreSombre
{
    readonly InterfaceSteam steam;
    readonly Reglages reglages;
    readonly int appid;
    readonly string nom;
    readonly Label etat, source;
    readonly ListBox liste;
    readonly Button sauver, restaurer, mods, ouvrir, fermer;
    string exe, dossierJeu;
    Ludusavi.Jeu jeu;      // null : fichiers de partie inconnus, seule la liste des mods est gardée
    bool prete, confirmer;

    public FenetreSauvegardes(InterfaceSteam s, Reglages r, int appid, string nom) : base("Steem — Sauvegardes")
    {
        steam = s; reglages = r; this.appid = appid; this.nom = nom;
        Titre($"Sauvegardes de « {nom} »");
        Paragraphe("Fichiers du jeu sur ce PC (sauvegardes de partie, configs, binds) et liste des mods installés.", Texte);
        source = Paragraphe("", Pale);
        source.Visible = false;
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
        mods = BoutonBas("Liste des mods", Bouton);
        ouvrir = BoutonBas("Dossier", Bouton);
        fermer = BoutonBas("Fermer", Bouton);
        PoserBarre();
        sauver.Click += async (_, _) => await Sauvegarder();
        restaurer.Click += async (_, _) => await Restaurer();
        mods.Click += (_, _) => { if (liste.SelectedItem is Ludusavi.Profil p) Lancer(p.PageMods); };
        ouvrir.Click += (_, _) => { Directory.CreateDirectory(dossierJeu); Lancer(dossierJeu); };
        fermer.Click += (_, _) => Close();
        CancelButton = fermer;
        Boutons(false);
        Load += async (_, _) =>
        {
            // Liste aussi large que la rangée de boutons (cinq boutons : plus large que les textes).
            liste.Width = Math.Max(liste.Width, barre.PreferredSize.Width - barre.Controls[^1].Margin.Right);
            await Preparer();
        };
    }

    void Dessiner(object s, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var p = (Ludusavi.Profil)liste.Items[e.Index];
        bool choisi = (e.State & DrawItemState.Selected) != 0;
        using (var f = new SolidBrush(choisi ? Color.FromArgb(0x3d, 0x6c, 0x8d) : Bouton)) e.Graphics.FillRectangle(f, e.Bounds);
        var r = Rectangle.Inflate(e.Bounds, -10, 0);
        var droite = string.Join(" · ", new[]
        {
            p.Fichiers ? Ludusavi.Taille(p.Octets) : null,
            p.Mods > 0 ? $"{p.Mods} mod{(p.Mods > 1 ? "s" : "")}" : null,
        }.Where(x => x != null));
        TextRenderer.DrawText(e.Graphics, p.Nom, Font, r, Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, droite, Font, r, choisi ? Color.White : Pale, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }

    void Boutons(bool actifs)
    {
        var p = liste.SelectedItem as Ludusavi.Profil;
        sauver.Enabled = actifs && prete;
        restaurer.Enabled = actifs && prete && exe != null && jeu != null && p is { Fichiers: true };
        mods.Enabled = actifs && p != null && p.Mods > 0 && File.Exists(p.PageMods);
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
        etat.Text = "Recherche des fichiers du jeu…";
        string info = null;
        if (exe == null) info = "Ludusavi introuvable (réglage « Ludusavi » de Steem) : seule la liste des mods est gardée.";
        else
        {
            try { jeu = await Ludusavi.TrouverJeu(exe, appid, nom); }
            catch (Exception ex) { info = "Ludusavi a échoué (" + ex.Message + ") : seule la liste des mods est gardée."; }
            if (jeu?.Dossiers != null) info = "Jeu absent de la base de Ludusavi. Dossiers trouvés par Steem :\n" + string.Join("\n", jeu.Dossiers);
            else if (jeu == null && info == null) info = "Fichiers de partie introuvables (absent de la base de Ludusavi et des dossiers habituels) : seule la liste des mods est gardée.";
        }
        if (info != null) { source.Text = info; source.Visible = true; }
        dossierJeu = Ludusavi.DossierJeu(reglages, jeu?.Nom ?? nom);
        prete = true;
        etat.Text = "";
        Remplir();
        Boutons(true);
    }

    async Task Sauvegarder()
    {
        Desarmer();
        Boutons(false);
        etat.ForeColor = Pale; etat.Text = "Sauvegarde en cours…";
        try
        {
            var b = await Ludusavi.SauvegarderTout(reglages, exe, jeu, nom, appid);
            if (b.Vide)
            {
                etat.ForeColor = Rouge;
                etat.Text = b.Erreur != null ? "La sauvegarde a rencontré une erreur : " + b.Erreur : "Rien à sauvegarder : ni fichiers de partie, ni mods trouvés.";
            }
            else
            {
                Remplir(b.Dossier);
                etat.ForeColor = b.Erreur != null ? Rouge : Texte;
                etat.Text = b.Erreur != null ? b.Resume() + ", mais Ludusavi signale une erreur : " + b.Erreur : b.Resume() + ".";
            }
        }
        catch (Exception ex) { etat.ForeColor = Rouge; etat.Text = "Échec de la sauvegarde : " + ex.Message; }
        Boutons(true);
        _ = steam?.PublierSauvegardes();
    }

    async Task Restaurer()
    {
        if (liste.SelectedItem is not Ludusavi.Profil p || !p.Fichiers || jeu == null) return;
        if (!confirmer)
        {
            // Premier clic : on explique, le second clic restaure.
            confirmer = true;
            restaurer.Text = "Confirmer la restauration";
            Couleur(restaurer, Rouge);
            etat.ForeColor = Texte;
            etat.Text = $"Les fichiers actuels du jeu seront remplacés par ceux du {p.Nom}. Une copie de l'état actuel est faite juste avant. Ferme le jeu s'il tourne. Les mods ne sont pas réinstallés : leur liste sert à les retrouver.";
            return;
        }
        Desarmer();
        Boutons(false);
        etat.ForeColor = Pale; etat.Text = "Copie de l'état actuel…";
        try
        {
            var c = await Ludusavi.SauvegarderTout(reglages, exe, jeu, nom, appid, " (avant restauration)");
            if (c.Erreur != null)
            {
                etat.ForeColor = Rouge;
                etat.Text = "La copie de l'état actuel a échoué, restauration annulée : " + c.Erreur;
                Remplir(p.Dossier); Boutons(true);
                return;
            }
            etat.Text = "Restauration en cours…";
            var r = await Ludusavi.Restaurer(exe, jeu.Nom, p.Dossier);
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
        _ = steam?.PublierSauvegardes();
    }

    static void Lancer(string chemin)
    {
        try { Process.Start(new ProcessStartInfo(chemin) { UseShellExecute = true }); } catch { }
    }
}
