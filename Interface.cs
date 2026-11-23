using System.Diagnostics;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Steem;

// Boutons ajoutés dans l'interface de Steam (0.13.0, Kev 19/11). Steam lancé avec l'accès développeur
// (fichier vide « .cef-enable-remote-debugging » dans son dossier) ouvre http://127.0.0.1:8080, local seulement.
// Steem s'y connecte au contexte partagé (SharedJSContext), y injecte Interface.js, et reçoit les clics par la
// liaison « steemAppel ». Sans cet accès, rien ne se passe (nouvel essai toutes les 10 s).
sealed class InterfaceSteam : IDisposable
{
    readonly Reglages reglages;
    readonly SynchronizationContext ui;
    readonly CancellationTokenSource fin = new();
    readonly SemaphoreSlim envoi = new(1, 1);
    readonly string script;
    ClientWebSocket ws;
    int prochainId = 10;
    Form fenetre;

    public InterfaceSteam(Reglages r)
    {
        reglages = r;
        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        using var s = typeof(InterfaceSteam).Assembly.GetManifestResourceStream("Interface.js");
        using var lu = new StreamReader(s);
        var version = typeof(InterfaceSteam).Assembly.GetName().Version?.ToString() ?? "0";
        script = lu.ReadToEnd().Replace("__VERSION__", version);
        _ = Task.Run(Boucle);
    }

    async Task Boucle()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        while (!fin.IsCancellationRequested)
        {
            try
            {
                var cibles = JsonNode.Parse(await http.GetStringAsync("http://127.0.0.1:8080/json", fin.Token))!.AsArray();
                var cible = cibles.FirstOrDefault(c => (string)c?["title"] == "SharedJSContext");
                var url = (string)cible?["webSocketDebuggerUrl"];
                if (url == null) { await Task.Delay(5000, fin.Token); continue; }
                ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(url), fin.Token);
                await Envoyer("Runtime.enable", null);
                await Envoyer("Runtime.addBinding", new JsonObject { ["name"] = "steemAppel" });
                await Injecter();
                await PublierSauvegardes();
                var garde = Task.Run(async () =>
                {
                    // Steam recharge parfois son interface : le script vérifie sa version et se remet en place.
                    while (ws.State == WebSocketState.Open && !fin.IsCancellationRequested)
                    { await Task.Delay(5000, fin.Token); await Injecter(); await PublierSauvegardes(); }
                });
                await Ecouter();
            }
            catch (OperationCanceledException) when (fin.IsCancellationRequested) { return; }
            catch { }
            try { ws?.Dispose(); } catch { }
            ws = null;
            try { await Task.Delay(10000, fin.Token); } catch { return; }
        }
    }

    Task Injecter() => Envoyer("Runtime.evaluate", new JsonObject { ["expression"] = script });

    // Dates des dernières sauvegardes, lues par la disquette de la page du jeu (LED, 0.15.0).
    public async Task PublierSauvegardes()
    {
        var d = await Task.Run(() => Ludusavi.Dernieres(reglages));
        var o = new JsonObject();
        foreach (var (appid, t) in d) o[appid.ToString()] = t;
        await Evaluer("window.__steemSauv = " + o.ToJsonString());
    }

    public Task Evaluer(string expression) => Envoyer("Runtime.evaluate", new JsonObject { ["expression"] = expression });

    async Task Envoyer(string methode, JsonObject parametres)
    {
        var w = ws;
        if (w == null || w.State != WebSocketState.Open) return;
        var msg = new JsonObject { ["id"] = Interlocked.Increment(ref prochainId), ["method"] = methode };
        if (parametres != null) msg["params"] = parametres;
        var octets = Encoding.UTF8.GetBytes(msg.ToJsonString());
        await envoi.WaitAsync(fin.Token);
        try { await w.SendAsync(octets, WebSocketMessageType.Text, true, fin.Token); }
        finally { envoi.Release(); }
    }

    async Task Ecouter()
    {
        var tampon = new byte[1 << 16];
        using var morceau = new MemoryStream();
        while (ws.State == WebSocketState.Open)
        {
            var r = await ws.ReceiveAsync(tampon, fin.Token);
            if (r.MessageType == WebSocketMessageType.Close) return;
            morceau.Write(tampon, 0, r.Count);
            if (!r.EndOfMessage) continue;
            var texte = Encoding.UTF8.GetString(morceau.GetBuffer(), 0, (int)morceau.Length);
            morceau.SetLength(0);
            JsonNode m;
            try { m = JsonNode.Parse(texte); } catch { continue; }
            var methode = (string)m?["method"];
            if (methode == "Runtime.executionContextCreated") { _ = Injecter(); continue; }
            if (methode != "Runtime.bindingCalled" || (string)m["params"]?["name"] != "steemAppel") continue;
            try
            {
                var appel = JsonNode.Parse((string)m["params"]["payload"]);
                var action = (string)appel?["action"];
                int appid = (int)appel["appid"];
                string nom = (string)appel["nom"] ?? "";
                if (action == "desinstaller") ui.Post(_ => Ouvrir(() => new FenetreDesinstaller(this, reglages, appid, nom)), null);
                else if (action == "sauvegardes") ui.Post(_ => Ouvrir(() => new FenetreSauvegardes(this, reglages, appid, nom)), null);
            }
            catch { }
        }
    }

    // Une seule fenêtre de Steem à la fois : un nouveau clic ramène celle déjà ouverte.
    void Ouvrir(Func<Form> creer)
    {
        if (fenetre != null && !fenetre.IsDisposed) { fenetre.Activate(); return; }
        fenetre = creer();
        fenetre.Show();
        fenetre.Activate();
    }

    // Fenêtre de confirmation de désinstallation de Steam (Interface.js : ShowUninstallDialog).
    // Jamais SteamClient.Installs.OpenUninstallWizard en direct : il désinstalle sans confirmation.
    public Task OuvrirDesinstallation(int appid) =>
        Evaluer($"window.__steem && window.__steem.desinstaller({appid})");

    public void Dispose()
    {
        fin.Cancel();
        try { ws?.Abort(); ws?.Dispose(); } catch { }
    }
}

