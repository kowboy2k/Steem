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
    sealed record Cible(string Nom, Func<Mods.Mod, bool> Present, Func<Mods.Mod, Task> Importer, string ExplicationFixe)
    {
        public bool Mo2 { get; init; }
        public string Explication => !Mo2 ? ExplicationFixe : Nexus.Cle() != null
            ? "Steem télécharge chaque mod depuis Nexus avec ta clé API (Premium) et le dépose dans les téléchargements de Mod Organizer : il n'y a plus qu'à l'installer."
            : "Sans clé API Nexus, les mods passent par des liens nxm:// envoyés à Mod Organizer, un par un. Mod Organizer 2.4 en laisse une partie bloquée à 0 octet.";
    }

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

    static readonly Color Panneau = Color.FromArgb(0x1f, 0x27, 0x31), LigneA = Color.FromArgb(0x24, 0x2c, 0x37),
                          LigneB = Color.FromArgb(0x29, 0x32, 0x3e), Section = Color.FromArgb(0x37, 0x41, 0x50),
                          Vert = Color.FromArgb(0x2e, 0x6b, 0x3a), Lien = Color.FromArgb(0x66, 0xc0, 0xff);
    readonly Dictionary<Mods.Mod, int> rangs = new();
    readonly Label puceImporter, pucePresents, puceSansLien, explication, compte;
    readonly FlowLayoutPanel ligneCle, ligneCompte;
    readonly TextBox saisieCle;
    readonly CancellationTokenSource fin = new();
    Nexus.ErreurNexus arret;
    int courant, totalImport;

    protected override void OnFormClosed(FormClosedEventArgs e) { fin.Cancel(); base.OnFormClosed(e); }

    // Ligne de la clé : saisie si aucune clé, sinon compte connecté (seulement pour Mod Organizer).
    void AfficherCle()
    {
        bool mo2 = Choisie is { Mo2: true };
        bool cle = Nexus.Cle() != null;
        ligneCle.Visible = mo2 && !cle;
        ligneCompte.Visible = mo2 && cle;
        if (mo2 && cle && string.IsNullOrEmpty(compte.Text)) compte.Text = "Nexus : clé API enregistrée · téléchargement direct par Steem";
    }

    async Task EnregistrerCle()
    {
        var cle = saisieCle.Text.Trim();
        if (cle.Length < 10) { Message("Colle la clé API personnelle de ton compte Nexus.", Rouge); return; }
        Message("Vérification de la clé auprès de Nexus…", Pale);
        try
        {
            var c = await Nexus.Valider(cle, fin.Token);
            Nexus.Enregistrer(cle);
            saisieCle.Clear();
            compte.Text = $"Nexus : {c.Nom} ({(c.Premium ? "Premium" : "compte gratuit : téléchargement direct refusé par Nexus")})";
            compte.ForeColor = c.Premium ? Texte : Rouge;
            Message("", Texte);
            AfficherCle();
            Compter();
        }
        catch (Exception ex) { Message("Clé non enregistrée : " + ex.Message, Rouge); }
    }

    async Task TelechargerDirect(string cle, Mods.Mod m, string telechargements, string jeuMo2)
    {
        if (arret != null) { bloques.Add(m.Nom); return; }
        var p = new Progress<(long recus, long total)>(x =>
            Message($"Import {courant}/{totalImport} : {m.Nom} — {Ludusavi.Taille(x.recus)}" + (x.total > 0 ? $" sur {Ludusavi.Taille(x.total)}" : ""), Pale));
        try { await Nexus.Telecharger(cle, m, telechargements, jeuMo2 ?? "", p, fin.Token); }
        catch (Nexus.ErreurNexus e) { bloques.Add(m.Nom); if (e.Arret) arret = e; }
        catch (OperationCanceledException) { bloques.Add(m.Nom); }
        catch (Exception e) { bloques.Add($"{m.Nom} ({e.Message})"); }
    }

    // Mise en page (0.16.3, Kev 24/11 : « plus agréable à lire ») : en-tête jeu / plateforme / infos, liste numérotée
    // à lignes alternées, puis un bloc « Importer » à part avec des pastilles de comptes.
    public FenetreMods(InterfaceSteam s, int appid, string nomJeu, string dossierProfil, int numeroSource) : base("Steem — Mods")
    {
        steam = s; this.appid = appid; dossier = dossierProfil; numero = numeroSource;
        var sources = Mods.Lire(dossierProfil);
        source = numeroSource >= 0 && numeroSource < sources.Count ? sources[numeroSource] : new Mods.Source("?", "", new());

        // En-tête
        Titre($"Mods de « {nomJeu} »");
        var plateforme = source.Titre.StartsWith("Workshop") ? "Workshop Steam"
                       : source.Titre.StartsWith("Mod Organizer") || source.Titre.StartsWith("DMM") ? "NexusMods" : "Dossier du jeu";
        var outil = source.Titre.Contains(" — ") ? source.Titre[(source.Titre.IndexOf(" — ") + 3)..] : null;
        var gestionnaire = source.Titre.StartsWith("Mod Organizer") ? "Mod Organizer 2" : source.Titre.StartsWith("DMM") ? "DMM" : null;
        var sousTitre = Ajouter(new Label
        {
            Text = plateforme + (outil != null ? " · " + outil : "") + (gestionnaire != null ? $"  ({gestionnaire})" : ""),
            Font = new Font("Segoe UI Semibold", 11f), ForeColor = Accent, AutoSize = true, Margin = new Padding(0, 0, 0, 2),
        });
        var profil = Regex.Match(source.Detail ?? "", "Profil « (.+?) »").Groups[1].Value;
        int actifs = source.Mods.Count(m => m.Actif);
        var infos = new List<string>();
        if (profil.Length > 0) infos.Add($"Profil « {profil} »");
        infos.Add(actifs == source.Mods.Count ? $"{source.Mods.Count} mods" : $"{actifs} actifs sur {source.Mods.Count}");
        infos.Add($"sauvegarde du {Path.GetFileName(dossierProfil)}");
        Ajouter(new Label
        {
            Text = string.Join("   ·   ", infos), ForeColor = Pale, AutoSize = true, Font = new Font("Segoe UI", 9f),
            Margin = new Padding(0, 0, 0, 10),
        });

        // Liste
        liste = Ajouter(new ListBox
        {
            Size = new Size(Largeur, 13 * 28), BorderStyle = BorderStyle.None, BackColor = LigneA, ForeColor = Texte,
            DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 28, IntegralHeight = false, Margin = new Padding(0, 0, 0, 2),
            SelectionMode = SelectionMode.None,
        });
        string section = null;
        int rang = 0;
        foreach (var m in source.Mods)
        {
            if (m.Section != null && m.Section != section) liste.Items.Add(m.Section);
            section = m.Section;
            rangs[m] = ++rang;
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
        Ajouter(new Label
        {
            Text = "Un clic sur un nom en bleu ouvre sa page (adresse connue au moment de la sauvegarde). Grisé : mod désactivé.",
            ForeColor = Pale, AutoSize = true, MaximumSize = new Size(Largeur, 0), Font = new Font("Segoe UI", 8.5f),
            Margin = new Padding(0, 0, 0, 12),
        });

        // Bloc « Importer »
        var bloc = Ajouter(new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Panneau,
            Padding = new Padding(14, 12, 14, 12), Margin = new Padding(0), MinimumSize = new Size(Largeur, 0),
        });
        bloc.Controls.Add(new Label
        {
            Text = "IMPORTER DANS UN GESTIONNAIRE DE MODS", ForeColor = Pale, AutoSize = true,
            Font = new Font("Segoe UI Semibold", 8.5f), Margin = new Padding(0, 0, 0, 6),
        });
        cibles = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Bouton, ForeColor = Color.White,
            Width = Largeur - 28, Margin = new Padding(0, 0, 0, 10),
        };
        bloc.Controls.Add(cibles);

        // Clé API Nexus (Mod Organizer) : saisie par l'utilisateur, gardée dans le Gestionnaire d'identification de Windows.
        ligneCle = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10), BackColor = Panneau, Visible = false };
        ligneCle.Controls.Add(new Label { Text = "Clé API Nexus :", AutoSize = true, ForeColor = Texte, Margin = new Padding(0, 5, 8, 0) });
        saisieCle = new TextBox { Width = 250, UseSystemPasswordChar = true, BackColor = Bouton, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 1, 8, 0) };
        ligneCle.Controls.Add(saisieCle);
        var garder = new Button { Text = "Enregistrer", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Bouton, ForeColor = Color.White, Margin = new Padding(0, 0, 8, 0), UseVisualStyleBackColor = false };
        garder.FlatAppearance.BorderSize = 0;
        garder.Click += async (_, _) => await EnregistrerCle();
        ligneCle.Controls.Add(garder);
        var ou = new LinkLabel { Text = "Où la trouver ?", AutoSize = true, LinkColor = Accent, ActiveLinkColor = Accent, Margin = new Padding(0, 5, 0, 0) };
        ou.LinkClicked += (_, _) => Navigateur.Ouvrir("https://next.nexusmods.com/settings/api-keys");
        ligneCle.Controls.Add(ou);
        bloc.Controls.Add(ligneCle);
        ligneCompte = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10), BackColor = Panneau, Visible = false };
        compte = new Label { AutoSize = true, ForeColor = Texte, Margin = new Padding(0, 0, 10, 0) };
        ligneCompte.Controls.Add(compte);
        var changer = new LinkLabel { Text = "Changer la clé", AutoSize = true, LinkColor = Accent, ActiveLinkColor = Accent, Margin = new Padding(0) };
        changer.LinkClicked += (_, _) => { ligneCompte.Visible = false; ligneCle.Visible = true; saisieCle.Focus(); };
        ligneCompte.Controls.Add(changer);
        bloc.Controls.Add(ligneCompte);

        var puces = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8), BackColor = Panneau };
        Label Puce(Color fond) { var l = new Label { AutoSize = true, BackColor = fond, ForeColor = Color.White, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 0, 8, 0) }; puces.Controls.Add(l); return l; }
        puceImporter = Puce(Accent);
        pucePresents = Puce(Vert);
        puceSansLien = Puce(Bouton);
        bloc.Controls.Add(puces);
        explication = new Label { ForeColor = Pale, AutoSize = true, MaximumSize = new Size(Largeur - 28, 0), Font = new Font("Segoe UI", 9f), Margin = new Padding(0) };
        bloc.Controls.Add(explication);
        etat = new Label { ForeColor = Texte, AutoSize = true, MaximumSize = new Size(Largeur - 28, 0), Margin = new Padding(0, 8, 0, 0), Visible = false };
        bloc.Controls.Add(etat);

        importer = BoutonBas("Importer", Accent);
        page = BoutonBas("Page complète", Bouton);
        fermer = BoutonBas("Fermer", Bouton);
        PoserBarre();
        importer.Click += async (_, _) => await Importer();
        page.Click += (_, _) => Navigateur.Ouvrir(new Uri(Path.Combine(dossier, Mods.FichierPage)).AbsoluteUri + "#s" + numero);
        fermer.Click += (_, _) => Close();
        CancelButton = fermer;
        cibles.SelectedIndexChanged += (_, _) => { Desarmer(); Compter(); };
        Load += (_, _) => ChercherCibles();
    }

    void Message(string texte, Color couleur)
    {
        etat.Text = texte;
        etat.ForeColor = couleur;
        etat.Visible = !string.IsNullOrEmpty(texte);
    }

    void Dessiner(object s, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var r = Rectangle.Inflate(e.Bounds, -12, 0);
        if (liste.Items[e.Index] is string sec)
        {
            using (var f = new SolidBrush(Section)) e.Graphics.FillRectangle(f, e.Bounds);
            using var gras = new Font("Segoe UI Semibold", 9.5f);
            TextRenderer.DrawText(e.Graphics, sec.ToUpperInvariant(), gras, r, Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            return;
        }
        var m = (Mods.Mod)liste.Items[e.Index];
        int rang = rangs.GetValueOrDefault(m);
        using (var f = new SolidBrush(rang % 2 == 0 ? LigneB : LigneA)) e.Graphics.FillRectangle(f, e.Bounds);
        var couleur = !m.Actif ? Color.FromArgb(0x5e, 0x68, 0x73) : m.Lien != null ? Lien : Color.White;
        using var petite = new Font("Segoe UI", 8.5f);
        TextRenderer.DrawText(e.Graphics, rang.ToString(), petite, new Rectangle(r.X, r.Y, 36, r.Height), Color.FromArgb(0x6b, 0x75, 0x80),
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        TextRenderer.DrawText(e.Graphics, m.Nom, Font, new Rectangle(r.X + 48, r.Y, r.Width - 48 - 110, r.Height), couleur,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, m.Version, petite, r, !m.Actif ? Color.FromArgb(0x5e, 0x68, 0x73) : Pale,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis);
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
                    var (domaine, presents, jeuMo2) = ModsMo2(Path.Combine(baseMo2, "mods"));
                    if (domaine == null || !domaines.Contains(domaine, StringComparer.OrdinalIgnoreCase)) continue;
                    var exe = Path.Combine(instance, "ModOrganizer.exe");
                    if (!File.Exists(exe)) continue;
                    var telechargements = Mods.ValeurMo2(ini.GetValueOrDefault("download_directory"));
                    if (string.IsNullOrWhiteSpace(telechargements)) telechargements = Path.Combine(baseMo2, "downloads");
                    lesCibles.Add(new Cible($"Mod Organizer 2 — {Path.GetFileName(instance.TrimEnd('\\'))}",
                        m => presents.Contains(m.ModId),
                        // Clé API Nexus enregistrée : Steem télécharge lui-même (0.16.5) ; sinon liens nxm:// à Mod Organizer.
                        m => Nexus.Cle() is { } cle ? TelechargerDirect(cle, m, telechargements, jeuMo2) : EnvoyerMo2(exe, telechargements, m),
                        null) { Mo2 = true });
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
            puceImporter.Visible = pucePresents.Visible = puceSansLien.Visible = false;
            explication.Text = source.Mods.Any(m => m.Nxm != null || m.Workshop != null)
                ? "Aucun gestionnaire de mods installé pour ce jeu."
                : "Mods sans lien de téléchargement : à retrouver par leur nom.";
        }
    }

    // Mods déjà dans une instance de Mod Organizer : numéros Nexus, et le domaine Nexus de l'instance.
    static (string domaine, HashSet<long> presents, string jeuMo2) ModsMo2(string dossierMods)
    {
        string domaine = null, jeuMo2 = null;
        var presents = new HashSet<long>();
        if (!Directory.Exists(dossierMods)) return (null, presents, null);
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
                    jeuMo2 = g;
                }
            }
            catch { }
        }
        return (domaine, presents, jeuMo2);
    }

    // Mod Organizer 2.4 bloque à 0 octet un téléchargement reçu pendant qu'un autre est en cours (essais du 24/11 :
    // seul = OK, deux à 0,7 s = le second reste « .unfinished » vide). Un mod à la fois : on attend son archive finie
    // (nom contenant « -<modid>- », sans .unfinished), ou on passe au suivant s'il reste vide 45 s (10 min au plus).
    readonly List<string> bloques = new();

    async Task EnvoyerMo2(string exe, string telechargements, Mods.Mod m)
    {
        var debut = DateTime.Now.AddSeconds(-2);
        await Lancer(exe, m.Nxm);
        var motif = $"-{m.ModId}-";
        var limite = DateTime.Now.AddMinutes(10);
        DateTime vide = DateTime.Now;
        while (DateTime.Now < limite && !IsDisposed)
        {
            await Task.Delay(1000);
            FileInfo[] f;
            try { f = new DirectoryInfo(telechargements).GetFiles($"*{motif}*").Where(x => x.LastWriteTime >= debut && !x.Name.EndsWith(".meta")).ToArray(); }
            catch { continue; }
            if (f.Any(x => !x.Name.EndsWith(".unfinished"))) return;                    // archive finie
            if (f.Any(x => x.Length > 0)) { vide = DateTime.Now; continue; }             // en cours
            if (DateTime.Now - vide > TimeSpan.FromSeconds(45)) break;                   // bloqué ou absent
        }
        bloques.Add(m.Nom);
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
        int presents = avecLien - a.Count, sansLien = source.Mods.Count - avecLien;
        puceImporter.Text = $"{a.Count} à importer";
        pucePresents.Text = $"{presents} déjà présent{(presents > 1 ? "s" : "")}";
        pucePresents.Visible = presents > 0;
        puceSansLien.Text = $"{sansLien} sans lien";
        puceSansLien.Visible = sansLien > 0;
        explication.Text = c.Explication;
        Message("", Texte);
        AfficherCle();
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
            Message($"Envoyer {a.Count} mod{(a.Count > 1 ? "s" : "")} à « {c.Nom} » ? Un second clic lance l'envoi.", Texte);
            return;
        }
        Desarmer();
        enCours = true;
        importer.Enabled = cibles.Enabled = false;
        int n = 0;
        bloques.Clear();
        arret = null;
        totalImport = a.Count;
        foreach (var m in a)
        {
            if (IsDisposed) return;
            courant = ++n;
            Message($"Import {n}/{a.Count} : {m.Nom}", Pale);
            await c.Importer(m);
        }
        if (IsDisposed) return;
        enCours = false;
        int ok = a.Count - bloques.Count;
        if (arret != null)
            Message($"✓ {ok} téléchargé{(ok > 1 ? "s" : "")} · ⚠ arrêt : {arret.Message} ({bloques.Count} non téléchargé{(bloques.Count > 1 ? "s" : "")})", Rouge);
        else if (bloques.Count == 0)
            Message($"✓ {ok} mod{(ok > 1 ? "s" : "")} envoyé{(ok > 1 ? "s" : "")} à « {c.Nom} ».", Texte);
        else
            Message($"✓ {ok} téléchargé{(ok > 1 ? "s" : "")} · ⚠ {bloques.Count} sans téléchargement (à relancer dans Mod Organizer) : "
                + string.Join(", ", bloques.Take(8)) + (bloques.Count > 8 ? "…" : ""), Rouge);
        cibles.Enabled = true;
    }
}
