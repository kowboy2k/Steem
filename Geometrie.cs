namespace Steem;

// Calculs de position du volet, sans aucun appel à Windows : Volet s'en sert à chaque tour,
// et `--essai-ecrans` les rejoue sur des écrans fictifs pour vérifier que rien ne sort de l'écran.
static class Geometrie
{
    public static int LargeurLanguette(float k) => (int)(19.2f * k);
    public static int HauteurLanguette(float k) => (int)(115.2f * k);

    public static int LargeurCote(float k, Reglages r) => Math.Max((int)(260 * k), r.Largeur > 0 ? r.Largeur : (int)(470 * k));

    // La liste chevauche Steam : bords accolés (4 px ou moins), Sweech y affiche sa poignée d'échange.
    public static int Chevauchement(float k) => Math.Max(5, (int)(5 * k));

    // De combien Steam rétrécit pour le côte à côte.
    public static int Reduction(float k, Reglages r) =>
        r.Gouttiere ? LargeurCote(k, r) + LargeurLanguette(k) : LargeurCote(k, r) - Chevauchement(k);

    // Steam (partie visible) une fois rétréci pour le côte à côte, s'il obéit exactement.
    public static Rectangle SteamEpingle(Rectangle travail, Rectangle steam, bool agrandi, float k, Reglages r)
    {
        int d = Reduction(k, r);
        if (agrandi) return new Rectangle(travail.X, travail.Y, travail.Width - d, travail.Height);
        var v = new Rectangle(steam.X, steam.Y, Math.Max((int)(500 * k), steam.Width - d), steam.Height);
        // Steam déjà étroit et près du bord droit : il ne peut plus rétrécir, on le décale à gauche pour que la liste tienne.
        v.X -= Math.Min(Excedent(v, travail, k, r), Math.Max(0, v.X - travail.X));
        return v;
    }

    // Volet par-dessus (non épinglé) : bord droit de Steam, sous la marge haute.
    public static Rectangle Tiroir(Rectangle s, float k, Reglages r)
    {
        int haut = (int)(r.MargeHaute * k);
        int larg = Math.Clamp(r.Largeur > 0 ? r.Largeur : (int)(470 * k), (int)(260 * k), Math.Max((int)(260 * k), s.Width * 7 / 10));
        return new Rectangle(s.Right - larg, s.Top + haut, larg, s.Height - haut);
    }

    // Place de la liste (tiroir) et de la languette / gouttière pour Steam visible en « vs ».
    public static (Rectangle tiroir, Rectangle languette) Disposer(Rectangle vs, float k, Reglages r, bool cote, bool deplie)
    {
        int pw = LargeurLanguette(k), ph = HauteurLanguette(k);
        if (cote && r.Gouttiere)
        {
            // Gouttière : pleine hauteur entre Steam et la liste (qui ne se touchent plus), en arrière-plan derrière les deux
            // et prolongée de 5 px sous chacune (coins arrondis et bords de Windows remplis).
            int sous = Math.Max(5, (int)(5 * k));
            return (new Rectangle(vs.Right + pw, vs.Top, LargeurCote(k, r), vs.Height),
                    new Rectangle(vs.Right - sous, vs.Top, pw + 2 * sous, vs.Height));
        }
        if (cote)
        {
            var t = new Rectangle(vs.Right - Chevauchement(k), vs.Top, LargeurCote(k, r), vs.Height);
            return (t, new Rectangle(t.Left + 1 - pw, vs.Top + (vs.Height - ph) / 2, pw, ph));
        }
        var tiroir = Tiroir(vs, k, r);
        return (tiroir, new Rectangle((deplie ? tiroir.Left + 1 : vs.Right) - pw, tiroir.Top + (tiroir.Height - ph) / 2, pw, ph));
    }

    // Côte à côte : de combien la liste dépasse à droite de la zone de travail (0 si elle tient).
    public static int Excedent(Rectangle vs, Rectangle travail, float k, Reglages r)
    {
        int fin = r.Gouttiere ? vs.Right + LargeurLanguette(k) + LargeurCote(k, r) : vs.Right - Chevauchement(k) + LargeurCote(k, r);
        return Math.Max(0, fin - travail.Right);
    }
}
