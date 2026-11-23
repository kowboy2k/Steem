using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Steem;

// Liste des mods d'un jeu, gardée avec chaque sauvegarde (0.16.0, Kev 24/11) : pas les fichiers des mods (trop lourds),
// seulement de quoi les retrouver et les retélécharger. Sources : Workshop Steam, Mod Organizer 2, DMM.
// Écrit mods.json (données) et mods.html (page à ouvrir, liens cliquables) dans le dossier de la sauvegarde.
static class Mods
{
    // Domaine / ModId / Fichier : mod Nexus (lien nxm:// pour le réimporter) ; Workshop : numéro d'objet du Workshop.
    public sealed record Mod(string Nom, string Version, string Lien, bool Actif, string Section,
                             string Domaine = null, long ModId = 0, long Fichier = 0, string Workshop = null)
    {
        public string Nxm => Domaine != null && ModId > 0 && Fichier > 0 ? $"nxm://{Domaine}/mods/{ModId}/files/{Fichier}" : null;
    }
    public sealed record Source(string Titre, string Detail, List<Mod> Mods);

    public const string FichierJson = "mods.json", FichierPage = "mods.html";

    // Relit la liste d'une sauvegarde (fenêtre d'une source, import).
    public static List<Source> Lire(string dossierProfil)
    {
        var res = new List<Source>();
        try
        {
            var j = JsonNode.Parse(File.ReadAllText(Path.Combine(dossierProfil, FichierJson)));
            foreach (var s in j?["sources"]?.AsArray() ?? [])
                res.Add(new Source((string)s["titre"], (string)s["detail"], (s["mods"]?.AsArray() ?? []).Select(m => new Mod(
                    (string)m["nom"], (string)m["version"], (string)m["lien"], (bool?)m["actif"] ?? true, (string)m["section"],
                    (string)m["domaine"], (long?)m["modid"] ?? 0, (long?)m["fichier"] ?? 0, (string)m["workshop"])).ToList()));
        }
        catch { }
        return res;
    }

    // Nombre de mods d'une sauvegarde (0 : pas de liste).
    public static int Compter(string dossierProfil)
    {
        try
        {
            var f = Path.Combine(dossierProfil, FichierJson);
            return File.Exists(f) ? (int?)JsonNode.Parse(File.ReadAllText(f))?["total"] ?? 0 : 0;
        }
        catch { return 0; }
    }

