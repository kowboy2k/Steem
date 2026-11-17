using System.Runtime.InteropServices;

namespace Steem;

// Fenêtres de discussion Steam regroupées sous leur propre bouton de barre des tâches :
// on leur donne un AppUserModelID à part (propriété de fenêtre, lisible par l'Explorateur), retiré à la sortie.
static class Groupe
{
    public const string Discussions = "Steem.ChatsSteam"; // nouvel id le 17/11 : l'Explorateur avait mémorisé le logo Steam pour l'ancien (Steem.DiscussionsSteam)

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        void GetCount(out uint n);
        void GetAt(uint i, out PropertyKey k);
        void GetValue(ref PropertyKey k, out PropVariant v);
        void SetValue(ref PropertyKey k, ref PropVariant v);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct PropertyKey { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Explicit)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr p;
        [FieldOffset(16)] public IntPtr p2; // taille complète d'un PROPVARIANT en 64 bits
    }

    [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr h, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant v);

    static readonly Guid Fmt = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    static PropertyKey CleAppId = new() { fmtid = Fmt, pid = 5 };
    static PropertyKey CleCommande = new() { fmtid = Fmt, pid = 2 }, CleIcone = new() { fmtid = Fmt, pid = 3 }, CleNom = new() { fmtid = Fmt, pid = 4 };

    // Icône du bouton groupé : l'Explorateur la prend dans ce fichier (RelaunchIconResource), pas dans WM_SETICON,
    // sinon le bouton garde le logo Steam selon l'ordre de création (Kev, 17/11).
    static readonly string FichierIcone = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Steem", "Discussions.ico");

    static void Ecrire(IPropertyStore store, ref PropertyKey k, string valeur)
    {
        var v = new PropVariant();
        if (valeur != null) { v.vt = 31; v.p = Marshal.StringToCoTaskMemUni(valeur); } // VT_LPWSTR
        try { store.SetValue(ref k, ref v); }
        finally { if (valeur != null) PropVariantClear(ref v); }
    }

    // id = null : retire l'identifiant (la fenêtre rejoint de nouveau le bouton de Steam).
    public static bool Definir(IntPtr h, string id)
    {
        IPropertyStore store = null;
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(h, ref iid, out store) != 0 || store == null) return false;
            bool icone = id != null && PreparerIcone();
            Ecrire(store, ref CleCommande, icone ? "explorer.exe steam://open/friends" : null);
            Ecrire(store, ref CleNom, icone ? "Steam" : null);
            Ecrire(store, ref CleIcone, icone ? FichierIcone + ",0" : null);
            Ecrire(store, ref CleAppId, id);
            store.Commit();
            return true;
        }
        catch { return false; }
        finally { if (store != null) Marshal.ReleaseComObject(store); }
    }

    static bool PreparerIcone()
    {
        try
        {
            if (File.Exists(FichierIcone)) return true;
            Directory.CreateDirectory(Path.GetDirectoryName(FichierIcone));
            using var s = typeof(Groupe).Assembly.GetManifestResourceStream("Discussions.ico");
            using var f = File.Create(FichierIcone);
            s.CopyTo(f);
            return true;
        }
        catch { return false; }
    }

    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint ms, out IntPtr res);

    static readonly Dictionary<IntPtr, (IntPtr petite, IntPtr grande)> origines = [];

    static IntPtr Envoyer(IntPtr h, uint msg, int w, IntPtr l)
    {
        SendMessageTimeout(h, msg, w, l, 0x0002, 200, out var r); // SMTO_ABORTIFHUNG
        return r;
    }

    // Pose l'icône des discussions (WM_SETICON) si la fenêtre ne l'a pas déjà ; garde l'icône de Steam pour la sortie.
    public static void Iconiser(IntPtr h, IntPtr petite, IntPtr grande)
    {
        var actuelle = Envoyer(h, 0x7F, 0, IntPtr.Zero); // WM_GETICON, ICON_SMALL
        if (actuelle == petite) return;
        if (!origines.ContainsKey(h)) origines[h] = (actuelle, Envoyer(h, 0x7F, 1, IntPtr.Zero));
        Envoyer(h, 0x80, 0, petite); // WM_SETICON
        Envoyer(h, 0x80, 1, grande);
    }

    public static void RendreIcone(IntPtr h)
    {
        if (!origines.Remove(h, out var o)) return;
        Envoyer(h, 0x80, 0, o.petite);
        Envoyer(h, 0x80, 1, o.grande);
    }

    public static string Lire(IntPtr h)
    {
        IPropertyStore store = null;
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(h, ref iid, out store) != 0 || store == null) return null;
            store.GetValue(ref CleAppId, out var v);
            string s = v.vt == 31 ? Marshal.PtrToStringUni(v.p) : null;
            PropVariantClear(ref v);
            return s;
        }
        catch { return null; }
        finally { if (store != null) Marshal.ReleaseComObject(store); }
    }
}
