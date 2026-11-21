using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ScreenAI;

record ProviderDef(string Id, string Name, string BaseUrl, bool VisionApi = true);

static class Providers
{
    public static readonly ProviderDef[] All =
    {
        new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1"),
        new("openai", "OpenAI", "https://api.openai.com/v1"),
        new("anthropic", "Claude (Anthropic)", "https://api.anthropic.com/v1"),
        new("gemini", "Gemini (Google)", "https://generativelanguage.googleapis.com/v1beta/openai"),
        new("xai", "Grok (xAI)", "https://api.x.ai/v1"),
        new("groq", "Groq", "https://api.groq.com/openai/v1"),
        new("mistral", "Mistral", "https://api.mistral.ai/v1"),
        new("together", "Together", "https://api.together.xyz/v1"),
        new("fireworks", "Fireworks", "https://api.fireworks.ai/inference/v1"),
        new("deepseek", "DeepSeek", "https://api.deepseek.com/v1", VisionApi: false),
        new("ollama", "Ollama (локально)", "http://localhost:11434/v1"),
        new("custom", "Свій (OpenAI-сумісний)", ""),
    };

    public static ProviderDef Get(string id) => All.FirstOrDefault(p => p.Id == id);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    static string BaseUrl(KeyEntry e) =>
        (e.Provider == "custom" ? e.BaseUrl : Get(e.Provider)?.BaseUrl ?? "").TrimEnd('/');

    // ---------- key detection ----------

    /// Returns provider id for a key, or null. Prefix first, then probing the ambiguous ones.
    public static async Task<string> Detect(string key)
    {
        key = key.Trim();
        if (key.StartsWith("sk-or-")) return "openrouter";
        if (key.StartsWith("sk-ant-")) return "anthropic";
        if (key.StartsWith("AIza")) return "gemini";
        if (key.StartsWith("xai-")) return "xai";
        if (key.StartsWith("gsk_")) return "groq";
        if (key.StartsWith("fw_")) return "fireworks";
        if (key.StartsWith("tgp_")) return "together";
        if (key.StartsWith("sk-proj-") || key.StartsWith("sk-svcacct-") || key.StartsWith("sk-admin-")) return "openai";

        var candidates = key.StartsWith("sk-")
            ? new[] { "openai", "deepseek" }
            : new[] { "mistral", "together", "openai" };
        foreach (var p in candidates)
            if (await Probe(new KeyEntry { Provider = p, Key = key })) return p;
        return null;
    }

