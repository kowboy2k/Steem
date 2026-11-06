using Microsoft.Win32;
using System.Diagnostics;

namespace Steem;

// Colle la liste de contacts de Steam dans le bord droit de la fenêtre principale.
// Replié : la liste passe juste sous Steam (cachée). Déplié : juste au-dessus.
sealed class Volet : IDisposable
{
    const string CleDemarrage = @"Software\Microsoft\Windows\CurrentVersion\Run";

    readonly Reglages reglages;
    readonly Steam steam = new();
    readonly Poignee poignee = new();
    readonly Cache cache = new();
    readonly CrochetShell crochet = new();
    readonly NotifyIcon icone;
    readonly Panneau panneau;
    readonly System.Windows.Forms.Timer minuterie = new() { Interval = 40 };

    readonly EventWaitHandle signalQuitter = new(false, EventResetMode.AutoReset, Program.SignalQuitter);
    readonly BarreDesTaches barre = new();
    bool deplie, gere, boutonAvant, poigneeVisible, cacheVisible, fermeForcee, horsBarre, nonLu, cote, ouvertManuel, activeParNous;
    IntPtr avantActivation;
    long pauseJusqua;
    Rectangle rectCache;
    long survolDebut, derniereDedans, retenuJusqua, prochainRetrait;
    Rectangle derniereCible, dernierVu, rectPoignee;
    IntPtr contactsConnus;
    Rectangle rectInitial;
    bool listeReduite; // liste réduite par nous en même temps que Steam
    readonly HashSet<IntPtr> signales = []; // fenêtres Steam déjà comptées comme « nouveau message », pas encore activées

    // Trace des clignotements reçus (%APPDATA%\Steem\messages.log, 100 Ko au plus) pour vérifier les faux positifs.
    static void Journal(IntPtr h, bool compte)
    {
        try
        {
            var dossier = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Steem");
            var f = Path.Combine(dossier, "messages.log");
            if (File.Exists(f) && new FileInfo(f).Length > 100_000) File.Delete(f);
            File.AppendAllText(f, $"{DateTime.Now:dd/MM/yyyy HH:mm:ss} {(compte ? "COMPTÉ " : "ignoré ")} {h} « {Native.Titre(h)} »{Environment.NewLine}");
        }
        catch { }
    }

