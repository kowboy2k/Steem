using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Steem;

// Statut Steam du compte (En ligne, Absent(e), Invisible, Hors ligne), lu dans le localconfig.vdf le plus récent :
// Steam y garde « FriendStoreLocalPrefs_<compte> » → {"ePersonaState":n}. Lecture seule, relu seulement quand le fichier change.
static class StatutSteam
{
    public const int HorsLigne = 0, EnLigne = 1, NePasDeranger = 2, Absent = 3, Invisible = 7; // Ne pas déranger : « DoNotDisturb » "1"

    static string fichier;
    static DateTime vu;
    static int dernier = EnLigne;
    static long prochaineRecherche;

    public static int Lire()
    {
        try
        {
            long t = Environment.TickCount64;
            if (fichier == null || t >= prochaineRecherche) { fichier = Chercher(); prochaineRecherche = t + 60000; }
            if (fichier == null) return dernier;
            var m = File.GetLastWriteTimeUtc(fichier);
            if (m == vu) return dernier;
            using var f = new FileStream(fichier, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(f);
            var texte = r.ReadToEnd();
            var c = Regex.Match(texte, @"ePersonaState\\"":(\d+)");
            bool nePasDeranger = Regex.IsMatch(texte, @"""DoNotDisturb""\s+""1""");
            vu = m;
            if (c.Success) dernier = int.Parse(c.Groups[1].Value) switch
            {
                0 => HorsLigne,
                7 => Invisible,
                2 => NePasDeranger, // Occupé(e)
                _ when nePasDeranger => NePasDeranger,
                3 or 4 => Absent, // Absent(e), Inactif
                _ => EnLigne,
            };
        }
        catch { }
        return dernier;
    }

    // Le compte utilisé est celui dont le localconfig.vdf a été écrit en dernier.
    static string Chercher()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var racine = (k?.GetValue("SteamPath") as string)?.Replace('/', '\\');
        var dossier = racine == null ? null : Path.Combine(racine, "userdata");
        if (dossier == null || !Directory.Exists(dossier)) return null;
        return Directory.GetDirectories(dossier)
            .Select(d => Path.Combine(d, "config", "localconfig.vdf"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
