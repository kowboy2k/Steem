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

  // Boutons de Steem, dans l'ordre : « Désinstaller » puis la disquette (sauvegarder / restaurer, 0.14.0).
  const BOUTONS = [
    { sorte: 'desinstaller', texte: 'Désinstaller', titre: 'Désinstaller (Steem propose d\'abord une sauvegarde)',
      icone: '<path d="M9 3h6l1 2h4v2H4V5h4l1-2zm-3 6h12l-1 12H7L6 9zm4 2v8h1.5v-8H10zm3.5 0v8H15v-8h-1.5z"/>' },
    { sorte: 'sauvegardes', texte: '', titre: 'Sauvegarder ou restaurer les fichiers du jeu (Steem)',
      icone: '<path d="M17 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V7l-4-4zm-5 16a3 3 0 1 1 0-6 3 3 0 0 1 0 6zm3-10H5V5h10v4z"/>' },
  ];
  const CLASSE = 'steem-bouton';

  const creer = (doc, appid, d) => {
    const b = doc.createElement('div');
    b.className = CLASSE + ' steem-' + d.sorte + ' ' + classes.MenuButton;
    b.dataset.appid = String(appid);
    b.title = d.titre;
    b.setAttribute('role', 'button');
    b.style.cssText = 'display:flex;align-items:center;justify-content:center;gap:8px;cursor:pointer;'
      + (d.texte ? 'width:auto;padding:0 14px;font-size:13px;font-weight:500;letter-spacing:.3px;white-space:nowrap' : '');
    b.innerHTML = '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor" style="flex:none">' + d.icone + '</svg>'
      + (d.texte ? '<span>' + d.texte + '</span>' : '');
    b.addEventListener('click', e => {
      e.preventDefault(); e.stopPropagation();
      const ov = appStore.GetAppOverviewByAppID(appid);
      window.steemAppel(JSON.stringify({ action: d.sorte, appid, nom: ov ? ov.display_name : '' }));
    });
    return b;
  };

  const cycle = () => {
    try {
      if (!classes) classes = chercherClasses();
      if (!classes) return;
      const popup = g_PopupManager.GetExistingPopup('SP Desktop_uid0');
      const doc = popup && popup.m_popup && popup.m_popup.document;
      if (!doc) return;
      const chemin = (MainWindowBrowserManager.m_lastLocation || {}).pathname || '';
      const m = /^\/library\/app\/(\d+)/.exec(chemin);
      const appid = m ? Number(m[1]) : 0;
      const o = appid ? appStore.GetAppOverviewByAppID(appid) : null;
      // Deux barres sur la page : l'en-tête compact (affiché au défilement) et la barre sous l'image (20/11).
      const barres = o && o.installed ? Array.from(doc.querySelectorAll('.' + classes.AppButtonsContainer)) : [];
      for (const x of doc.querySelectorAll('#steem-desinstaller, .steem-desinstaller:not(.' + CLASSE + ')')) x.remove(); // 0.13.x
      for (const x of doc.querySelectorAll('.' + CLASSE))
        if (!barres.includes(x.parentElement) || x.dataset.appid !== String(appid)) x.remove();
      for (const barre of barres) {
        if (barre.querySelectorAll(':scope > .' + CLASSE).length === BOUTONS.length) continue;
        for (const x of barre.querySelectorAll(':scope > .' + CLASSE)) x.remove();
        const debut = barre.firstChild;
        for (const d of BOUTONS) barre.insertBefore(creer(doc, appid, d), debut);
      }
    } catch (e) { /* Steam en cours de chargement : prochain tour */ }
  };

  window.__steem = { version: VERSION, boucle: setInterval(cycle, 400) };
  cycle();
  return 'injecte';
})()