    public Volet(Reglages r)
    {
        reglages = r;
        cote = r.SteamReduit; // Steam déjà rétréci par une session précédente interrompue
        _ = poignee.Handle;
        _ = cache.Handle;
        poignee.ClicEpingle += () => { if (!OuvrirContactsSiAbsents()) BasculerEpingle(); };
        crochet.Evenement += (code, h) =>
        {
            // Fenêtre activée : son bouton de barre des tâches n'est plus orange, un prochain clignotement compte de nouveau.
            if (code is 4 or 0x8004) signales.Remove(h);
            if (code == 0x8006 && h != steam.Principale && steam.EstSteam(h))
            {
                // 0x8006 (HSHELL_FLASH) revient aussi quand une fenêtre DÉJÀ signalée (bouton resté orange) est
                // redessinée, par ex. une discussion qui se réactualise : faux nouveau message (Kev, 06/11). On ne
                // compte donc qu'un clignotement par fenêtre tant qu'elle n'a pas été activée.
                bool nouveau = signales.Add(h) && h != Native.GetForegroundWindow();
                Journal(h, nouveau);
                if (nouveau)
                {
                    retenuJusqua = Environment.TickCount64 + reglages.DelaiMessageMs;
                    nonLu = true;
                }
            }
            // Activation, clignotement ou titre changé : l'Explorateur peut remettre le bouton, on le retire aussitôt.
            if (h == steam.Contacts && horsBarre) prochainRetrait = 0;
        };

        Recolorer();
        panneau = new Panneau(this, reglages);
        icone = new NotifyIcon { Icon = Icone.Charger(SystemInformation.SmallIconSize.Width), Text = "Steem", Visible = true };
        icone.MouseUp += (_, e) => { if (e.Button is MouseButtons.Left or MouseButtons.Right) panneau.Basculer(); };

        minuterie.Tick += (_, _) => Tic();
        minuterie.Start();

        // Steam déplacé ou redimensionné : on suit aussitôt, sans attendre le prochain tour de 40 ms
        // (sinon la languette traîne derrière le bord de Steam).
        suivi = (_, _, h, obj, _, _, _) => { if (obj == 0 && h != IntPtr.Zero && h == steam.Principale) Tic(); };
        crochetPosition = Native.SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, suivi, 0, 0, 0); // EVENT_OBJECT_LOCATIONCHANGE, hors contexte
    }

    readonly Native.WinEventProc suivi;
    readonly IntPtr crochetPosition;

    bool Mien(IntPtr h) => h == poignee.Handle || h == cache.Handle || h == panneau.Handle || h == steam.Contacts;

    // Zone du volet déplié, en pixels réels, à partir des bords visibles de Steam.
    public static Rectangle Tiroir(Rectangle s, float k, Reglages r) => Geometrie.Tiroir(s, k, r);

    void Tic()
    {
        if (signalQuitter.WaitOne(0)) { Quitter(); return; }
        long t = Environment.TickCount64;
        if (!steam.Trouver(reglages.TitresContacts)) { CacherPoignee(); return; }
        var s = steam.Principale;
        bool contactsOk = steam.ContactsOk;

        // Steam fermé dans la zone de notification : la liste redevient une fenêtre à part entière.
        if (!Native.IsWindowVisible(s)) RemettreBouton();
        else if (contactsOk && t >= prochainRetrait)
        {
            barre.Retirer(steam.Contacts);
            horsBarre = true;
            prochainRetrait = t + 2000;
        }
        if (t >= prochainTri) { TrierDiscussions(); statut = StatutSteam.Lire(); prochainTri = t + 2000; }

        // Steam réduit, fermé dans la zone de notification ou sur un autre bureau : on ne gère plus.
        if (!Native.IsWindowVisible(s) || Native.IsIconic(s) || Native.Masquee(s))
        {
            if (gere && !deplie && contactsOk) Native.PlacerSous(steam.Contacts, Native.HWND_BOTTOM);
            // Steam réduit (dont « Afficher le bureau ») : la liste, sans bouton de barre des tâches, n'est pas réduite
            // par Windows et restait au bord droit de l'écran ; on la réduit avec lui (0.12.6).
            if (gere && contactsOk && Native.IsIconic(s) && !Native.IsIconic(steam.Contacts))
            {
                Native.ShowWindowAsync(steam.Contacts, 7); // SW_SHOWMINNOACTIVE
                listeReduite = true;
            }
            gere = false; deplie = false;
            CacherPoignee();
            return;
        }
        gere = true;
        if (listeReduite)
        {
            // Steam revenu : la liste réduite avec lui revient, sans prendre le focus.
            listeReduite = false;
            if (contactsOk && Native.IsIconic(steam.Contacts)) Native.ShowWindowAsync(steam.Contacts, 4); // SW_SHOWNOACTIVATE
            derniereCible = Rectangle.Empty;
        }

        if (contactsOk && steam.Contacts != contactsConnus)
        {
            contactsConnus = steam.Contacts;
            rectInitial = Native.Rect(steam.Contacts);
            if (reglages.Largeur <= 0) reglages.Largeur = Native.Visuel(steam.Contacts).Width;
            derniereCible = Rectangle.Empty;
        }

        float k = Native.Echelle(s);
        int pw = Geometrie.LargeurLanguette(k);

        // Épinglé : Steam rétrécit pour faire place au volet, collé à sa droite ; la languette empiète sur Steam.
        bool coteVoulu = reglages.Epingle && contactsOk;
        if (coteVoulu != cote || (cote && Native.IsZoomed(s)))
        {
            Cote(coteVoulu, k);
            pauseJusqua = t + 250;
            return;
        }
        if (t < pauseJusqua) return;

        var vs = Native.Visuel(s);
        // Côte à côte : la liste doit finir au bord de l'écran. Steam ne prend pas toujours la largeur exacte demandée
        // (la liste débordait de 13 px à droite) : on le rétrécit de l'excédent, rendu à la sortie.
        if (cote && contactsOk && corrections < 3 && Native.GetAsyncKeyState(0x01) >= 0)
        {
            int trop = Geometrie.Excedent(vs, Screen.FromHandle(s).WorkingArea, k, reglages);
            if (trop > 1)
            {
                // Rétréci de l'excédent ; trop étroit pour ça, décalé à gauche.
                var r = vs.Width - trop >= (int)(500 * k) ? new Rectangle(vs.X, vs.Y, vs.Width - trop, vs.Height) : new Rectangle(vs.X - trop, vs.Y, vs.Width, vs.Height);
                Native.Placer(s, Native.RectPourVisuel(s, r), true);
                if (r.Width != vs.Width) reglages.ReductionSteam += trop;
                corrections++; // au plus 3 essais par entrée en côte à côte, si Steam refuse la largeur
                reglages.Enregistrer();
                pauseJusqua = t + 120;
                return;
            }
        }
        var (tiroir, rPoignee) = Geometrie.Disposer(vs, k, reglages, cote, deplie);

        int lb = (int)(reglages.BordSurvol * k);
        var bord = new Rectangle(vs.Right - lb, tiroir.Top, lb, tiroir.Height);

        var souris = Cursor.Position;
        bool bouton = Native.GetAsyncKeyState(0x01) < 0;
        bool surPoignee = reglages.AfficherLanguette && rPoignee.Contains(souris);
        bool surTiroir = deplie && Rectangle.Inflate(tiroir, (int)(8 * k), 0).Contains(souris);
        // La languette elle-même ne déplie pas au survol : seul le reste du bord droit le fait.
        // Seulement si c'est bien Steam sous la souris (pas le bureau ni une autre fenêtre par-dessus).
        bool surBord = !deplie && !bouton && !surPoignee && bord.Contains(souris)
            && Native.RacineSous(souris) is var sous && (sous == s || sous == steam.Contacts);
        if (surPoignee || surTiroir) { derniereDedans = t; retenuJusqua = 0; nonLu = false; }
        survolDebut = surBord ? (survolDebut == 0 ? t : survolDebut) : 0;
        var premier = Native.GetForegroundWindow();
        if (fermeForcee && premier != steam.Contacts) fermeForcee = false;
        if (premier == steam.Contacts) nonLu = false;
        signales.Remove(premier);
        // Un clic dans la liste activée par nous : elle devient la fenêtre de travail, plus de repli au départ de la souris.
        if (activeParNous && bouton && !boutonAvant && tiroir.Contains(souris)) activeParNous = false;

        bool parSurvol = surBord && t - survolDebut >= reglages.DelaiSurvolMs;
        bool voulu = reglages.Epingle
            || ouvertManuel
            || (premier == steam.Contacts && !fermeForcee && !activeParNous)
            || t < retenuJusqua
            || parSurvol
            || (deplie && (surTiroir || surPoignee)) // souris dedans : reste ouvert, même avec une fermeture « instantané » (sinon ça clignotait)
            || (deplie && t - derniereDedans < reglages.DelaiRepliMs)
            || (deplie && bouton);
        bool etaitDeplie = deplie;
        deplie = voulu && contactsOk;

        if (contactsOk)
        {
            var actuel = Native.Rect(steam.Contacts);
            // Largeur reprise quand la liste vient d'être redimensionnée à la main.
            if (boutonAvant && !bouton && deplie && premier == steam.Contacts)
            {
                int w = Native.Visuel(steam.Contacts).Width;
                if (Math.Abs(w - tiroir.Width) > 2)
                {
                    // Côte à côte : Steam cède (ou reprend) la différence.
                    if (cote) Native.Placer(s, Native.RectPourVisuel(s, new Rectangle(vs.X, vs.Y, vs.Width - (w - tiroir.Width), vs.Height)), true);
                    reglages.Largeur = w;
                    reglages.Enregistrer();
                }
            }
            else if (!bouton)
            {
                var cible = Native.RectPourVisuel(steam.Contacts, tiroir);
                if (cible != derniereCible || (actuel != dernierVu && actuel != cible))
                {
                    Native.Placer(steam.Contacts, cible, true);
                    derniereCible = cible;
                }
            }
            dernierVu = actuel;

            if (deplie)
            {
                if (premier != steam.Contacts && !Native.JusteAuDessus(steam.Contacts, s, Mien))
                    Native.MettreAuDessus(steam.Contacts, s, Mien);
            }
            else if (!Native.JusteEnDessous(steam.Contacts, s, Mien))
                Native.PlacerSous(steam.Contacts, s);

            // Steam ne redessine plus la liste tant qu'elle est cachée sous sa fenêtre, et ne la réveille qu'à l'activation :
            // ouverte au survol ou par le chevron, elle prend le focus ; repliée, le focus revient à la fenêtre d'avant.
            if (deplie && !etaitDeplie && !reglages.Epingle && (parSurvol || ouvertManuel) && premier != steam.Contacts)
            {
                avantActivation = premier;
                Native.Activer(steam.Contacts);
                activeParNous = true;
            }
            // Épinglée depuis la languette alors qu'elle était cachée : on la réveille aussi (elle garde le focus, plus de repli).
            else if (deplie && !etaitDeplie && reglages.Epingle && premier != steam.Contacts)
                Native.Activer(steam.Contacts);
            else if (!deplie && activeParNous)
            {
                activeParNous = false;
                if (Native.GetForegroundWindow() == steam.Contacts)
                    Native.Activer(avantActivation != IntPtr.Zero && Native.IsWindow(avantActivation) ? avantActivation : s);
            }
        }
        boutonAvant = bouton;

        // Gouttière (épinglé) : la liste garde ses boutons Réduire / Agrandir / Fermer, pas de cache.
        if (deplie && !(cote && reglages.Gouttiere))
        {
            if (!cacheVisible) cache.Accorder(steam.Contacts, k);
            var vc = Native.Visuel(steam.Contacts);
            // Décalé d'un pixel en haut et à droite : le liseré de Windows autour de la liste reste visible.
            int marge = Math.Max(1, (int)k);
            var rc = new Rectangle(vc.Right - (int)(108 * k) - marge, vc.Top + marge, (int)(108 * k), (int)(36 * k));
            if (!cacheVisible || rc != rectCache)
            {
                Native.Placer(cache.Handle, rc, false);
                rectCache = rc;
                cacheVisible = true;
            }
            if (!Native.JusteAuDessus(cache.Handle, steam.Contacts, Mien)) Native.MettreAuDessus(cache.Handle, steam.Contacts, Mien);
        }
        else CacherCache();

        // Position recalculée avec l'état de CE tour : sinon la languette restait 40 ms à l'ancienne place
        // (chevron déjà retourné, liste déjà partie).
        if (!cote) rPoignee = Geometrie.Disposer(vs, k, reglages, cote, deplie).languette;
        // Redimensionnement à la main en cours : la languette suit le bord gauche réel de la liste.
        if (deplie && contactsOk && bouton && premier == steam.Contacts && !(cote && reglages.Gouttiere))
            rPoignee.X = Native.Visuel(steam.Contacts).Left + 1 - pw;
        poignee.Gouttiere = cote && reglages.Gouttiere;
        poignee.Deplie = deplie;
        poignee.Epingle = reglages.Epingle;
        poignee.Absent = !contactsOk;
        poignee.NonLu = nonLu;
        poignee.Statut = statut;
        // Languette masquée dans le panneau : le survol du bord et l'icône suffisent.
        if (!reglages.AfficherLanguette && !(cote && reglages.Gouttiere))
        {
            if (poigneeVisible) { Native.ShowWindow(poignee.Handle, 0); poigneeVisible = false; }
            return;
        }
        if (!poigneeVisible || rPoignee != rectPoignee)
        {
            poignee.Placer(rPoignee);
            rectPoignee = rPoignee;
            poigneeVisible = true;
        }
        // Déplié : au-dessus de la liste, sinon sa bordure invisible (à gauche) recouvre la languette
        // et Windows y affiche sa poignée de redimensionnement des fenêtres accolées.
        if (cote && reglages.Gouttiere)
        {
            // Gouttière : juste sous Steam (la liste est au-dessus de Steam), donc derrière les deux.
            if (!Native.JusteEnDessous(poignee.Handle, s, Mien)) Native.PlacerSous(poignee.Handle, s);
            return;
        }
        var dessous = deplie && contactsOk ? steam.Contacts : s;
        if (!Native.JusteAuDessus(poignee.Handle, dessous, Mien)) Native.MettreAuDessus(poignee.Handle, dessous, Mien);
    }

    int corrections;

    int LargeurCote(float k) => Geometrie.LargeurCote(k, reglages);

    // La liste chevauche Steam : bords accolés (4 px ou moins), Sweech y affiche sa poignée d'échange.
    static int Chevauchement(float k) => Geometrie.Chevauchement(k);

    // Entre ou sort du côte à côte. Steam agrandi sort directement dans la zone de travail moins le volet,
    // et y retourne à la sortie s'il l'occupe toujours.
    void Cote(bool actif, float k)
    {
        var s = steam.Principale;
        int d = actif || reglages.ReductionSteam <= 0 ? Geometrie.Reduction(k, reglages) : reglages.ReductionSteam;
        var travail = Screen.FromHandle(s).WorkingArea;
        if (actif)
        {
            if (Native.IsZoomed(s))
                Native.RestaurerDans(s, new Rectangle(travail.X, travail.Y, travail.Width - d, travail.Height));
            else
            {
                Native.Placer(s, Native.RectPourVisuel(s, Geometrie.SteamEpingle(travail, Native.Visuel(s), false, k, reglages)), true);
            }
        }
        else if (!Native.IsZoomed(s))
        {
            var v = Native.Visuel(s);
            var r = new Rectangle(v.X, v.Y, v.Width + d, v.Height);
            if (Math.Abs(r.Left - travail.Left) <= 2 && Math.Abs(r.Top - travail.Top) <= 2
                && r.Right >= travail.Right - 2 && Math.Abs(r.Bottom - travail.Bottom) <= 2) // au-delà : Steam avait gardé quelques px de trop
                Native.Agrandir(s);
            else
                Native.Placer(s, Native.RectPourVisuel(s, r), true);
        }
        cote = actif;
        corrections = 0;
        reglages.SteamReduit = actif;
        reglages.ReductionSteam = actif ? d : 0;
        reglages.Enregistrer();
        derniereCible = Rectangle.Empty;
    }

    // Option du panneau : épinglé, on sort du côte à côte avec l'ancienne forme ; la boucle y rentre avec la nouvelle.
    internal void BasculerGouttiere()
    {
        if (cote && steam.Principale != IntPtr.Zero && Native.IsWindow(steam.Principale)) Cote(false, Native.Echelle(steam.Principale));
        reglages.Gouttiere = !reglages.Gouttiere;
        reglages.Enregistrer();
        rectPoignee = Rectangle.Empty;
    }

    void CacherCache()
    {
        if (!cacheVisible) return;
        Native.ShowWindow(cache.Handle, 0);
        cacheVisible = false;
    }

    void CacherPoignee()
    {
        CacherCache();
        if (!poigneeVisible) return;
        Native.ShowWindow(poignee.Handle, 0);
        poigneeVisible = false;
    }

    bool OuvrirContactsSiAbsents()
    {
        if (steam.ContactsOk) return false;
        try { Process.Start(new ProcessStartInfo("steam://open/friends") { UseShellExecute = true }); } catch { }
        return true;
    }

    // Épinglé = ouvert à côté de Steam, qui se réarrange ; détaché = fermé tout de suite.
    // Fond de la languette choisi dans le panneau.
    internal void Recolorer()
    {
        try { Poignee.Fond = ColorTranslator.FromHtml("#" + reglages.CouleurLanguette); } catch { }
        Poignee.OmbreTaille = reglages.OmbreTaille; Poignee.OmbreDx = reglages.OmbreDx; Poignee.OmbreDy = reglages.OmbreDy; Poignee.OmbreOpacite = reglages.OmbreOpacite;
        Poignee.StatutSiOuvert = reglages.StatutSiOuvert;
        poignee.Rendre();
    }

    internal void BasculerEpingle()
    {
        reglages.Epingle = !reglages.Epingle;
        ouvertManuel = false;
        if (!reglages.Epingle) Fermer();
        reglages.Enregistrer();
    }

    // Chevron : ouvre par-dessus Steam jusqu'au prochain clic ; ferme (et détache) si ouvert.
    void BasculerVolet()
    {
        if (reglages.Epingle) { BasculerEpingle(); return; }
        if (deplie) { ouvertManuel = false; Fermer(); }
        else ouvertManuel = true;
    }

    void Fermer()
    {
        derniereDedans = 0;
        retenuJusqua = 0;
        if (Native.GetForegroundWindow() == steam.Contacts)
        {
            fermeForcee = true;
            if (steam.Principale != IntPtr.Zero) Native.Activer(steam.Principale);
        }
    }

    internal static bool AuDemarrage()
    {
        using var k = Registry.CurrentUser.OpenSubKey(CleDemarrage);
        return k?.GetValue("Steem") != null;
    }

    internal static void BasculerDemarrage()
    {
        using var k = Registry.CurrentUser.CreateSubKey(CleDemarrage);
        if (AuDemarrage()) k.DeleteValue("Steem", false);
        else k.SetValue("Steem", $"\"{Environment.ProcessPath}\"");
    }

    void RemettreBouton()
    {
        if (!horsBarre) return;
        if (Native.IsWindow(steam.Contacts)) barre.Remettre(steam.Contacts);
        horsBarre = false;
        prochainRetrait = 0;
    }

    // Rend la liste de contacts telle qu'elle était, au-dessus de Steam, avec son bouton.
    void Restaurer()
    {
        RemettreBouton();
        DegrouperDiscussions();
        if (cote && steam.Principale != IntPtr.Zero && Native.IsWindow(steam.Principale))
        {
            Cote(false, Native.Echelle(steam.Principale));
        }
        if (!steam.ContactsOk || steam.Contacts != contactsConnus) return;
        Native.Placer(steam.Contacts, rectInitial, true);
        if (steam.Principale != IntPtr.Zero) Native.MettreAuDessus(steam.Contacts, steam.Principale, Mien);
    }

    // Discussions Steam (fenêtres à part, hors Steam et liste) : leur propre bouton dans la barre des tâches.
    readonly HashSet<IntPtr> discussions = [];
    readonly Icon icoPetite = Icone.Charger(SystemInformation.SmallIconSize.Width, "Discussions.ico"),
                 icoGrande = Icone.Charger(SystemInformation.IconSize.Width, "Discussions.ico");
    long prochainTri;
    int statut = StatutSteam.Lire();

    // Option « Regrouper les chats » coupée : les discussions rejoignent le bouton de Steam.
    internal void AppliquerRegroupement()
    {
        if (reglages.RegrouperChats) TrierDiscussions(); else DegrouperDiscussions();
    }

    void DegrouperDiscussions()
    {
        foreach (var h in discussions) if (Native.IsWindow(h)) { Groupe.Definir(h, null); Groupe.RendreIcone(h); }
        discussions.Clear();
    }

    void TrierDiscussions()
    {
        if (!reglages.RegrouperChats) return;
        discussions.RemoveWhere(h => !Native.IsWindow(h));
        foreach (var f in steam.Lister())
        {
            if (f.Hwnd == steam.Principale || f.Hwnd == steam.Contacts || f.Proprietaire != IntPtr.Zero || !f.Visible
                || f.Classe != "SDL_app" || string.IsNullOrWhiteSpace(f.Titre) || f.Titre.Contains("Steam")
                || reglages.TitresContacts.Contains(f.Titre) || discussions.Contains(f.Hwnd)) continue;
            if (Groupe.Definir(f.Hwnd, Groupe.Discussions)) discussions.Add(f.Hwnd);
        }
        // Icône « discussions » (logo Steam + bulle blanche), remise si Steam la change.
        foreach (var h in discussions) Groupe.Iconiser(h, icoPetite.Handle, icoGrande.Handle);    }

    internal void Quitter()
    {
        minuterie.Stop();
        if (crochetPosition != IntPtr.Zero) Native.UnhookWinEvent(crochetPosition);
        Restaurer();
        Application.ExitThread();
    }

    public void Dispose()
    {
        minuterie.Dispose();
        icone.Visible = false;
        icone.Dispose();
        crochet.Dispose();
        poignee.Dispose();
        cache.Dispose();
        panneau.Dispose();
    }
}

// Reçoit les messages du shell, dont le clignotement (0x8006) : Steam fait clignoter la discussion à chaque nouveau message.
sealed class CrochetShell : NativeWindow, IDisposable
{
    readonly int message;
    public event Action<int, IntPtr> Evenement;

    public CrochetShell()
    {
        CreateHandle(new CreateParams());
        message = (int)Native.RegisterWindowMessage("SHELLHOOK");
        Native.RegisterShellHookWindow(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == message) Evenement?.Invoke((int)m.WParam, m.LParam);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Native.DeregisterShellHookWindow(Handle);
        DestroyHandle();
    }
}

static class Icone
{
    // Steem.ico (logo Steam dont le tiers droit devient le volet), intégré à l'exe.
    public static Icon Charger(int taille, string nom = "Steem.ico")
    {
        using var s = typeof(Icone).Assembly.GetManifestResourceStream(nom);
        return new Icon(s, taille, taille);
    }
}
