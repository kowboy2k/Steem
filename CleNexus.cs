namespace Steem;

// Fenêtre « Clé API Nexus » (0.16.8, Kev 24/11 : « je la mets comment dedans ? ») : ouverte par Steem.exe --cle-nexus,
// sans passer par une sauvegarde. La clé va dans le Gestionnaire d'identification de Windows (Nexus.Enregistrer).
sealed class FenetreCleNexus : FenetreSombre
{
    readonly TextBox saisie;
    readonly Label etat;
    readonly Button garder;

    public FenetreCleNexus() : base("Steem — Clé API Nexus")
    {
        ShowInTaskbar = true;
        Titre("Clé API Nexus");
        Paragraphe("Elle permet à Steem de retélécharger tes mods Nexus (compte Premium) et de les déposer dans Mod Organizer.", Texte);
        Paragraphe("1.  « Ouvrir la page Nexus » ci-dessous (connecté à ton compte).\n"
                 + "2.  Tout en bas, « Personal API Key » : copie la clé (« Request an API Key » si elle n'existe pas encore).\n"
                 + "3.  Colle-la ici, puis « Enregistrer ».", Pale);
        saisie = Ajouter(new TextBox
        {
            Width = Largeur, UseSystemPasswordChar = true, BackColor = Bouton, ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 10, 0, 4), Font = new Font("Segoe UI", 11f),
        });
        etat = Paragraphe(Nexus.Cle() != null ? "Une clé est déjà enregistrée : en coller une nouvelle la remplace." : "", Pale, 24);
        garder = BoutonBas("Enregistrer", Accent);
        var page = BoutonBas("Ouvrir la page Nexus", Bouton);
        var fermer = BoutonBas("Fermer", Bouton);
        PoserBarre();
        garder.Click += async (_, _) => await Enregistrer();
        page.Click += (_, _) => Navigateur.Ouvrir("https://next.nexusmods.com/settings/api-keys");
        fermer.Click += (_, _) => Close();
        AcceptButton = garder;
        CancelButton = fermer;
    }

    async Task Enregistrer()
    {
        var cle = saisie.Text.Trim();
        if (cle.Length < 10) { etat.ForeColor = Rouge; etat.Text = "Colle d'abord la clé copiée sur la page Nexus."; return; }
        garder.Enabled = false;
        etat.ForeColor = Pale; etat.Text = "Vérification auprès de Nexus…";
        try
        {
            var c = await Nexus.Valider(cle);
            Nexus.Enregistrer(cle);
            saisie.Clear();
            etat.ForeColor = c.Premium ? Texte : Rouge;
            etat.Text = c.Premium
                ? $"✓ Clé enregistrée : {c.Nom} (Premium). Steem peut retélécharger tes mods."
                : $"Clé enregistrée pour {c.Nom}, mais le compte n'est pas Premium : Nexus refusera le téléchargement direct.";
        }
        catch (Exception ex) { etat.ForeColor = Rouge; etat.Text = "Clé non enregistrée : " + ex.Message; }
        garder.Enabled = true;
    }
}
