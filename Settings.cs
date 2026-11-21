using System.Text.Json;

namespace ScreenAI;

class KeyEntry
{
    public string Provider { get; set; } = "";   // provider id, see Providers.All
    public string Key { get; set; } = "";
    public string BaseUrl { get; set; } = "";    // only for "custom"
}

class Settings
{
    public List<KeyEntry> Keys { get; set; } = new();
    public bool UseOllama { get; set; }
    // "<provider>:<model id>"
    public string FastModel { get; set; } = "openrouter:google/gemini-2.5-flash";
    public string SmartModel { get; set; } = "openrouter:google/gemini-2.5-pro";
    public string Prompt { get; set; } = DefaultPrompt;
    // legacy (v1): single OpenRouter key
    public string ApiKey { get; set; }

    public const string DefaultPrompt =
        "You see a screenshot. Find the test question / problem / task on it and give the CORRECT answer, " +
        "fast and compact, ready to copy. Answer in the language of the question. No intro, no restating the question.\n" +
        "Format rules:\n" +
        "- Multiple-choice test, ONE question on screen: write only the correct option as \"<option number/letter>) <option text>\", " +
        "e.g. \"3) Київ\". Do NOT write the question number. If several options are correct, put each on its own line.\n" +
        "- Multiple-choice test, SEVERAL questions on screen: one line per question: " +
        "\"Питання <question number> — (<option number/letter>) <option text>\", e.g. \"Питання 1 — (3) Київ\".\n" +
        "- Open question: the answer in 1-3 short sentences.\n" +
        "- Math/physics/chemistry problem: short solution steps (one per line), then a final line \"Відповідь: ...\" with units.\n" +
        "- The text is shown in a plain-text window: NEVER use LaTeX, $...$, Markdown, ** or #. " +
        "Write formulas with Unicode: x², x³, xⁿ, a₁, √(x+1), ∛, π, ∞, ±, ≈, ≠, ≤, ≥, ·, ×, ÷, °, Δ, ∑, ∫, →, α β γ λ ω, " +
        "fractions as (a+b)/(c−d), e.g. v = s/t, F = m·a, x₁,₂ = (−b ± √D)/(2a).\n" +
        "- Code task: just the code.";

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenAI");
    static string FilePath => Path.Combine(Dir, "settings.json");

    public bool HasAnyProvider => Keys.Count > 0 || UseOllama;

    public static Settings Load()
    {
        Settings s = null;
        try
        {
            if (File.Exists(FilePath))
                s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
        }
        catch { }
        s ??= new Settings();
        s.Keys ??= new();

        // migrate v1
        if (!string.IsNullOrWhiteSpace(s.ApiKey))
        {
            if (!s.Keys.Any(k => k.Provider == "openrouter"))
                s.Keys.Add(new KeyEntry { Provider = "openrouter", Key = s.ApiKey.Trim() });
            s.ApiKey = null;
        }
        s.FastModel = FixRef(s.FastModel);
        s.SmartModel = FixRef(s.SmartModel);
        if (string.IsNullOrWhiteSpace(s.Prompt) || s.Prompt.Contains("<question number>) <option")
            || s.Prompt.StartsWith("Look at the screenshot. Find the question"))
            s.Prompt = DefaultPrompt;
        return s;
    }

    // old values were bare OpenRouter model ids like "google/gemini-2.5-pro"
    static string FixRef(string r)
    {
        if (string.IsNullOrWhiteSpace(r)) return "";
        var (p, _) = SplitRef(r);
        return p == null ? "openrouter:" + r : r;
    }

    public static (string provider, string model) SplitRef(string r)
    {
        int i = r?.IndexOf(':') ?? -1;
        if (i <= 0) return (null, r);
        var p = r[..i];
        if (Providers.Get(p) == null) return (null, r);
        return (p, r[(i + 1)..]);
    }

    public KeyEntry EntryFor(string provider)
    {
        if (provider == "ollama")
            return UseOllama ? new KeyEntry { Provider = "ollama", Key = "ollama" } : null;
        return Keys.FirstOrDefault(k => k.Provider == provider);
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
