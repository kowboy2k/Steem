using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace Steem;

// Téléchargement direct depuis Nexus Mods (0.16.5, Kev 24/11) : Mod Organizer 2.4 bloque une partie des téléchargements
// envoyés par liens nxm:// (0 octet) alors que les serveurs Nexus répondent. Avec la clé API personnelle de l'utilisateur
// (compte Premium), Steem demande le lien de téléchargement et dépose l'archive + sa fiche .meta dans le dossier
// « downloads » de l'instance : Mod Organizer la voit comme un téléchargement terminé, même s'il était fermé.
// La clé est collée par l'utilisateur et rangée dans le Gestionnaire d'identification de Windows (jamais dans un fichier).
static class Nexus
{
    const string Cible = "Steem:NexusAPI";

    // ---- Clé API : Gestionnaire d'identification de Windows ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CREDENTIAL
    {
        public int Flags, Type;
        public string TargetName, Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist, AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias, UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredWriteW(ref CREDENTIAL c, int flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredReadW(string target, int type, int flags, out IntPtr c);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredDeleteW(string target, int type, int flags);
    [DllImport("advapi32.dll")] static extern void CredFree(IntPtr c);

    public static string Cle()
    {
        if (!CredReadW(Cible, 1, 0, out var p)) return null;
        try
        {
            var c = Marshal.PtrToStructure<CREDENTIAL>(p);
            return c.CredentialBlobSize > 0 ? Marshal.PtrToStringUni(c.CredentialBlob, c.CredentialBlobSize / 2) : null;
        }
        finally { CredFree(p); }
    }

    public static void Enregistrer(string cle)
    {
        var octets = Encoding.Unicode.GetBytes(cle);
        var blob = Marshal.AllocHGlobal(octets.Length);
        try
        {
            Marshal.Copy(octets, 0, blob, octets.Length);
            var c = new CREDENTIAL
            {
                Type = 1, TargetName = Cible, UserName = "Steem", CredentialBlobSize = octets.Length, CredentialBlob = blob,
                Persist = 2,   // CRED_PERSIST_LOCAL_MACHINE
            };
            if (!CredWriteW(ref c, 0)) throw new InvalidOperationException("Gestionnaire d'identification : " + Marshal.GetLastWin32Error());
        }
        finally { Marshal.FreeHGlobal(blob); }
    }

    public static void Oublier() => CredDeleteW(Cible, 1, 0);

    // ---- API ----

    static readonly HttpClient http = Client();

    static HttpClient Client()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        var v = typeof(Nexus).Assembly.GetName().Version?.ToString(3) ?? "0";
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"Steem/{v} (Windows)");
        h.DefaultRequestHeaders.Add("Application-Name", "Steem");
        h.DefaultRequestHeaders.Add("Application-Version", v);
        return h;
    }

    static async Task<JsonNode> Api(string chemin, string cle, CancellationToken fin)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.nexusmods.com/v1/" + chemin);
        req.Headers.Add("apikey", cle);
        using var rep = await http.SendAsync(req, fin);
        var texte = await rep.Content.ReadAsStringAsync(fin);
        if (rep.StatusCode == HttpStatusCode.Unauthorized) throw new ErreurNexus("Clé API refusée par Nexus.", true);
        if (rep.StatusCode == HttpStatusCode.Forbidden) throw new ErreurNexus("Nexus refuse le lien direct (compte non Premium ?).", true);
        if ((int)rep.StatusCode == 429) throw new ErreurNexus("Limite de requêtes Nexus atteinte : réessayer dans une heure.", true);
        if (!rep.IsSuccessStatusCode) throw new ErreurNexus($"Nexus : erreur {(int)rep.StatusCode}.", false);
        return JsonNode.Parse(texte);
    }

    public sealed class ErreurNexus(string message, bool arret) : Exception(message)
    {
        public bool Arret { get; } = arret;   // vrai : inutile de continuer avec les mods suivants
    }

    public sealed record Compte(string Nom, bool Premium);

    public static async Task<Compte> Valider(string cle, CancellationToken fin = default)
    {
        var j = await Api("users/validate.json", cle, fin);
        return new Compte((string)j?["name"] ?? "?", (bool?)j?["is_premium"] ?? false);
    }

    // Télécharge un fichier Nexus dans « telechargements » + fiche .meta pour Mod Organizer. Renvoie le nom de l'archive
    // (ou null si elle était déjà là). progression : octets reçus / total.
    public static async Task<string> Telecharger(string cle, Mods.Mod m, string telechargements, string jeuMo2,
                                                 IProgress<(long recus, long total)> progression, CancellationToken fin)
    {
        var infos = await Api($"games/{m.Domaine}/mods/{m.ModId}/files/{m.Fichier}.json", cle, fin);
        var nomFichier = (string)infos?["file_name"];
        if (string.IsNullOrWhiteSpace(nomFichier)) throw new ErreurNexus("Nexus ne donne pas le nom du fichier.", false);
        foreach (var c in Path.GetInvalidFileNameChars()) nomFichier = nomFichier.Replace(c, '_');
        var final = Path.Combine(telechargements, nomFichier);
        if (File.Exists(final)) return null;

        var liens = (await Api($"games/{m.Domaine}/mods/{m.ModId}/files/{m.Fichier}/download_link.json", cle, fin))?.AsArray();
        var uri = liens?.Select(l => (string)l?["URI"]).FirstOrDefault(u => !string.IsNullOrEmpty(u));
        if (uri == null) throw new ErreurNexus("Nexus ne donne pas de lien de téléchargement.", false);

        Directory.CreateDirectory(telechargements);
        var partiel = final + ".steem";
        try
        {
            using (var rep = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, fin))
            {
                rep.EnsureSuccessStatusCode();
                long total = rep.Content.Headers.ContentLength ?? 0, recus = 0;
                await using var source = await rep.Content.ReadAsStreamAsync(fin);
                await using var dest = File.Create(partiel);
                var tampon = new byte[1 << 16];
                int n;
                var dernier = DateTime.MinValue;
                while ((n = await source.ReadAsync(tampon, fin)) > 0)
                {
                    await dest.WriteAsync(tampon.AsMemory(0, n), fin);
                    recus += n;
                    if (DateTime.Now - dernier > TimeSpan.FromMilliseconds(250)) { progression?.Report((recus, total)); dernier = DateTime.Now; }
                }
                progression?.Report((recus, total));
            }
            File.Move(partiel, final);
        }
        catch { try { File.Delete(partiel); } catch { } throw; }

        // Fiche lue par Mod Organizer (mêmes clés que celles qu'il écrit lui-même).
        var meta = new StringBuilder()
            .AppendLine("[General]")
            .AppendLine($"gameName={jeuMo2}")
            .AppendLine($"modID={m.ModId}")
            .AppendLine($"fileID={m.Fichier}")
            .AppendLine($"url=\"{uri}\"")
            .AppendLine($"name={(string)infos["name"]}")
            .AppendLine("description=")
            .AppendLine($"modName={m.Nom}")
            .AppendLine($"version={(string)infos["version"] ?? (string)infos["mod_version"] ?? m.Version}")
            .AppendLine("newestVersion=")
            .AppendLine($"fileCategory={(int?)infos["category_id"] ?? 0}")
            .AppendLine("repository=Nexus")
            .AppendLine("installed=false")
            .AppendLine("uninstalled=false")
            .AppendLine("paused=false")
            .AppendLine("removed=false");
        File.WriteAllText(final + ".meta", meta.ToString(), new UTF8Encoding(false));
        return nomFichier;
    }
}
