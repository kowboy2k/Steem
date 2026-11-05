using System.Diagnostics;

namespace Steem;

record Fenetre(IntPtr Hwnd, uint Pid, string Classe, string Titre, bool Visible, IntPtr Proprietaire, Rectangle Rect)
{
    public override string ToString() =>
        $"{Hwnd} pid={Pid} visible={Visible} classe={Classe} titre='{Titre}' rect={Rect} proprio={Proprietaire}";
}

// Repère la fenêtre principale de Steam et sa liste de contacts.
sealed class Steam
{
    public IntPtr Principale, Contacts;
    readonly Dictionary<uint, bool> processus = new();
    long prochainScan;

    public bool ContactsOk =>
        Contacts != IntPtr.Zero && Native.IsWindow(Contacts) && Native.IsWindowVisible(Contacts) && !Native.IsIconic(Contacts);

    public bool EstSteam(IntPtr h)
    {
        Native.GetWindowThreadProcessId(h, out var pid);
        return EstSteam(pid);
    }

    bool EstSteam(uint pid)
    {
        if (processus.TryGetValue(pid, out var oui)) return oui;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            oui = p.ProcessName.Equals("steamwebhelper", StringComparison.OrdinalIgnoreCase)
               || p.ProcessName.Equals("steam", StringComparison.OrdinalIgnoreCase);
        }
        catch { oui = false; }
        return processus[pid] = oui;
    }

    public List<Fenetre> Lister()
    {
        var liste = new List<Fenetre>();
        Native.EnumWindows((h, _) =>
        {
            Native.GetWindowThreadProcessId(h, out var pid);
            if (EstSteam(pid))
                liste.Add(new Fenetre(h, pid, Native.Classe(h), Native.Titre(h), Native.IsWindowVisible(h),
                                      Native.GetWindow(h, Native.GW_OWNER), Native.Rect(h)));
            return true;
        }, IntPtr.Zero);
        return liste;
    }

    public bool Trouver(string[] titresContacts)
    {
        bool okP = Principale != IntPtr.Zero && Native.IsWindow(Principale);
        bool okC = Contacts != IntPtr.Zero && Native.IsWindow(Contacts);
        if (okP && okC) return true;
        long t = Environment.TickCount64;
        if (t < prochainScan) return okP;
        prochainScan = t + 1000;
        if (!okP) Principale = IntPtr.Zero;
        if (!okC) Contacts = IntPtr.Zero;

        foreach (var f in Lister())
        {
            if (f.Classe != "SDL_app" || f.Proprietaire != IntPtr.Zero) continue;
            if (!okP && f.Titre == "Steam" && (Principale == IntPtr.Zero || f.Visible)) Principale = f.Hwnd;
            if (!okC && titresContacts.Contains(f.Titre) && (Contacts == IntPtr.Zero || f.Visible)) Contacts = f.Hwnd;
        }
        return Principale != IntPtr.Zero;
    }
}
