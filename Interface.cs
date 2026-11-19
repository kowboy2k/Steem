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
    FenetreDesinstaller fenetre;

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
                var garde = Task.Run(async () =>
                {
                    // Steam recharge parfois son interface : le script vérifie sa version et se remet en place.
                    while (ws.State == WebSocketState.Open && !fin.IsCancellationRequested)
                    { await Task.Delay(5000, fin.Token); await Injecter(); }
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
                if ((string)appel?["action"] == "desinstaller")
                {
                    int appid = (int)appel["appid"];
                    string nom = (string)appel["nom"] ?? "";
                    ui.Post(_ => Desinstaller(appid, nom), null);
                }
            }
            catch { }
        }
    }

    void Desinstaller(int appid, string nom)
    {
        if (fenetre != null && !fenetre.IsDisposed) { fenetre.Activate(); return; }
        fenetre = new FenetreDesinstaller(this, reglages, appid, nom);
        fenetre.Show();
        fenetre.Activate();
    }

    // Assistant de désinstallation de Steam (il garde sa propre confirmation).
    public Task OuvrirDesinstallation(int appid) =>
        Evaluer($"SteamClient.Installs.OpenUninstallWizard([{appid}], false)");

    public void Dispose()
    {
        fin.Cancel();
        try { ws?.Abort(); ws?.Dispose(); } catch { }
    }
}

// Fenêtre sombre : sauvegarder le profil du jeu (Ludusavi) puis désinstaller, ou désinstaller directement.
sealed class FenetreDesinstaller : Form
{
    static readonly Color Fond = Color.FromArgb(0x17, 0x1d, 0x25), Texte = Color.FromArgb(0xc7, 0xd5, 0xe0),
                          Pale = Color.FromArgb(0x8f, 0x98, 0xa0), Accent = Color.FromArgb(0x1a, 0x9f, 0xff),
                          Bouton = Color.FromArgb(0x2a, 0x33, 0x40), Rouge = Color.FromArgb(0xe5, 0x48, 0x3f);

    readonly InterfaceSteam steam;
    readonly Reglages reglages;
    readonly int appid;
    readonly string nom;
    readonly Label etat;
    readonly Button sauver, direct, annuler;

    public FenetreDesinstaller(InterfaceSteam s, Reglages r, int appid, string nom)
    {
        steam = s; reglages = r; this.appid = appid; this.nom = nom;
        Text = "Steem — Désinstaller";
        Icon = Icone.Charger(32);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Fond; ForeColor = Texte;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 210);
        TopMost = true;

