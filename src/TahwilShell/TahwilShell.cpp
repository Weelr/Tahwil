// Windows 11 context menu for Tahwil: "Convert to" with a submenu of formats.
//
// Explorer loads this DLL through the sparse package (see packaging/AppxManifest.xml).
// The format table is not hard-coded here: it is read from menu.txt, which Tahwil.exe
// writes from its own Formats table, so the menu and the converter always agree.
//
// Layout on disk:   <install>\Tahwil.exe, <install>\menu.txt, <install>\shell\<version>\TahwilShell.dll

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <new>
#include <string>
#include <vector>
#include <algorithm>

// {4CAAAB8A-843E-41B0-8367-842C1C460C0E}
static const CLSID CLSID_TahwilCommand = {0x4caaab8a, 0x843e, 0x41b0, {0x83, 0x67, 0x84, 0x2c, 0x1c, 0x46, 0x0c, 0x0e}};

static HMODULE g_module = nullptr;
static LONG g_objects = 0;

// ---------------------------------------------------------------- format table

struct TargetDef { std::wstring cat, ext, ar, en; bool audio; };
struct CategoryDef { std::wstring key; std::vector<std::wstring> sources; };

struct Table
{
    bool loaded = false;
    std::wstring title = L"Convert to", custom = L"Custom…";
    std::vector<CategoryDef> cats;
    std::vector<TargetDef> targets;
    std::vector<std::pair<std::wstring, std::wstring>> aliases;
};

static Table g_table;
static std::wstring g_installDir;
static bool g_arabic = false;

static std::vector<std::wstring> Split(const std::wstring& s, wchar_t sep)
{
    std::vector<std::wstring> out;
    size_t start = 0;
    for (size_t i = 0; i <= s.size(); i++)
        if (i == s.size() || s[i] == sep) { out.push_back(s.substr(start, i - start)); start = i + 1; }
    return out;
}

static std::wstring Lower(std::wstring s)
{
    CharLowerBuffW(s.data(), (DWORD)s.size());
    return s;
}

static void LoadTable()
{
    if (g_table.loaded) return;
    g_table.loaded = true;

    wchar_t path[MAX_PATH];
    GetModuleFileNameW(g_module, path, MAX_PATH);
    PathRemoveFileSpecW(path);           // ...\shell\<ver>
    PathRemoveFileSpecW(path);           // ...\shell
    PathRemoveFileSpecW(path);           // install dir
    g_installDir = path;
    g_arabic = PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_ARABIC;

    std::wstring file = g_installDir + L"\\menu.txt";
    HANDLE h = CreateFileW(file.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);
    if (h == INVALID_HANDLE_VALUE) return;
    DWORD size = GetFileSize(h, nullptr), read = 0;
    std::string bytes(size, '\0');
    ReadFile(h, bytes.data(), size, &read, nullptr);
    CloseHandle(h);
    if (bytes.size() >= 3 && (unsigned char)bytes[0] == 0xEF) bytes.erase(0, 3);   // UTF-8 BOM

    int n = MultiByteToWideChar(CP_UTF8, 0, bytes.data(), (int)bytes.size(), nullptr, 0);
    std::wstring text(n, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, bytes.data(), (int)bytes.size(), text.data(), n);

    for (auto& raw : Split(text, L'\n'))
    {
        std::wstring line = raw;
        if (!line.empty() && line.back() == L'\r') line.pop_back();
        auto f = Split(line, L'|');
        if (f.empty()) continue;
        if (f[0] == L"L" && f.size() >= 4)
        {
            std::wstring v = g_arabic ? f[2] : f[3];
            if (f[1] == L"title") g_table.title = v; else if (f[1] == L"custom") g_table.custom = v;
        }
        else if (f[0] == L"C" && f.size() >= 3)
            g_table.cats.push_back({f[1], Split(f[2], L',')});
        else if (f[0] == L"T" && f.size() >= 6)
            g_table.targets.push_back({f[1], f[2], f[4], f[5], f[3] == L"1"});
        else if (f[0] == L"A" && f.size() >= 2)
            for (auto& pair : Split(f[1], L','))
            {
                auto kv = Split(pair, L'=');
                if (kv.size() == 2) g_table.aliases.push_back({kv[0], kv[1]});
            }
    }
}

