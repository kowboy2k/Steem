using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Steem;

// Navigateur par défaut, adresse complète (le # d'une page locale passe ; ShellExecute le perd sur un fichier).
static class Navigateur
{
    public static void Ouvrir(string adresse)
    {
        try
        {
            var choix = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice", "ProgId", null) as string;
            var commande = choix == null ? null
                : Microsoft.Win32.Registry.GetValue($@"HKEY_CLASSES_ROOT\{choix}\shell\open\command", "", null) as string;
            var m = commande == null ? null : Regex.Match(commande, "^\"([^\"]+)\"");
            if (m is { Success: true } && File.Exists(m.Groups[1].Value))
            {
                Process.Start(new ProcessStartInfo(m.Groups[1].Value) { ArgumentList = { adresse }, UseShellExecute = false });
                return;
            }
        }
        catch { }
        try { Process.Start(new ProcessStartInfo(adresse) { UseShellExecute = true }); } catch { }
    }
}

// Une source de la liste des mods (clic dans l'infobulle de la disquette, 0.16.1, Kev 24/11) : la liste, chaque nom ouvre
// sa dernière adresse connue au moment de la sauvegarde ; puis import dans un gestionnaire installé (Steam pour le
// Workshop, Mod Organizer 2 ou le gestionnaire des liens nxm:// pour Nexus). Seuls les mods absents sont envoyés.
sealed class FenetreMods : FenetreSombre
{
    sealed record Cible(string Nom, Func<Mods.Mod, bool> Present, Func<Mods.Mod, Task> Importer, string Explication);

    readonly InterfaceSteam steam;
    readonly int appid;
    readonly string dossier;
    readonly Mods.Source source;
    readonly ListBox liste;
    readonly ComboBox cibles;
    readonly Label etat;
    readonly Button importer, page, fermer;
    readonly List<Cible> lesCibles = new();
    readonly int numero;
    bool confirmer, enCours;