        var titre = new Label
        {
            Text = $"Désinstaller « {nom} »", Font = new Font("Segoe UI Semibold", 13f), ForeColor = Color.White,
            AutoSize = false, Bounds = new Rectangle(20, 16, 480, 30), AutoEllipsis = true,
        };
        var question = new Label
        {
            Text = "Sauvegarder d'abord le profil du jeu (sauvegardes, configs, binds) ?",
            AutoSize = false, Bounds = new Rectangle(20, 52, 480, 24),
        };
        etat = new Label { AutoSize = false, Bounds = new Rectangle(20, 80, 480, 60), ForeColor = Pale };
        sauver = Creer("Sauvegarder puis désinstaller", Accent, new Rectangle(20, 152, 230, 38));
        direct = Creer("Désinstaller sans sauvegarde", Bouton, new Rectangle(260, 152, 160, 38));
        annuler = Creer("Annuler", Bouton, new Rectangle(430, 152, 70, 38));
        sauver.Click += async (_, _) => await Sauvegarder();
        direct.Click += async (_, _) => await Finir();
        annuler.Click += (_, _) => Close();
        CancelButton = annuler;
        Controls.AddRange([titre, question, etat, sauver, direct, annuler]);
        if (CheminLudusavi() == null)
        {
            sauver.Enabled = false;
            etat.Text = "Ludusavi introuvable : la sauvegarde n'est pas possible (réglage « Ludusavi » de Steem).";
        }
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int sombre = 1; DwmSetWindowAttribute(Handle, 20, ref sombre, 4); // barre de titre sombre
    }

    static Button Creer(string texte, Color fond, Rectangle place)
    {
        var b = new Button
        {
            Text = texte, Bounds = place, FlatStyle = FlatStyle.Flat, BackColor = fond, ForeColor = Color.White,
            Cursor = Cursors.Hand, UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(fond, 0.15f);
        return b;
    }

    string CheminLudusavi()
    {
        if (!string.IsNullOrWhiteSpace(reglages.Ludusavi) && File.Exists(reglages.Ludusavi)) return reglages.Ludusavi;
        var aCote = Path.Combine(AppContext.BaseDirectory, "ludusavi.exe");
        if (File.Exists(aCote)) return aCote;
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try { var c = Path.Combine(d.Trim(), "ludusavi.exe"); if (File.Exists(c)) return c; } catch { }
        }
        return null;
    }

    static async Task<string> Lancer(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var sortie = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return await sortie;
    }

    async Task Sauvegarder()
    {
        var exe = CheminLudusavi();
        if (exe == null) return;
        sauver.Enabled = direct.Enabled = annuler.Enabled = false;
        etat.ForeColor = Pale;
        etat.Text = "Recherche des fichiers du jeu…";
        try
        {
            var trouve = JsonNode.Parse(await Lancer(exe, "find", "--steam-id", appid.ToString(), "--api"));
            var jeu = trouve?["games"]?.AsObject().Select(p => p.Key).FirstOrDefault();
            if (jeu == null)
            {
                etat.ForeColor = Rouge;
                etat.Text = "Ludusavi ne connaît pas les fichiers de ce jeu : rien n'a été sauvegardé.";
                direct.Text = "Désinstaller quand même";
                direct.Enabled = annuler.Enabled = true;
                return;
            }
            var racine = string.IsNullOrWhiteSpace(reglages.DossierSauvegardes)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Steem", "Sauvegardes")
                : reglages.DossierSauvegardes;
            var dossier = Path.Combine(racine, Nettoyer(jeu));
            etat.Text = "Sauvegarde en cours…";
            var r = JsonNode.Parse(await Lancer(exe, "backup", "--force", "--api", "--format", "zip", "--path", dossier, jeu));
            long octets = (long?)r?["overall"]?["processedBytes"] ?? (long?)r?["overall"]?["totalBytes"] ?? 0;
            int fichiers = r?["games"]?[jeu]?["files"]?.AsObject().Count ?? 0;
            bool erreur = r?["errors"] is JsonObject e && e.Count > 0;
            if (fichiers == 0 || erreur)
            {
                etat.ForeColor = Rouge;
                etat.Text = fichiers == 0 ? "Aucun fichier trouvé pour ce jeu : rien n'a été sauvegardé." : "La sauvegarde a rencontré une erreur, voir le dossier : " + dossier;
                direct.Text = "Désinstaller quand même";
                direct.Enabled = annuler.Enabled = true;
                return;
            }
            etat.ForeColor = Texte;
            etat.Text = $"✓ {fichiers} fichier{(fichiers > 1 ? "s" : "")} sauvegardé{(fichiers > 1 ? "s" : "")} ({octets / 1048576.0:0.0} Mo) dans\n{dossier}";
            await Task.Delay(1200);
            await Finir();
        }
        catch (Exception ex)
        {
            etat.ForeColor = Rouge;
            etat.Text = "Échec de la sauvegarde : " + ex.Message;
            direct.Text = "Désinstaller quand même";
            direct.Enabled = annuler.Enabled = true;
        }
    }

    static string Nettoyer(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim().TrimEnd('.');
    }

    async Task Finir()
    {
        await steam.OuvrirDesinstallation(appid);
        Close();
    }
}