    static async Task<bool> Probe(KeyEntry e)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl(e) + "/models");
            Auth(req, e);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var resp = await Http.SendAsync(req, cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    static void Auth(HttpRequestMessage req, KeyEntry e)
    {
        if (e.Provider == "anthropic")
        {
            req.Headers.Add("x-api-key", e.Key);
            req.Headers.Add("anthropic-version", "2023-06-01");
        }
        else
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", e.Key);
        }
        if (e.Provider == "openrouter") req.Headers.Add("X-Title", "ScreenAI");
    }

    static async Task<JsonNode> GetJson(string url, KeyEntry e, Action<HttpRequestMessage> extra = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (extra != null) extra(req); else Auth(req, e);
        using var resp = await Http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{(int)resp.StatusCode}: {ErrorText(text)}");
        return JsonNode.Parse(text);
    }

    static string Str(JsonNode n) { try { return n?.GetValue<string>(); } catch { return n?.ToString(); } }

    static bool HasImage(JsonNode arr) =>
        arr is JsonArray a && a.Any(x => Str(x) == "image");

    // ---------- model lists (only models that can read images) ----------

    public static async Task<List<string>> ListModels(KeyEntry e)
    {
        var baseUrl = BaseUrl(e);
        var result = new List<(string id, long created)>();

        switch (e.Provider)
        {
            case "openrouter":
            {
                var j = await GetJson(baseUrl + "/models", e);
                foreach (var m in j?["data"]?.AsArray() ?? new JsonArray())
                    if (HasImage(m?["architecture"]?["input_modalities"]))
                        result.Add((Str(m["id"]), 0)); // API order = newest first
                return result.Select(x => x.id).ToList();
            }
            case "anthropic":
            {
                var j = await GetJson(baseUrl + "/models?limit=1000", e);
                return (j?["data"]?.AsArray() ?? new JsonArray()).Select(m => Str(m?["id"])).Where(x => x != null).ToList();
            }
            case "gemini":
            {
                var j = await GetJson("https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000", e,
                    r => r.Headers.Add("x-goog-api-key", e.Key));
                var list = new List<string>();
                foreach (var m in j?["models"]?.AsArray() ?? new JsonArray())
                {
                    var id = Str(m?["name"])?.Replace("models/", "");
                    var methods = m?["supportedGenerationMethods"] as JsonArray;
                    if (id == null || !id.StartsWith("gemini") || methods == null
                        || !methods.Any(x => Str(x) == "generateContent")) continue;
                    if (Regex.IsMatch(id, "embedding|tts|aqa|live|native-audio|image-generation|computer-use")) continue;
                    list.Add(id);
                }
                return list.OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            }
            case "xai":
            {
                try
                {
                    var j = await GetJson(baseUrl + "/language-models", e);
                    var list = (j?["models"]?.AsArray() ?? new JsonArray())
                        .Where(m => HasImage(m?["input_modalities"])).Select(m => Str(m?["id"])).Where(x => x != null).ToList();
                    if (list.Count > 0) return list.OrderByDescending(x => x).ToList();
                }
                catch { }
                break; // fall back to generic list
            }
            case "mistral":
            {
                var j = await GetJson(baseUrl + "/models", e);
                return (j?["data"]?.AsArray() ?? new JsonArray())
                    .Where(m => m?["capabilities"]?["vision"]?.GetValue<bool>() == true)
                    .Select(m => Str(m?["id"])).Where(x => x != null).Distinct().OrderBy(x => x).ToList();
            }
            case "ollama":
            {
                var j = await GetJson("http://localhost:11434/api/tags", e);
                return (j?["models"]?.AsArray() ?? new JsonArray()).Select(m => Str(m?["name"])).Where(x => x != null).ToList();
            }
        }

        // generic OpenAI-compatible /models
        var g = await GetJson(baseUrl + "/models", e);
        var arr = g?["data"] as JsonArray ?? g as JsonArray ?? new JsonArray();
        foreach (var m in arr)
        {
            var id = Str(m?["id"]);
            if (id == null) continue;
            long created = 0;
            try { created = m?["created"]?.GetValue<long>() ?? 0; } catch { }
            result.Add((id, created));
        }
        var ids = result.OrderByDescending(x => x.created).ThenBy(x => x.id).Select(x => x.id).ToList();

        List<string> vision = e.Provider switch
        {
            "openai" => ids.Where(id =>
                    // any current or future family: gpt-4o, gpt-4.1, gpt-5.x, gpt-6…, o1/o3/o4/o5…, chatgpt-*
                    Regex.IsMatch(id, @"^(gpt-(4o|4\.1|([5-9]|\d{2,}))|o\d+|chatgpt-)")
                    && !Regex.IsMatch(id, "audio|realtime|tts|transcribe|search|image|codex|-pro|deep-research"))
                .ToList(),
            "groq" or "together" or "fireworks" => ids.Where(id =>
                Regex.IsMatch(id, @"vision|-vl|vl-|omni|maverick|scout|pixtral|llama-([4-9]|\d{2,})|gemma-([3-9]|\d{2,})", RegexOptions.IgnoreCase)).ToList(),
            "xai" => ids.Where(id => Regex.IsMatch(id, @"vision|grok-([4-9]|\d{2,})")).ToList(),
            "deepseek" => new List<string>(),
            _ => ids,
        };
        return vision.Count > 0 || e.Provider == "deepseek" ? vision : ids;
    }

    // ---------- chat ----------

    public static async Task<string> Ask(KeyEntry e, string model, string prompt, string imageB64, CancellationToken ct)
    {
        return e.Provider == "anthropic"
            ? await AskAnthropic(e, model, prompt, imageB64, ct)
            : await AskOpenAi(e, model, prompt, imageB64, ct);
    }

    static async Task<string> AskOpenAi(KeyEntry e, string model, string prompt, string imageB64, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = prompt },
                        new JsonObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject { ["url"] = "data:image/jpeg;base64," + imageB64 }
                        }
                    }
                }
            }
        };
        var json = await Post(BaseUrl(e) + "/chat/completions", e, body, ct);
        var content = json?["choices"]?[0]?["message"]?["content"];
        string answer = content is JsonArray parts
            ? string.Concat(parts.Select(p => Str(p?["text"]) ?? ""))
            : Str(content);
        return Clean(answer);
    }

    static async Task<string> AskAnthropic(KeyEntry e, string model, string prompt, string imageB64, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = 4096,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "image",
                            ["source"] = new JsonObject
                            {
                                ["type"] = "base64", ["media_type"] = "image/jpeg", ["data"] = imageB64
                            }
                        },
                        new JsonObject { ["type"] = "text", ["text"] = prompt }
                    }
                }
            }
        };
        var json = await Post(BaseUrl(e) + "/messages", e, body, ct);
        var text = string.Concat((json?["content"]?.AsArray() ?? new JsonArray())
            .Where(b => Str(b?["type"]) == "text").Select(b => Str(b?["text"]) ?? ""));
        return Clean(text);
    }

    static async Task<JsonNode> Post(string url, KeyEntry e, JsonObject body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        Auth(req, e);
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        JsonNode json = null;
        try { json = JsonNode.Parse(text); } catch { }
        if (!resp.IsSuccessStatusCode || json?["error"] != null)
            throw new Exception($"{(int)resp.StatusCode}: {ErrorText(text)}");
        return json;
    }

    static string ErrorText(string text)
    {
        try
        {
            var j = JsonNode.Parse(text);
            var err = j?["error"];
            var msg = Str(err?["message"]) ?? (err is JsonValue ? Str(err) : null) ?? Str(j?["message"]);
            if (msg != null) return msg;
        }
        catch { }
        return text.Length > 300 ? text[..300] : text;
    }

    static string Clean(string answer)
    {
        answer = answer?.Trim();
        if (string.IsNullOrEmpty(answer)) return "(порожня відповідь)";
        // safety net in case the model still used Markdown/LaTeX markers
        answer = answer.Replace("**", "").Replace("$$", "").Replace("\\(", "").Replace("\\)", "")
                       .Replace("\\[", "").Replace("\\]", "").Replace("\\cdot", "·").Replace("\\times", "×")
                       .Replace("\\pm", "±").Replace("\\le", "≤").Replace("\\ge", "≥").Replace("\\pi", "π");
        answer = Regex.Replace(answer, @"(?m)^#+\s*", "");
        answer = Regex.Replace(answer, @"\$([^$\n]+)\$", "$1");
        return answer;
    }
}
