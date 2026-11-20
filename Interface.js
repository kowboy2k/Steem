// Injecté par Steem dans le contexte partagé de l'interface Steam (SharedJSContext) par l'accès développeur.
// Ajoute un bouton « Désinstaller » dans la barre de la page d'un jeu installé ; le clic est renvoyé à Steem
// par la liaison window.steemAppel (Runtime.addBinding). Relancé sans risque : l'ancienne boucle est arrêtée.
(() => {
  const VERSION = '__VERSION__';
  if (window.__steem && window.__steem.version === VERSION) return 'deja';
  if (window.__steem) clearInterval(window.__steem.boucle);

  // Classes de la barre du jeu, retrouvées par leur nom dans les modules de Steam (leurs valeurs changent à chaque version).
  let classes = null;
  const chercherClasses = () => {
    let req;
    window.webpackChunksteamui.push([[Symbol()], {}, r => { req = r; }]);
    for (const id of Object.keys(req.m)) {
      let m; try { m = req(id); } catch { continue; }
      if (m && typeof m === 'object' && typeof m.AppButtonsContainer === 'string' && typeof m.MenuButton === 'string'
          && typeof m.GameInfoButton === 'string') return m;
    }
    return null;
  };

  // Fenêtre de confirmation de désinstallation de Steam (ShowUninstallDialog), retrouvée par son message d'erreur interne.
  // SteamClient.Installs.OpenUninstallWizard, lui, désinstalle SANS confirmation (Steam l'appelle après le « OK ») : jamais direct.
  const desinstaller = appid => {
    let req;
    window.webpackChunksteamui.push([[Symbol()], {}, r => { req = r; }]);
    for (const id of Object.keys(req.m)) {
      const src = String(req.m[id]);
      if (src.indexOf('RemoteClientID to ShowUninstallDialog') < 0) continue;
      const m = req(id);
      const k = Object.keys(m).find(k => typeof m[k] === 'function' && String(m[k]).indexOf('ShowUninstallDialog') >= 0);
      if (!k) break;
      let client = '0';   // ce PC
      try {
        const x = /(\w+)\.(\w+),"Sent in invalid RemoteClientID/.exec(String(m[k]));
        const y = new RegExp('[,{ ]' + x[1] + '=\\w\\((\\d+)\\)').exec(src);
        client = req(y[1])[x[2]] ?? '0';
      } catch { }
      const fenetre = g_PopupManager.GetExistingPopup('SP Desktop_uid0').m_popup;
      m[k]([appid], fenetre, false, client);
      return 'ok';
    }
    return 'introuvable';
  };

  // Boutons de Steem, dans l'ordre : « Désinstaller » puis la disquette (sauvegarder / restaurer, 0.14.0).
  const BOUTONS = [
    { sorte: 'desinstaller', texte: 'Désinstaller', titre: 'Désinstaller (Steem propose d\'abord une sauvegarde)',
      icone: '<path d="M9 3h6l1 2h4v2H4V5h4l1-2zm-3 6h12l-1 12H7L6 9zm4 2v8h1.5v-8H10zm3.5 0v8H15v-8h-1.5z"/>' },
    { sorte: 'sauvegardes', texte: '', titre: 'Sauvegarder ou restaurer les fichiers du jeu (Steem)', led: true,
      icone: '<path d="M17 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V7l-4-4zm-5 16a3 3 0 1 1 0-6 3 3 0 0 1 0 6zm3-10H5V5h10v4z"/>' },
  ];
  const CLASSE = 'steem-bouton';

  // Infobulle de Steem (0.15.6) : celle de Steam (attribut title) coupait le texte dans un cadre trop petit.
  const BULLE = 'steem-bulle';
  const cacherBulle = doc => { const x = doc.getElementById(BULLE); if (x) x.remove(); };
  const bulle = (doc, b) => {
    cacherBulle(doc);
    const x = doc.createElement('div');
    x.id = BULLE;
    x.textContent = b.dataset.info || '';
    x.style.cssText = 'position:fixed;z-index:100000;pointer-events:none;white-space:pre;max-width:none;'
      + 'background:#3d4450;color:#dcdedf;font-size:12px;line-height:1.5;padding:6px 10px;border-radius:3px;'
      + 'box-shadow:0 2px 8px rgba(0,0,0,.6)';
    doc.body.appendChild(x);
    const r = b.getBoundingClientRect(), w = x.offsetWidth, h = x.offsetHeight, vw = doc.documentElement.clientWidth;
    x.style.left = Math.max(4, Math.min(r.right - w, vw - w - 4)) + 'px';
    x.style.top = (r.bottom + h + 8 > doc.documentElement.clientHeight ? r.top - h - 6 : r.bottom + 6) + 'px';
  };

  const creer = (doc, appid, d) => {
    const b = doc.createElement('div');
    b.className = CLASSE + ' steem-' + d.sorte + ' ' + classes.MenuButton;
    b.dataset.appid = String(appid);
    b.dataset.v = VERSION;   // boutons d'une version précédente de Steem : refaits
    b.dataset.info = d.titre;
    b.addEventListener('mouseenter', () => bulle(doc, b));
    b.addEventListener('mouseleave', () => cacherBulle(doc));
    b.setAttribute('role', 'button');
    b.style.cssText = 'display:flex;align-items:center;justify-content:center;gap:8px;cursor:pointer;'
      + (d.texte || d.led ? 'width:auto;padding:0 12px;font-size:13px;font-weight:400;white-space:nowrap' : '');
    b.innerHTML = '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor" style="flex:none">' + d.icone + '</svg>'
      + (d.texte ? '<span>' + d.texte + '</span>' : '')
      + (d.led ? '<span class="steem-date" style="font-size:11px"></span><span class="steem-led" style="flex:none;width:8px;height:8px;border-radius:50%"></span>' : '');
    b.addEventListener('click', e => {
      e.preventDefault(); e.stopPropagation();
      const ov = appStore.GetAppOverviewByAppID(appid);
      window.steemAppel(JSON.stringify({ action: d.sorte, appid, nom: ov ? ov.display_name : '' }));
    });
    return b;
  };

  // LED de la disquette (0.15.0) : grise = jamais sauvegardé ; verte = sauvegarde du jour du dernier lancement
  // ou plus récente ; rouge = sauvegarde plus ancienne que le dernier lancement. Dates poussées par Steem (window.__steemSauv).
  const jour = t => { const d = new Date(t * 1000); d.setHours(0, 0, 0, 0); return d.getTime(); };
  const date = t => new Date(t * 1000).toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric' });
  const heure = t => new Date(t * 1000).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
  const led = (b, appid, o) => {
    const t = (window.__steemSauv || {})[appid] || 0;
    const lance = o.rt_last_time_played || 0;
    const etat = t + '|' + lance;
    if (b.dataset.etat === etat) return;
    b.dataset.etat = etat;
    const l = b.querySelector('.steem-led'), d = b.querySelector('.steem-date');
    if (!l || !d) return;
    const couleur = !t ? '#5c6670' : (!lance || jour(t) >= jour(lance)) ? '#4cd137' : '#e5483f';
    l.style.background = couleur;
    l.style.boxShadow = t ? '0 0 6px ' + couleur : 'none';
    d.textContent = t ? date(t) : 'Aucune';
    b.dataset.info = 'Sauvegarder ou restaurer les fichiers du jeu (Steem)\n'
      + 'Dernière sauvegarde : ' + (t ? date(t) + ' ' + heure(t) : 'aucune') + '\n'
      +'Dernier lancement : ' + (lance ? date(lance) + ' ' + heure(lance) : 'jamais');
  };

  const cycle = () => {
    try {
      if (!classes) classes = chercherClasses();
      if (!classes) return;
      const popup = g_PopupManager.GetExistingPopup('SP Desktop_uid0');
      const doc = popup && popup.m_popup && popup.m_popup.document;
      if (!doc) return;
      if (doc.getElementById(BULLE) && !doc.querySelector('.' + CLASSE + ':hover')) cacherBulle(doc);  // bouton retiré sous la souris
      const chemin = (MainWindowBrowserManager.m_lastLocation || {}).pathname || '';
      const m = /^\/library\/app\/(\d+)/.exec(chemin);
      const appid = m ? Number(m[1]) : 0;
      const o = appid ? appStore.GetAppOverviewByAppID(appid) : null;
      // Deux barres sur la page : l'en-tête compact (affiché au défilement) et la barre sous l'image (20/11).
      const barres = o && o.installed ? Array.from(doc.querySelectorAll('.' + classes.AppButtonsContainer)) : [];
      for (const x of doc.querySelectorAll('#steem-desinstaller, .steem-desinstaller:not(.' + CLASSE + ')')) x.remove(); // 0.13.x
      for (const x of doc.querySelectorAll('.' + CLASSE))
        if (!barres.includes(x.parentElement) || x.dataset.appid !== String(appid) || x.dataset.v !== VERSION) x.remove();
      for (const barre of barres) {
        if (barre.querySelectorAll(':scope > .' + CLASSE).length === BOUTONS.length) continue;
        for (const x of barre.querySelectorAll(':scope > .' + CLASSE)) x.remove();
        const debut = barre.firstChild;
        for (const d of BOUTONS) barre.insertBefore(creer(doc, appid, d), debut);
      }
      for (const b of doc.querySelectorAll('.steem-sauvegardes.' + CLASSE)) led(b, appid, o);
    } catch (e) { /* Steam en cours de chargement : prochain tour */ }
  };

  window.__steem = { version: VERSION, boucle: setInterval(cycle, 400), desinstaller };
  cycle();
  return 'injecte';
})()