static const CategoryDef* CategoryOfExt(const std::wstring& ext)
{
    for (auto& c : g_table.cats)
        if (std::find(c.sources.begin(), c.sources.end(), ext) != c.sources.end()) return &c;
    return nullptr;
}

static std::wstring Canonical(const std::wstring& ext)
{
    for (auto& a : g_table.aliases) if (a.first == ext) return a.second;
    return ext;
}

static bool CatHasTarget(const std::wstring& cat, const TargetDef& t)
{
    for (auto& x : g_table.targets)
        if (x.cat == cat && x.ext == t.ext) return true;   // same rule as Formats.TargetsFor
    return false;
}

// ---------------------------------------------------------------- selection

// What the current selection can convert to. Mirrors Formats.TargetsFor in the app.
struct Selection
{
    std::vector<std::wstring> paths;
    std::wstring primary;
    std::vector<const TargetDef*> visible;
};

static Selection ReadSelection(IShellItemArray* items)
{
    Selection sel;
    if (!items) return sel;
    LoadTable();
    DWORD count = 0;
    if (FAILED(items->GetCount(&count)) || count == 0) return sel;

    std::vector<std::wstring> cats, exts;
    for (DWORD i = 0; i < count; i++)
    {
        IShellItem* item = nullptr;
        if (FAILED(items->GetItemAt(i, &item))) return {};
        PWSTR p = nullptr;
        HRESULT hr = item->GetDisplayName(SIGDN_FILESYSPATH, &p);
        item->Release();
        if (FAILED(hr) || !p) return {};
        std::wstring path = p;
        CoTaskMemFree(p);
        if (GetFileAttributesW(path.c_str()) & FILE_ATTRIBUTE_DIRECTORY) return {};

        const wchar_t* dot = PathFindExtensionW(path.c_str());
        std::wstring ext = Lower(*dot ? dot + 1 : L"");
        const CategoryDef* c = CategoryOfExt(ext);
        if (!c) return {};
        sel.paths.push_back(path);
        cats.push_back(c->key);
        exts.push_back(Canonical(ext));
    }

    sel.primary = cats[0];
    bool sameExt = std::all_of(exts.begin(), exts.end(), [&](auto& e) { return e == exts[0]; });
    for (auto& t : g_table.targets)
    {
        if (t.cat != sel.primary) continue;
        if (sameExt && !t.audio && t.ext == exts[0]) continue;
        bool all = std::all_of(cats.begin(), cats.end(), [&](auto& c) { return CatHasTarget(c, t); });
        if (all) sel.visible.push_back(&t);
    }
    return sel;
}

// Explorer asks every sub-command for its state; cache by array so a large selection is read once.
static Selection g_cache;
static IShellItemArray* g_cacheKey = nullptr;
static DWORD g_cacheTick = 0;

static const Selection& Current(IShellItemArray* items, bool refresh = false)
{
    DWORD now = GetTickCount();
    if (refresh || items != g_cacheKey || now - g_cacheTick > 1500)
    {
        g_cache = ReadSelection(items);
        g_cacheKey = items;
    }
    g_cacheTick = now;
    return g_cache;
}

static HRESULT Launch(const Selection& sel, const std::wstring& args)
{
    // File list goes through a temp file: command lines are too short for big selections.
    wchar_t tmp[MAX_PATH], list[MAX_PATH];
    GetTempPathW(MAX_PATH, tmp);
    GetTempFileNameW(tmp, L"thw", 0, list);
    std::string utf8;
    for (auto& p : sel.paths)
    {
        int n = WideCharToMultiByte(CP_UTF8, 0, p.c_str(), (int)p.size(), nullptr, 0, nullptr, nullptr);
        std::string s(n, '\0');
        WideCharToMultiByte(CP_UTF8, 0, p.c_str(), (int)p.size(), s.data(), n, nullptr, nullptr);
        utf8 += s + "\n";
    }
    HANDLE h = CreateFileW(list, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, 0, nullptr);
    if (h == INVALID_HANDLE_VALUE) return E_FAIL;
    DWORD written = 0;
    WriteFile(h, utf8.data(), (DWORD)utf8.size(), &written, nullptr);
    CloseHandle(h);

    std::wstring exe = g_installDir + L"\\Tahwil.exe";
    std::wstring cmd = L"\"" + exe + L"\" convert " + args + L" --list \"" + list + L"\"";
    STARTUPINFOW si = {sizeof(si)};
    PROCESS_INFORMATION pi = {};
    if (!CreateProcessW(exe.c_str(), cmd.data(), nullptr, nullptr, FALSE, 0, nullptr, g_installDir.c_str(), &si, &pi))
        return HRESULT_FROM_WIN32(GetLastError());
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return S_OK;
}