// Fenêtre sombre : sauvegarder le profil du jeu (Ludusavi) puis désinstaller, ou désinstaller directement.
sealed class FenetreDesinstaller : FenetreSombre
{
    readonly InterfaceSteam steam;
    readonly Reglages reglages;
    readonly int appid;
    readonly string nom;
    readonly Label etat;
    readonly Button sauver, direct, annuler;

    public FenetreDesinstaller(InterfaceSteam s, Reglages r, int appid, string nom) : base("Steem — Désinstaller")
    {
        steam = s; reglages = r; this.appid = appid; this.nom = nom;
        Titre($"Désinstaller « {nom} »");
        Paragraphe("Sauvegarder d'abord le profil du jeu (sauvegardes, configs, binds) et la liste de ses mods ?", Texte);
        etat = Paragraphe("", Pale, 44);
        sauver = BoutonBas("Sauvegarder puis désinstaller", Accent);
        direct = BoutonBas("Désinstaller sans sauvegarde", Bouton);
        annuler = BoutonBas("Annuler", Bouton);
        PoserBarre();
        sauver.Click += async (_, _) => await Sauvegarder();
        direct.Click += async (_, _) => await Finir();
        annuler.Click += (_, _) => Close();
        CancelButton = annuler;
        if (Ludusavi.Chemin(reglages) == null)
            etat.Text = "Ludusavi introuvable (réglage « Ludusavi » de Steem) : seule la liste des mods sera gardée.";
    }

    void Echec(string texte)
    {
        etat.ForeColor = Rouge;
        etat.Text = texte;
        direct.Text = "Désinstaller quand même";
        direct.Enabled = annuler.Enabled = true;
    }

    async Task Sauvegarder()
    {
        var exe = Ludusavi.Chemin(reglages);
        sauver.Enabled = direct.Enabled = annuler.Enabled = false;
        etat.ForeColor = Pale;
        etat.Text = "Recherche des fichiers du jeu…";
        try
        {
            // Jeu inconnu de Ludusavi : la liste des mods est quand même gardée (0.16.0).
            var j = exe == null ? null : await Ludusavi.TrouverJeu(exe, appid, nom);
            etat.Text = "Sauvegarde en cours…";
            var b = await Ludusavi.SauvegarderTout(reglages, exe, j, nom, appid, " (avant désinstallation)");
            if (b.Vide) { Echec(b.Erreur != null ? "La sauvegarde a rencontré une erreur : " + b.Erreur : "Rien à sauvegarder : ni fichiers de partie, ni mods trouvés."); return; }
            if (b.Erreur != null) { Echec("La sauvegarde a rencontré une erreur, voir le dossier : " + b.Dossier); return; }
            etat.ForeColor = Texte;
            etat.Text = $"{b.Resume()} dans\n{b.Dossier}";
            await Task.Delay(1200);
            await Finir();
        }
        catch (Exception ex) { Echec("Échec de la sauvegarde : " + ex.Message); }
    }

    async Task Finir()
    {
        _ = steam.PublierSauvegardes();
        await steam.OuvrirDesinstallation(appid);
        Close();
    }
}