    public static async Task<int> Ecrire(int appid, string nomJeu, string dossierProfil)
    {
        var sources = await Lister(appid);
        int total = sources.Sum(s => s.Mods.Count);
        if (total == 0) return 0;
        Directory.CreateDirectory(dossierProfil);
        var json = new JsonObject
        {
            ["jeu"] = nomJeu, ["appid"] = appid, ["date"] = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"), ["total"] = total,
            ["sources"] = new JsonArray(sources.Select(s => (JsonNode)new JsonObject
            {
                ["titre"] = s.Titre, ["detail"] = s.Detail,
                ["mods"] = new JsonArray(s.Mods.Select(m => (JsonNode)new JsonObject
                {
                    ["nom"] = m.Nom, ["version"] = m.Version, ["lien"] = m.Lien, ["actif"] = m.Actif, ["section"] = m.Section,
                    ["domaine"] = m.Domaine, ["modid"] = m.ModId, ["fichier"] = m.Fichier, ["workshop"] = m.Workshop,
                }).ToArray()),
            }).ToArray()),
        };
        File.WriteAllText(Path.Combine(dossierProfil, FichierJson), json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dossierProfil, FichierPage), Page(nomJeu, sources, total), new UTF8Encoding(false));
        return total;
    }

    public static async Task<List<Source>> Lister(int appid)
    {
        var res = new List<Source>();
        var (bibliotheque, installation) = Installation(appid);
        try { var w = await Workshop(appid, bibliotheque); if (w != null) res.Add(w); } catch { }
        try { res.AddRange(ModOrganizer(appid, installation)); } catch { }
        try { res.AddRange(Dmm(installation)); } catch { }
        try { res.AddRange(DossierDuJeu(installation)); } catch { }
        return res.Where(s => s.Mods.Count > 0).ToList();
    }

    // ---- Mods posés dans le dossier du jeu : dossiers « mods » (1 ou 2 niveaux) et BepInEx\plugins ----

    static IEnumerable<Source> DossierDuJeu(string installation)
    {
        if (installation == null || !Directory.Exists(installation)) yield break;
        var dossiers = new List<string>();
        foreach (var d1 in Directory.GetDirectories(installation))
        {
            if (Path.GetFileName(d1).Equals("mods", StringComparison.OrdinalIgnoreCase)) dossiers.Add(d1);
            try
            {
                foreach (var d2 in Directory.GetDirectories(d1))
                    if (Path.GetFileName(d2).Equals("mods", StringComparison.OrdinalIgnoreCase)) dossiers.Add(d2);
            }
            catch { }
        }
        var bepinex = Path.Combine(installation, "BepInEx", "plugins");
        if (Directory.Exists(bepinex)) dossiers.Add(bepinex);
        foreach (var d in dossiers)
        {
            var mods = Directory.GetDirectories(d).Select(Path.GetFileName)
                .Concat(Directory.GetFiles(d).Select(Path.GetFileName)
                    .Where(f => !Regex.IsMatch(f, @"^(readme|license|changelog).*|\.(txt|md|log)$", RegexOptions.IgnoreCase)))
                .Where(n => !n.StartsWith('.') && !n.StartsWith('_'))
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .Select(n => new Mod(n, "", null, true, null)).ToList();
            yield return new Source($"Dossier du jeu — {Path.GetRelativePath(installation, d)}",
                $"Mods posés à la main dans {d} (sans lien : à retrouver par leur nom).", mods);
        }
    }

    // ---- Steam : bibliothèque et dossier d'installation du jeu ----

    static string Normaliser(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        try { p = Path.GetFullPath(p.Replace('/', '\\')); } catch { }
        return p.TrimEnd('\\').ToLowerInvariant();
    }

    static (string bibliotheque, string installation) Installation(int appid)
    {
        var steam = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (steam == null) return (null, null);
        var biblios = new List<string> { steam };
        try
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf")), "\"path\"\\s+\"([^\"]+)\""))
                biblios.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        catch { }
        foreach (var b in biblios.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var acf = Path.Combine(b, "steamapps", $"appmanifest_{appid}.acf");
            if (!File.Exists(acf)) continue;
            var dossier = Regex.Match(File.ReadAllText(acf), "\"installdir\"\\s+\"([^\"]+)\"").Groups[1].Value;
            return (b, dossier.Length > 0 ? Path.Combine(b, "steamapps", "common", dossier) : null);
        }
        return (null, null);
    }

    // ---- Workshop : abonnements installés, noms donnés par l'API publique de Steam ----

    static async Task<Source> Workshop(int appid, string bibliotheque)
    {
        if (bibliotheque == null) return null;
        var acf = Path.Combine(bibliotheque, "steamapps", "workshop", $"appworkshop_{appid}.acf");
        if (!File.Exists(acf)) return null;
        var texte = File.ReadAllText(acf);
        int debut = texte.IndexOf("\"WorkshopItemsInstalled\"", StringComparison.Ordinal);
        if (debut < 0) return null;
        int fin = texte.IndexOf("\"WorkshopItemDetails\"", debut, StringComparison.Ordinal);
        var bloc = fin > 0 ? texte[debut..fin] : texte[debut..];
        var ids = Regex.Matches(bloc, "^\\s*\"(\\d+)\"\\s*$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).Distinct().ToList();
        if (ids.Count == 0) return null;

        var titres = new Dictionary<string, string>();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            foreach (var paquet in ids.Chunk(100))
            {
                var champs = new List<KeyValuePair<string, string>> { new("itemcount", paquet.Length.ToString()) };
                for (int i = 0; i < paquet.Length; i++) champs.Add(new($"publishedfileids[{i}]", paquet[i]));
                var rep = await http.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", new FormUrlEncodedContent(champs));
                var j = JsonNode.Parse(await rep.Content.ReadAsStringAsync());
                foreach (var d in j?["response"]?["publishedfiledetails"]?.AsArray() ?? [])
                    if ((string)d?["title"] is { Length: > 0 } t) titres[(string)d["publishedfileid"]] = t;
            }
        }
        catch { }   // hors ligne : les numéros suffisent à retrouver les mods

        var mods = ids.Select(id => new Mod(titres.GetValueOrDefault(id, $"Objet {id}"), "",
            $"https://steamcommunity.com/sharedfiles/filedetails/?id={id}", true, null, Workshop: id))
            .OrderBy(m => m.Nom, StringComparer.CurrentCultureIgnoreCase).ToList();
        return new Source("Workshop Steam", "Abonnements installés : Steam les retélécharge tant que l'abonnement est gardé.", mods);
    }

    // ---- Mod Organizer 2 ----

    // Nom de jeu de Mod Organizer → numéro Steam (instances dont le jeu est une copie, ex. « Stock Game » de Wabbajack).
    static readonly Dictionary<string, int> JeuxMo2 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Skyrim Special Edition"] = 489830, ["Skyrim"] = 72850, ["Skyrim VR"] = 611670,
        ["Fallout 4"] = 377160, ["Fallout 4 VR"] = 611660, ["New Vegas"] = 22380, ["Fallout 3"] = 22300,
        ["Oblivion"] = 22330, ["Morrowind"] = 22320, ["Enderal"] = 933480, ["Enderal Special Edition"] = 976620,
        ["Starfield"] = 1716740, ["Cyberpunk 2077"] = 1091500, ["Baldur's Gate 3"] = 1086940,
        ["Stardew Valley"] = 413150, ["The Witcher 3"] = 292030, ["Kingdom Come: Deliverance"] = 379430,
    };

    // Nom court de Mod Organizer (meta.ini) → domaine Nexus.
    internal static readonly Dictionary<string, string> DomainesNexus = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SkyrimSE"] = "skyrimspecialedition", ["Skyrim"] = "skyrim", ["SkyrimVR"] = "skyrimspecialedition",
        ["Fallout4"] = "fallout4", ["Fallout4VR"] = "fallout4", ["FalloutNV"] = "newvegas", ["Fallout3"] = "fallout3",
        ["Oblivion"] = "oblivion", ["Morrowind"] = "morrowind", ["Enderal"] = "enderal", ["EnderalSE"] = "enderalspecialedition",
        ["Starfield"] = "starfield", ["Cyberpunk2077"] = "cyberpunk2077", ["BaldursGate3"] = "baldursgate3",
        ["StardewValley"] = "stardewvalley", ["Witcher3"] = "witcher3",
    };

    internal static Dictionary<string, string> LireIni(string fichier)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in File.ReadLines(fichier))
        {
            int i = l.IndexOf('=');
            if (i > 0 && !l.StartsWith('[')) d.TryAdd(l[..i].Trim(), l[(i + 1)..].Trim());
        }
        return d;
    }

    // @ByteArray(F:\\Jeu) → F:\Jeu
    internal static string ValeurMo2(string v)
    {
        if (v == null) return null;
        if (v.StartsWith("@ByteArray(") && v.EndsWith(")")) v = v[11..^1];
        return v.Replace(@"\\", @"\");
    }

    internal static IEnumerable<string> InstancesMo2()
    {
        var racine = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModOrganizer");
        var vues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Instances portables déclarées pour les liens nxm://
        var nxm = Path.Combine(racine, "nxmhandler.ini");
        if (File.Exists(nxm))
            foreach (var l in File.ReadLines(nxm))
            {
                var m = Regex.Match(l, @"^\d+\\executable=(.+)$");
                if (!m.Success) continue;
                var dossier = Path.GetDirectoryName(m.Groups[1].Value.Replace(@"\\", @"\"));
                if (dossier != null && File.Exists(Path.Combine(dossier, "ModOrganizer.ini")) && vues.Add(dossier)) yield return dossier;
            }
        // Instances globales
        if (Directory.Exists(racine))
            foreach (var d in Directory.GetDirectories(racine))
                if (File.Exists(Path.Combine(d, "ModOrganizer.ini")) && vues.Add(d)) yield return d;
    }

    static IEnumerable<Source> ModOrganizer(int appid, string installation)
    {
        var jeuInstalle = Normaliser(installation);
        foreach (var instance in InstancesMo2())
        {
            Dictionary<string, string> ini;
            try { ini = LireIni(Path.Combine(instance, "ModOrganizer.ini")); } catch { continue; }
            var nomJeu = ini.GetValueOrDefault("gameName", "");
            var chemin = Normaliser(ValeurMo2(ini.GetValueOrDefault("gamePath")));
            bool lie = (jeuInstalle.Length > 0 && chemin == jeuInstalle) || (JeuxMo2.TryGetValue(nomJeu, out var id) && id == appid);
            if (!lie) continue;
            var baseMo2 = ValeurMo2(ini.GetValueOrDefault("base_directory"));
            if (string.IsNullOrWhiteSpace(baseMo2)) baseMo2 = instance;
            var profil = ValeurMo2(ini.GetValueOrDefault("selected_profile")) ?? "Default";
            var liste = Path.Combine(baseMo2, "profiles", profil, "modlist.txt");
            if (!File.Exists(liste)) continue;

            var mods = new List<Mod>();
            string section = null;
            // modlist.txt : le haut du fichier = priorité la plus haute ; à l'envers = l'ordre affiché par Mod Organizer.
            foreach (var ligne in File.ReadAllLines(liste).Reverse())
            {
                if (ligne.Length < 2 || ligne.StartsWith('#')) continue;
                char etat = ligne[0];
                var nom = ligne[1..];
                if (etat == '*') continue;                        // fichiers du jeu ou DLC, pas des mods
                if (nom.EndsWith("_separator")) { section = nom[..^"_separator".Length]; continue; }
                string version = "", lien = null, domaine = null;
                long modid = 0, fichier = 0;
                try
                {
                    var meta = Path.Combine(baseMo2, "mods", nom, "meta.ini");
                    if (File.Exists(meta))
                    {
                        var m = LireIni(meta);
                        version = m.GetValueOrDefault("version", "");
                        var url = m.GetValueOrDefault("url", "");
                        long.TryParse(m.GetValueOrDefault("modid", "0"), out modid);
                        long.TryParse(m.GetValueOrDefault(@"1\fileid", "0"), out fichier);   // [installedFiles] : le fichier Nexus installé
                        if (modid > 0)
                        {
                            var g = m.GetValueOrDefault("gameName", "");
                            domaine = DomainesNexus.GetValueOrDefault(g, g.ToLowerInvariant());
                        }
                        if (url.Length > 0 && m.GetValueOrDefault("hasCustomURL", "false") == "true") lien = url;
                        else if (modid > 0) lien = $"https://www.nexusmods.com/{domaine}/mods/{modid}";
                    }
                }
                catch { }
                mods.Add(new Mod(nom, version, lien, etat == '+', section, domaine, modid, fichier));
            }
            yield return new Source($"Mod Organizer 2 — {Path.GetFileName(instance.TrimEnd('\\'))}",
                $"Profil « {profil} », dans l'ordre de Mod Organizer. Dossier : {instance}", mods);
        }
    }

    // ---- DMM (gestionnaire déclaré pour les liens nxm://, avec config.json + nexus_links.json) ----

    static IEnumerable<Source> Dmm(string installation)
    {
        var jeuInstalle = Normaliser(installation);
        if (jeuInstalle.Length == 0) yield break;
        var commande = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Classes\nxm\shell\open\command", "", null) as string;
        var exe = commande == null ? null : Regex.Match(commande, "^\"([^\"]+)\"").Groups[1].Value;
        if (string.IsNullOrEmpty(exe)) yield break;
        var dossier = Path.GetDirectoryName(exe);
        var config = Path.Combine(dossier!, "config.json");
        if (!File.Exists(config)) yield break;
        JsonNode c;
        try { c = JsonNode.Parse(File.ReadAllText(config)); } catch { yield break; }
        if (Normaliser((string)c?["gamePath"]) != jeuInstalle) yield break;

        var actifs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cle in new[] { "activeBrowserMods", "activeAsiMods" })
            foreach (var x in c[cle]?.AsArray() ?? []) if ((string)x is { } s) actifs.Add(s);
        foreach (var x in c["activeMods"]?.AsArray() ?? [])
            if ((string)x?["fileName"] is { } f) actifs.Add(f.Split('/')[0]);

        var liens = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var nl = JsonNode.Parse(File.ReadAllText(Path.Combine(dossier, "nexus_links.json")))?.AsObject();
            foreach (var (k, v) in nl ?? []) liens[k] = v;
        }
        catch { }
        var noms = new SortedSet<string>(liens.Keys, StringComparer.CurrentCultureIgnoreCase);
        var dossierMods = (string)c["modsPath"];
        if (dossierMods != null && Directory.Exists(dossierMods))
            foreach (var d in Directory.GetDirectories(dossierMods))
            {
                var n = Path.GetFileName(d);
                if (!n.StartsWith('_')) noms.Add(n);
            }
        // DMM écrit un même mod sous plusieurs formes (« Adaptive HUD », « adaptive_hud ») : comparaison sur lettres et chiffres.
        static string Cle(string s) => new((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var actifsCles = actifs.Select(Cle).ToHashSet();
        var vus = new HashSet<string>();
        var mods = new List<Mod>();
        foreach (var n in noms)
        {
            liens.TryGetValue(n, out var l);
            var lien = l != null && (int?)l["mod_id"] is int id and > 0 ? $"https://www.nexusmods.com/{(string)l["game"]}/mods/{id}" : null;
            var nomMod = (string)l?["mod_name"] ?? n;
            if (!vus.Add(lien ?? Cle(nomMod))) continue;   // même mod Nexus sous deux noms
            bool actif = new[] { n, (string)l?["file_name"], nomMod }.Any(x => x != null && actifsCles.Contains(Cle(x)));
            long fichierDmm = (long?)l?["file_id"] ?? 0;
            mods.Add(new Mod(nomMod, (string)l?["version"] ?? "", lien, actif, null,
                lien != null ? (string)l["game"] : null, lien != null ? (long?)l["mod_id"] ?? 0 : 0, fichierDmm));
        }
        yield return new Source("DMM", $"Gestionnaire de mods : {dossier}", mods);
    }

    // ---- Page à ouvrir dans le navigateur (thème sombre) ----

    static string Page(string jeu, List<Source> sources, int total)
    {
        static string H(string s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\"><title>Mods de ").Append(H(jeu)).Append("</title><style>")
          .Append("body{background:#171d25;color:#c7d5e0;font:14px 'Segoe UI',sans-serif;margin:0;padding:24px 32px}")
          .Append("h1{color:#fff;font-size:22px;margin:0 0 4px}h2{color:#fff;font-size:17px;margin:28px 0 2px}")
          .Append(".pale{color:#8f98a0}table{border-collapse:collapse;width:100%;margin-top:10px}")
          .Append("td{padding:5px 10px;border-bottom:1px solid #2a3340}td.n{color:#8f98a0;width:44px;text-align:right}")
          .Append("tr.sec td{background:#2a3340;color:#fff;font-weight:600;padding:7px 10px}tr.off td{opacity:.45}")
          .Append("a{color:#1a9fff;text-decoration:none}a:hover{text-decoration:underline}td.v{color:#8f98a0;white-space:nowrap}")
          .Append("</style></head><body>");
        sb.Append("<h1>Mods de « ").Append(H(jeu)).Append(" »</h1><div class=\"pale\">Liste du ")
          .Append(DateTime.Now.ToString("dd/MM/yyyy HH:mm")).Append(" · ").Append(total).Append(" mods · les mods désactivés sont grisés</div>");
        int numero = 0;
        foreach (var s in sources)
        {
            int actifs = s.Mods.Count(m => m.Actif);
            sb.Append("<h2 id=\"s").Append(numero++).Append("\">").Append(H(s.Titre)).Append("</h2><div class=\"pale\">").Append(H(s.Detail)).Append(" · ")
              .Append(actifs).Append(" actifs sur ").Append(s.Mods.Count).Append("</div><table>");
            string section = null; int n = 0;
            foreach (var m in s.Mods)
            {
                if (m.Section != section && m.Section != null) sb.Append("<tr class=\"sec\"><td colspan=\"4\">").Append(H(m.Section)).Append("</td></tr>");
                section = m.Section;
                // Nom du mod = lien vers sa dernière adresse connue au moment de la sauvegarde (Kev 24/11).
                sb.Append(m.Actif ? "<tr>" : "<tr class=\"off\">").Append("<td class=\"n\">").Append(++n).Append("</td><td>");
                if (m.Lien != null) sb.Append("<a href=\"").Append(H(m.Lien)).Append("\" target=\"_blank\">").Append(H(m.Nom)).Append("</a>");
                else sb.Append(H(m.Nom));
                sb.Append("</td><td class=\"v\">").Append(H(m.Version)).Append("</td><td class=\"v\">")
                  .Append(m.Lien == null ? "" : m.Lien.Contains("nexusmods") ? "Nexus" : m.Lien.Contains("steamcommunity") ? "Workshop" : "Web")
                  .Append("</td></tr>");
            }
            sb.Append("</table>");
        }
        return sb.Append("</body></html>").ToString();
    }
}