// ---------------------------------------------------------------- commands

enum class Kind { Root, Target, AudioSeparator, CustomSeparator, Custom };

class Command;
static std::vector<Command*> MakeSubCommands();

class Command final : public IExplorerCommand
{
    LONG _ref = 1;
    Kind _kind;
    const TargetDef* _target;

public:
    Command(Kind kind, const TargetDef* target = nullptr) : _kind(kind), _target(target) { InterlockedIncrement(&g_objects); }
    ~Command() { InterlockedDecrement(&g_objects); }

    // IUnknown
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (riid == IID_IUnknown || riid == IID_IExplorerCommand) { *ppv = static_cast<IExplorerCommand*>(this); AddRef(); return S_OK; }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_ref); }
    IFACEMETHODIMP_(ULONG) Release() override
    {
        LONG r = InterlockedDecrement(&_ref);
        if (r == 0) delete this;
        return r;
    }

    // IExplorerCommand
    IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* name) override
    {
        LoadTable();
        std::wstring t;
        switch (_kind)
        {
            case Kind::Root: t = g_table.title; break;
            case Kind::Custom: t = g_table.custom; break;
            case Kind::Target: t = g_arabic ? _target->ar : _target->en; break;
            default: *name = nullptr; return E_NOTIMPL;
        }
        return SHStrDupW(t.c_str(), name);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
    {
        if (_kind != Kind::Root) { *icon = nullptr; return E_NOTIMPL; }
        LoadTable();
        return SHStrDupW((g_installDir + L"\\Tahwil.exe,0").c_str(), icon);
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override { *tip = nullptr; return E_NOTIMPL; }

    IFACEMETHODIMP GetCanonicalName(GUID* guid) override { *guid = GUID_NULL; return E_NOTIMPL; }

    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
    {
        // The root is asked first each time the menu opens, so it re-reads the selection.
        const Selection& sel = Current(items, _kind == Kind::Root);
        bool any = !sel.visible.empty();
        bool show = false;
        switch (_kind)
        {
            case Kind::Root:
            case Kind::Custom:
            case Kind::CustomSeparator: show = any; break;
            case Kind::Target: show = std::find(sel.visible.begin(), sel.visible.end(), _target) != sel.visible.end(); break;
            case Kind::AudioSeparator:
                show = std::any_of(sel.visible.begin(), sel.visible.end(), [](auto t) { return t->audio; }) &&
                       std::any_of(sel.visible.begin(), sel.visible.end(), [](auto t) { return !t->audio; });
                break;
        }
        *state = show ? ECS_ENABLED : ECS_HIDDEN;
        return S_OK;
    }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
    {
        const Selection& sel = Current(items);
        if (sel.visible.empty()) return S_OK;
        switch (_kind)
        {
            case Kind::Target:
                return Launch(sel, L"--to " + _target->ext + (_target->audio ? L" --audio" : L""));
            case Kind::Custom:
                return Launch(sel, L"--custom --to " + sel.visible[0]->ext + (sel.visible[0]->audio ? L" --audio" : L""));
            default:
                return S_OK;
        }
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
    {
        switch (_kind)
        {
            case Kind::Root: *flags = ECF_HASSUBCOMMANDS; break;
            case Kind::AudioSeparator:
            case Kind::CustomSeparator: *flags = ECF_ISSEPARATOR; break;
            default: *flags = ECF_DEFAULT; break;
        }
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** out) override;
};

class CommandEnum final : public IEnumExplorerCommand
{
    LONG _ref = 1;
    std::vector<Command*> _items;
    size_t _pos = 0;

public:
    explicit CommandEnum(std::vector<Command*> items) : _items(std::move(items)) { InterlockedIncrement(&g_objects); }
    ~CommandEnum()
    {
        for (auto c : _items) c->Release();
        InterlockedDecrement(&g_objects);
    }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (riid == IID_IUnknown || riid == IID_IEnumExplorerCommand) { *ppv = static_cast<IEnumExplorerCommand*>(this); AddRef(); return S_OK; }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_ref); }
    IFACEMETHODIMP_(ULONG) Release() override
    {
        LONG r = InterlockedDecrement(&_ref);
        if (r == 0) delete this;
        return r;
    }

    IFACEMETHODIMP Next(ULONG count, IExplorerCommand** out, ULONG* fetched) override
    {
        ULONG n = 0;
        while (n < count && _pos < _items.size())
        {
            out[n] = _items[_pos++];
            out[n]->AddRef();
            n++;
        }
        if (fetched) *fetched = n;
        return n == count ? S_OK : S_FALSE;
    }
    IFACEMETHODIMP Skip(ULONG count) override { _pos = std::min(_items.size(), _pos + count); return S_OK; }
    IFACEMETHODIMP Reset() override { _pos = 0; return S_OK; }
    IFACEMETHODIMP Clone(IEnumExplorerCommand** out) override { *out = nullptr; return E_NOTIMPL; }
};

