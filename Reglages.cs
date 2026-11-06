using System.Text.Json;

namespace Steem;

sealed class Reglages
{
    public bool Epingle { get; set; }
    public bool SteamReduit { get; set; }          // Steam rétréci pour le côte à côte
    public int ReductionSteam { get; set; }        // de combien (px réels), rendu à la sortie
    public bool Gouttiere { get; set; } = true;         // épinglé : gouttière pleine hauteur entre Steam et la liste, au lieu de la languette
    public int Largeur { get; set; }               // pixels réels, reprise de la liste de contacts
    public int MargeHaute { get; set; } = 40;      // laisse libres les boutons de fenêtre de Steam (à 100 %)
    public int BordSurvol { get; set; } = 3;        // bande du bord droit de Steam qui déplie au survol (tout au bord)
    public int DelaiSurvolMs { get; set; } = 500;
    public int DelaiRepliMs { get; set; } = 450;
    public int DelaiMessageMs { get; set; } = 8000;
    public string CouleurLanguette { get; set; } = "35383c"; // fond de la languette, au choix dans le panneau
    public bool AfficherLanguette { get; set; } = true;
    public bool RegrouperChats { get; set; } = true;
    public bool StatutSiOuvert { get; set; }       // icône de statut Steam aussi quand le volet est ouvert
    public float OmbreTaille { get; set; } = 4.5f;  // ombre de la languette, px à 100 %
    public float OmbreDx { get; set; } = 2;
    public float OmbreDy { get; set; } = 1.5f;
    public int OmbreOpacite { get; set; } = 6;      // alpha de chaque couche (0-255)
    public string[] TitresContacts { get; set; } =
        ["Liste de contacts", "Friends List", "Freundesliste", "Lista de amigos"];

    static string Chemin => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Steem", "reglages.json");

    public static Reglages Charger()
    {
        try { return JsonSerializer.Deserialize<Reglages>(File.ReadAllText(Chemin)) ?? new(); }
        catch { return new(); }
    }

    public void Enregistrer()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Chemin));
            File.WriteAllText(Chemin, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
