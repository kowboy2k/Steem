using System.Runtime.InteropServices;
using System.Text;

namespace Steem;

[ComImport, Guid("56FDF342-FD6D-11d0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ITaskbarList
{
    void HrInit();
    void AddTab(IntPtr h);
    void DeleteTab(IntPtr h);
    void ActivateTab(IntPtr h);
    void SetActiveAlt(IntPtr h);
}

[ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
class TaskbarList { }

// Bouton de barre des tâches retiré sans toucher au style de la fenêtre :
// une fenêtre-outil ne déclenche plus le clignotement des nouveaux messages.
sealed class BarreDesTaches
{
    readonly ITaskbarList liste;

    public BarreDesTaches()
    {
        try { liste = (ITaskbarList)new TaskbarList(); liste.HrInit(); }
        catch { liste = null; }
    }

    public void Retirer(IntPtr h) { try { liste?.DeleteTab(h); } catch { } }
    public void Remettre(IntPtr h) { try { liste?.AddTab(h); } catch { } }
}

static class Native
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    // Fenêtre principale (racine) réellement visible sous le point : le bureau, une autre appli… ou Steam.
    public static IntPtr RacineSous(Point p) { var h = WindowFromPoint(p); return h == IntPtr.Zero ? h : GetAncestor(h, 2); }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT p);
    [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT p);

    [StructLayout(LayoutKind.Sequential)]
    struct WINDOWPLACEMENT { public int length, flags, showCmd, minX, minY, maxX, maxY; public RECT normal; }

    // Sort une fenêtre agrandie directement dans « r » (écran), sans passer par son ancienne taille.
    public static void RestaurerDans(IntPtr h, Rectangle r)
    {
        var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(h, ref wp)) return;
        var ecran = Screen.FromHandle(h);
        int dx = ecran.WorkingArea.Left - ecran.Bounds.Left, dy = ecran.WorkingArea.Top - ecran.Bounds.Top;
        wp.flags = 0;
        wp.showCmd = 1; // SW_SHOWNORMAL
        wp.normal = new RECT { Left = r.Left - dx, Top = r.Top - dy, Right = r.Right - dx, Bottom = r.Bottom - dy };
        SetWindowPlacement(h, ref wp);
    }
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool lier);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    // Met une fenêtre au premier plan depuis un processus en arrière-plan (sinon Windows ne fait que clignoter le bouton).
    public static void Activer(IntPtr h)
    {
        uint fg = GetWindowThreadProcessId(GetForegroundWindow(), out _), moi = GetCurrentThreadId();
        bool lie = fg != 0 && fg != moi && AttachThreadInput(moi, fg, true);
        SetForegroundWindow(h);
        if (lie) AttachThreadInput(moi, fg, false);
    }

    // Agrandit une fenêtre d'un autre processus (ShowWindowAsync(SW_MAXIMIZE) est ignoré par Steam).
    public static void Agrandir(IntPtr h)
    {
        var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(h, ref wp)) return;
        wp.flags = 0;
        wp.showCmd = 3; // SW_SHOWMAXIMIZED
        SetWindowPlacement(h, ref wp);
    }
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern bool RegisterShellHookWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool DeregisterShellHookWindow(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);

    const uint GW_HWNDNEXT = 2, GW_HWNDPREV = 3;
    public const uint GW_OWNER = 4;
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10,
               SWP_SHOWWINDOW = 0x40, SWP_NOOWNERZORDER = 0x200, SWP_ASYNCWINDOWPOS = 0x4000;
    static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public static readonly IntPtr HWND_BOTTOM = new(1);

    public static string Titre(IntPtr h) { var s = new StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); }
    public static string Classe(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }

    public static Rectangle Rect(IntPtr h)
    {
        GetWindowRect(h, out var r);
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    // Bords réellement visibles (sans les bordures invisibles de redimensionnement).
    public static Rectangle Visuel(IntPtr h)
    {
        if (DwmGetWindowAttribute(h, 9, out RECT r, Marshal.SizeOf<RECT>()) == 0)
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        return Rect(h);
    }

    // Rectangle de fenêtre à donner pour que la partie visible occupe « visuel ».
    public static Rectangle RectPourVisuel(IntPtr h, Rectangle visuel)
    {
        var w = Rect(h); var v = Visuel(h);
        return Rectangle.FromLTRB(visuel.Left - (v.Left - w.Left), visuel.Top - (v.Top - w.Top),
                                  visuel.Right + (w.Right - v.Right), visuel.Bottom + (w.Bottom - v.Bottom));
    }

    public static bool Masquee(IntPtr h) =>
        DwmGetWindowAttribute(h, 14, out int c, 4) == 0 && c != 0;

    public static float Echelle(IntPtr h) { var d = GetDpiForWindow(h); return d == 0 ? 1f : d / 96f; }

    static bool Topmost(IntPtr h) => (GetWindowLong(h, -20) & 0x8) != 0;

    // Fenêtres étrangères : appel asynchrone pour ne jamais rester bloqué si Steam ne répond plus.
    public static void Placer(IntPtr h, Rectangle r, bool etrangere) =>
        SetWindowPos(h, IntPtr.Zero, r.X, r.Y, r.Width, r.Height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | (etrangere ? SWP_ASYNCWINDOWPOS : SWP_SHOWWINDOW));

    public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr h, int obj, int enfant, uint thread, uint temps);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);

    public static void PlacerSous(IntPtr h, IntPtr apres) =>
        SetWindowPos(h, apres, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS);

    // Une fenêtre compte pour l'ordre si elle est visible et recouvre la cible.
    static bool Compte(IntPtr p, IntPtr cible, Rectangle zone, Func<IntPtr, bool> ignorer) =>
        IsWindowVisible(p) && !ignorer(p) && GetWindow(p, GW_OWNER) != cible && Visuel(p).IntersectsWith(zone);

    public static bool JusteAuDessus(IntPtr h, IntPtr cible, Func<IntPtr, bool> ignorer)
    {
        var zone = Visuel(cible);
        for (var p = GetWindow(cible, GW_HWNDPREV); p != IntPtr.Zero; p = GetWindow(p, GW_HWNDPREV))
        {
            if (p == h) return true;
            if (Compte(p, cible, zone, ignorer)) return false;
        }
        return false;
    }

    public static bool JusteEnDessous(IntPtr h, IntPtr cible, Func<IntPtr, bool> ignorer)
    {
        var zone = Visuel(cible);
        for (var p = GetWindow(cible, GW_HWNDNEXT); p != IntPtr.Zero; p = GetWindow(p, GW_HWNDNEXT))
        {
            if (p == h) return true;
            if (Compte(p, cible, zone, ignorer)) return false;
        }
        return false;
    }

    public static void MettreAuDessus(IntPtr h, IntPtr cible, Func<IntPtr, bool> ignorer)
    {
        var p = GetWindow(cible, GW_HWNDPREV);
        while (p != IntPtr.Zero && (p == h || ignorer(p))) p = GetWindow(p, GW_HWNDPREV);
        PlacerSous(h, p == IntPtr.Zero || Topmost(p) ? HWND_TOP : p);
    }
}