// Every possible entry for every category; GetState hides the ones that don't fit the selection.
static std::vector<Command*> MakeSubCommands()
{
    LoadTable();
    std::vector<Command*> items;
    for (auto& cat : g_table.cats)
    {
        bool sepAdded = false;
        for (auto& t : g_table.targets)
        {
            if (t.cat != cat.key) continue;
            if (t.audio && !sepAdded) { items.push_back(new (std::nothrow) Command(Kind::AudioSeparator)); sepAdded = true; }
            items.push_back(new (std::nothrow) Command(Kind::Target, &t));
        }
    }
    items.push_back(new (std::nothrow) Command(Kind::CustomSeparator));
    items.push_back(new (std::nothrow) Command(Kind::Custom));
    items.erase(std::remove(items.begin(), items.end(), nullptr), items.end());
    return items;
}

IFACEMETHODIMP Command::EnumSubCommands(IEnumExplorerCommand** out)
{
    if (_kind != Kind::Root) { *out = nullptr; return E_NOTIMPL; }
    *out = new (std::nothrow) CommandEnum(MakeSubCommands());
    return *out ? S_OK : E_OUTOFMEMORY;
}

// ---------------------------------------------------------------- COM plumbing

class Factory final : public IClassFactory
{
    LONG _ref = 1;

public:
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (riid == IID_IUnknown || riid == IID_IClassFactory) { *ppv = static_cast<IClassFactory*>(this); AddRef(); return S_OK; }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_ref); }
    IFACEMETHODIMP_(ULONG) Release() override
    {
        LONG r = InterlockedDecrement(&_ref);
        if (r == 0) delete this;
        return r;
    }
    IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override
    {
        *ppv = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        auto cmd = new (std::nothrow) Command(Kind::Root);
        if (!cmd) return E_OUTOFMEMORY;
        HRESULT hr = cmd->QueryInterface(riid, ppv);
        cmd->Release();
        return hr;
    }
    IFACEMETHODIMP LockServer(BOOL lock) override
    {
        if (lock) InterlockedIncrement(&g_objects); else InterlockedDecrement(&g_objects);
        return S_OK;
    }
};

extern "C" BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) { g_module = inst; DisableThreadLibraryCalls(inst); }
    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** ppv)
{
    *ppv = nullptr;
    if (clsid != CLSID_TahwilCommand) return CLASS_E_CLASSNOTAVAILABLE;
    auto f = new (std::nothrow) Factory();
    if (!f) return E_OUTOFMEMORY;
    HRESULT hr = f->QueryInterface(riid, ppv);
    f->Release();
    return hr;
}

STDAPI DllCanUnloadNow()
{
    return g_objects == 0 ? S_OK : S_FALSE;
}