    public FenetreMods(InterfaceSteam s, int appid, string nomJeu, string dossierProfil, int numeroSource) : base("Steem — Mods")
    {
        steam = s; this.appid = appid; dossier = dossierProfil; numero = numeroSource;
        var sources = Mods.Lire(dossierProfil);
        source = numeroSource >= 0 && numeroSource < sources.Count ? sources[numeroSource] : new Mods.Source("?", "", new());
        Titre($"Mods de « {nomJeu} » — {source.Titre}");
        Paragraphe($"{source.Detail}\nListe de la sauvegarde « {Path.GetFileName(dossierProfil)} » · {source.Mods.Count(m => m.Actif)} actifs sur {source.Mods.Count}. Un clic sur un nom ouvre sa page.", Pale);
        liste = Ajouter(new ListBox
        {
            Size = new Size(Largeur, 14 * 26), BorderStyle = BorderStyle.None, BackColor = Bouton, ForeColor = Texte,
            DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 26, IntegralHeight = false, Margin = new Padding(0, 6, 0, 6),
            SelectionMode = SelectionMode.None,
        });
        string section = null;
        foreach (var m in source.Mods)
        {
            if (m.Section != null && m.Section != section) liste.Items.Add(m.Section);
            section = m.Section;
            liste.Items.Add(m);
        }
        liste.DrawItem += Dessiner;
        liste.MouseMove += (_, e) =>
        {
            int i = liste.IndexFromPoint(e.Location);
            liste.Cursor = i >= 0 && liste.Items[i] is Mods.Mod { Lien: not null } ? Cursors.Hand : Cursors.Default;
        };
        liste.MouseClick += (_, e) =>
        {
            int i = liste.IndexFromPoint(e.Location);
            if (i >= 0 && liste.Items[i] is Mods.Mod { Lien: { } lien }) Navigateur.Ouvrir(lien);
        };

        var ligne = Ajouter(new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) });
        ligne.Controls.Add(new Label { Text = "Importer dans :", AutoSize = true, ForeColor = Texte, Margin = new Padding(0, 6, 8, 0) });
        cibles = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Bouton, ForeColor = Color.White,
            Width = 380, Margin = new Padding(0, 2, 0, 0),
        };
        ligne.Controls.Add(cibles);
        etat = Paragraphe("", Pale, 40);
        importer = BoutonBas("Importer", Accent);
        page = BoutonBas("Page complète", Bouton);
        fermer = BoutonBas("Fermer", Bouton);
        PoserBarre();
        importer.Click += async (_, _) => await Importer();
        page.Click += (_, _) => Navigateur.Ouvrir(new Uri(Path.Combine(dossier, Mods.FichierPage)).AbsoluteUri + "#s" + numero);
        fermer.Click += (_, _) => Close();
        CancelButton = fermer;
        cibles.SelectedIndexChanged += (_, _) => { Desarmer(); Compter(); };
        Load += (_, _) =>
        {
            liste.Width = Math.Max(liste.Width, barre.PreferredSize.Width - barre.Controls[^1].Margin.Right);
            ChercherCibles();
        };
    }

    void Dessiner(object s, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var r = Rectangle.Inflate(e.Bounds, -10, 0);
        if (liste.Items[e.Index] is string sec)
        {
            using (var f = new SolidBrush(Color.FromArgb(0x37, 0x41, 0x50))) e.Graphics.FillRectangle(f, e.Bounds);
            using var gras = new Font(Font, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, sec, gras, r, Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            return;
        }
        var m = (Mods.Mod)liste.Items[e.Index];
        using (var f = new SolidBrush(Bouton)) e.Graphics.FillRectangle(f, e.Bounds);
        var couleur = !m.Actif ? Color.FromArgb(0x6b, 0x75, 0x80) : m.Lien != null ? Color.FromArgb(0x66, 0xc0, 0xff) : Color.White;
        TextRenderer.DrawText(e.Graphics, m.Nom, Font, new Rectangle(r.X, r.Y, r.Width - 120, r.Height), couleur,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, m.Version, Font, r, Pale, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis);
    }

    void ChercherCibles()
    {
        // Workshop : abonnement par Steam (Steam ignore ceux déjà abonnés).
        if (source.Mods.Any(m => m.Workshop != null) && steam != null)
            lesCibles.Add(new Cible("Steam — abonnements du Workshop", _ => false,
                m => steam.Evaluer($"SteamClient.Apps.SubscribeWorkshopItem({appid}, '{m.Workshop}', true)"),
                "Steam s'abonne aux objets du Workshop et les télécharge ; ceux déjà abonnés ne changent pas."));

        var domaines = source.Mods.Where(m => m.Nxm != null).Select(m => m.Domaine).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (domaines.Count > 0)
        {
            // Mod Organizer 2 : instances du même jeu Nexus ; un lien nxm:// passé à ModOrganizer.exe rejoint ses téléchargements.
            foreach (var instance in Mods.InstancesMo2())
            {
                try
                {
                    var ini = Mods.LireIni(Path.Combine(instance, "ModOrganizer.ini"));
                    var baseMo2 = Mods.ValeurMo2(ini.GetValueOrDefault("base_directory"));
                    if (string.IsNullOrWhiteSpace(baseMo2)) baseMo2 = instance;
                    var (domaine, presents) = ModsMo2(Path.Combine(baseMo2, "mods"));
                    if (domaine == null || !domaines.Contains(domaine, StringComparer.OrdinalIgnoreCase)) continue;
                    var exe = Path.Combine(instance, "ModOrganizer.exe");
                    if (!File.Exists(exe)) continue;
                    lesCibles.Add(new Cible($"Mod Organizer 2 — {Path.GetFileName(instance.TrimEnd('\\'))}",
                        m => presents.Contains(m.ModId), m => Lancer(exe, m.Nxm),
                        "Mod Organizer les ajoute à ses téléchargements (compte Nexus Premium : sans passer par le site), puis on les installe dans Mod Organizer."));
                }
                catch { }
            }
            // Gestionnaire déclaré pour les liens nxm:// (ex. DMM).
            var commande = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Classes\nxm\shell\open\command", "", null) as string;
            var gest = commande == null ? null : Regex.Match(commande, "^\"([^\"]+)\"").Groups[1].Value;
            if (!string.IsNullOrEmpty(gest) && File.Exists(gest) && !Path.GetFileName(gest).Equals("nxmhandler.exe", StringComparison.OrdinalIgnoreCase))
            {
                var deja = new HashSet<long>();
                var jeux = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var nl = JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(gest)!, "nexus_links.json")))?.AsObject();
                    foreach (var (_, v) in nl ?? [])
                    {
                        if ((long?)v?["mod_id"] is long id) deja.Add(id);
                        if ((string)v?["game"] is { } g) jeux.Add(g);
                    }
                }
                catch { }
                // Gestionnaire propre à un jeu (DMM : Crimson Desert) : proposé seulement pour ce jeu.
                if (jeux.Count == 0 || domaines.Any(jeux.Contains))
                lesCibles.Add(new Cible($"{Path.GetFileNameWithoutExtension(gest)} — gestionnaire des liens nxm://", m => deja.Contains(m.ModId),
                    m => Lancer(gest, m.Nxm), "Les liens nxm:// sont envoyés au gestionnaire de mods déclaré dans Windows."));
            }
        }

        foreach (var c in lesCibles) cibles.Items.Add(c.Nom);
        if (cibles.Items.Count > 0) cibles.SelectedIndex = 0;
        else
        {
            cibles.Enabled = importer.Enabled = false;
            etat.Text = source.Mods.Any(m => m.Nxm != null || m.Workshop != null)
                ? "Aucun gestionnaire de mods installé pour ce jeu."
                : "Mods sans lien de téléchargement : à retrouver par leur nom.";
        }
    }

    // Mods déjà dans une instance de Mod Organizer : numéros Nexus, et le domaine Nexus de l'instance.
    static (string domaine, HashSet<long> presents) ModsMo2(string dossierMods)
    {
        string domaine = null;
        var presents = new HashSet<long>();
        if (!Directory.Exists(dossierMods)) return (null, presents);
        foreach (var d in Directory.GetDirectories(dossierMods))
        {
            var meta = Path.Combine(d, "meta.ini");
            if (!File.Exists(meta)) continue;
            try
            {
                var m = Mods.LireIni(meta);
                if (!long.TryParse(m.GetValueOrDefault("modid", "0"), out var id) || id <= 0) continue;
                presents.Add(id);
                if (domaine == null)
                {
                    var g = m.GetValueOrDefault("gameName", "");
                    domaine = Mods.DomainesNexus.GetValueOrDefault(g, g.ToLowerInvariant());
                }
            }
            catch { }
        }
        return (domaine, presents);
    }

    static async Task Lancer(string exe, string lien)
    {
        try { using var p = Process.Start(new ProcessStartInfo(exe) { ArgumentList = { lien }, UseShellExecute = false }); } catch { }
        await Task.Delay(700);   // laisse le gestionnaire prendre chaque lien
    }

    Cible Choisie => cibles.SelectedIndex >= 0 ? lesCibles[cibles.SelectedIndex] : null;

    List<Mods.Mod> AImporter(Cible c) => source.Mods
        .Where(m => (c.Nom.StartsWith("Steam") ? m.Workshop != null : m.Nxm != null) && !c.Present(m))
        .GroupBy(m => m.Workshop ?? m.Nxm).Select(g => g.First()).ToList();

    void Compter()
    {
        if (Choisie is not { } c) return;
        var a = AImporter(c);
        int avecLien = source.Mods.Count(m => c.Nom.StartsWith("Steam") ? m.Workshop != null : m.Nxm != null);
        etat.ForeColor = Pale;
        etat.Text = $"{a.Count} mod{(a.Count > 1 ? "s" : "")} à importer"
            + (avecLien > a.Count ? $" ({avecLien - a.Count} déjà présents)" : "")
            + (source.Mods.Count > avecLien ? $" · {source.Mods.Count - avecLien} sans lien de téléchargement" : "") + ".\n" + c.Explication;
        importer.Enabled = a.Count > 0 && !enCours;
    }

    void Desarmer()
    {
        if (!confirmer) return;
        confirmer = false;
        importer.Text = "Importer";
        Couleur(importer, Accent);
    }

    async Task Importer()
    {
        if (Choisie is not { } c) return;
        var a = AImporter(c);
        if (a.Count == 0) return;
        if (!confirmer)
        {
            confirmer = true;
            importer.Text = $"Confirmer : {a.Count} mod{(a.Count > 1 ? "s" : "")}";
            Couleur(importer, Rouge);
            etat.ForeColor = Texte;
            etat.Text = $"Envoyer {a.Count} mod{(a.Count > 1 ? "s" : "")} à « {c.Nom} » ? {c.Explication}";
            return;
        }
        Desarmer();
        enCours = true;
        importer.Enabled = cibles.Enabled = false;
        int n = 0;
        foreach (var m in a)
        {
            if (IsDisposed) return;
            etat.ForeColor = Pale;
            etat.Text = $"Import {++n}/{a.Count} : {m.Nom}";
            await c.Importer(m);
        }
        enCours = false;
        etat.ForeColor = Texte;
        etat.Text = $"✓ {a.Count} mod{(a.Count > 1 ? "s" : "")} envoyé{(a.Count > 1 ? "s" : "")} à « {c.Nom} ». {c.Explication}";
        cibles.Enabled = true;
    }
}
