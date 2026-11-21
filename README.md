**English** · [Українська](README.uk.md)

# ScreenAI

A small always-on-top overlay for Windows. Press **O**, and it takes a
screenshot of the monitor under the cursor, sends it to an AI vision model and
shows the answer in the top-right corner of the screen.

Built for tests, problems and questions on screen: the default instruction
asks the model to find the task and answer it compactly. C# on .NET 9 and
WinForms, no dependencies beyond the framework. The interface is in Ukrainian.

## The idea worth explaining

### Two models race, the answer upgrades itself

Every capture goes to two models at once: a **fast** one and a **smart** one.
The fast model gets the same instruction plus "Be very short." Its answer
appears first, in yellow. When the smart model finishes, its answer replaces
the fast one, in white. The status line shows each model's elapsed time live.

If the smart model fails, the fast answer stays; if both fail, the error is
shown. Pressing **O** again cancels the request in flight and starts a new one.

### An overlay that is not in its own screenshot

The window is excluded from screen capture with `SetWindowDisplayAffinity`
(`WDA_EXCLUDEFROMCAPTURE`), so the previous answer never ends up in the next
screenshot, nor in any other screen recording. It is also click-through,
never takes focus and has no taskbar button: the window you are working in
stays active.

### Paste any key, it figures out whose it is

In Settings you paste an API key and ScreenAI detects the provider. Known
prefixes decide it directly (`sk-or-` OpenRouter, `sk-ant-` Anthropic, `AIza`
Gemini, `xai-` xAI, `gsk_` Groq, `fw_` Fireworks, `tgp_` Together, `sk-proj-`
OpenAI). An ambiguous key is tried against the `/models` endpoint of the likely
providers until one accepts it.

The model lists come from the providers themselves and are filtered to models
that accept images, so new models appear without an update.

## Providers

OpenRouter, OpenAI, Anthropic, Gemini, xAI (Grok), Groq, Mistral, Together,
Fireworks, a local Ollama (`localhost:11434`, no key), and any custom
OpenAI-compatible server by base URL. A DeepSeek key is recognised but
refused, because DeepSeek's API does not take images; the app suggests using
DeepSeek through OpenRouter instead.

The defaults are `google/gemini-2.5-flash` (fast) and `google/gemini-2.5-pro`
(smart) through OpenRouter. Any provider and model can be picked for either
slot.

## Keys

| Key | Action |
|---|---|
| `O` | screenshot and request; pressing again cancels the previous request |
| `H` | hide or show the overlay (a running request continues) |
| `Alt+K` | settings |
| `Alt+Q` | quit |

The keys work globally through a low-level keyboard hook. `O` and `H` are
still passed on to the active application, so typing a word with an "o" in
another window also starts a capture. They are ignored while the settings
window has focus.

The screenshot is downscaled to at most 1920 px wide and sent as JPEG.

The instruction for the models (answer format for tests, problems, formulas,
code) is editable in Settings and can be reset to the default.

## Privacy

- Settings, including API keys, are stored in **plain text** in
  `%APPDATA%\ScreenAI\settings.json`. Unhandled errors are appended to
  `%APPDATA%\ScreenAI\error.log`.
- A screenshot is sent only to the providers of the two models you selected,
  and only when you press **O**.
- When a key is added, it is sent to the detected provider to check it and
  load its models. A key without a recognisable prefix may be tried against
  up to three providers (OpenAI and DeepSeek for `sk-` keys; Mistral, Together
  and OpenAI otherwise) before one accepts it.
- There is no telemetry, no update check and no autostart.

## Running and building

Needs the .NET 9 SDK to build; the published build is framework-dependent, so
the .NET 9 Desktop Runtime must be installed to run it.

```powershell
dotnet run                               # run from source
dotnet publish -c Release                # single-file ScreenAI.exe (win-x64)
```

The published file lands in `bin\Release\net9.0-windows\win-x64\publish\`.
Only one copy runs at a time. While no key is set, or when started with
`--settings`, the settings window opens on start.

Builds: see Releases.

```
Program.cs        overlay, screen capture, global keyboard hook
Providers.cs      provider list, key detection, model lists, requests
Settings.cs       settings file and the default instruction
SettingsForm.cs   settings window
```

## License

License: not specified yet.

---

**Chekaliuk Dmytro** · [@ifoxp](https://github.com/ifoxp) ·
[ifoxp.top](https://ifoxp.top) · Telegram [@ifoxp](https://t.me/ifoxp) ·
Discord `ifoxp`
