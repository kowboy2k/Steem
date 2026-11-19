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

  const ID = 'steem-desinstaller';
  const icone = '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor" style="flex:none">'
    + '<path d="M9 3h6l1 2h4v2H4V5h4l1-2zm-3 6h12l-1 12H7L6 9zm4 2v8h1.5v-8H10zm3.5 0v8H15v-8h-1.5z"/></svg>';

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
      const barre = doc.querySelector('.' + classes.AppButtonsContainer);
      let b = doc.getElementById(ID);
      if (!barre || !o || !o.installed) { if (b) b.remove(); return; }
      if (b && (b.parentElement !== barre || b.dataset.appid !== String(appid))) { b.remove(); b = null; }
      if (b) return;
      b = doc.createElement('div');
      b.id = ID;
      b.dataset.appid = String(appid);
      b.className = classes.MenuButton;
      b.title = 'Désinstaller (Steem propose d\'abord une sauvegarde)';
      b.setAttribute('role', 'button');
      b.style.cssText = 'display:flex;align-items:center;gap:8px;width:auto;padding:0 14px;cursor:pointer;'
        + 'font-size:13px;font-weight:500;letter-spacing:.3px;white-space:nowrap';
      b.innerHTML = icone + '<span>Désinstaller</span>';
      b.addEventListener('click', e => {
        e.preventDefault(); e.stopPropagation();
        const ov = appStore.GetAppOverviewByAppID(appid);
        window.steemAppel(JSON.stringify({ action: 'desinstaller', appid, nom: ov ? ov.display_name : '' }));
      });
      barre.insertBefore(b, barre.firstChild);
    } catch (e) { /* Steam en cours de chargement : prochain tour */ }
  };

  window.__steem = { version: VERSION, boucle: setInterval(cycle, 400) };
  cycle();
  return 'injecte';
})()
