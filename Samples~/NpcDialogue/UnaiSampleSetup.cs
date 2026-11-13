using System.Collections.Generic;
using System.Text;
using TMPro;
using UnAI.Config;
using UnAI.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TextCore.LowLevel;
#if UNAI_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace UnAI.Samples.NpcDialogue
{
    /// <summary>
    /// Shared sample plumbing: finds or creates the UnaiManager, and adds the UI input module
    /// that matches the project's input handling (Input System package or legacy Input Manager).
    /// </summary>
    public static class UnaiSampleSetup
    {
        public static bool TryEnsureManager(GameObject host, out string error)
        {
            error = null;
            var manager = UnaiManager.Instance;
            if (manager == null)
            {
                manager = Object.FindAnyObjectByType<UnaiManager>();
                if (manager == null) manager = host.AddComponent<UnaiManager>();
            }

            if (manager.Config == null)
            {
                var config = FindConfigInProject();
                if (config == null)
                {
                    error = "No UnaiGlobalConfig found.\n" +
                            "Open Window > UnAI > Setup Wizard to create one, then assign it to the " +
                            "UnaiManager in this scene.";
                    return false;
                }
                manager.Initialize(config);
            }

            if (manager.ActiveProvider == null)
            {
                error = $"Provider '{manager.Config.DefaultProviderId}' is not available. " +
                        "Check that it is enabled in your UnaiGlobalConfig.";
                return false;
            }
            return true;
        }

        public static string DescribeActiveProvider()
        {
            var manager = UnaiManager.Instance;
            var provider = manager != null ? manager.ActiveProvider : null;
            if (provider == null) return "No provider";
            string model = manager.Config.GetProviderConfig(provider.ProviderId)?.DefaultModel;
            return string.IsNullOrEmpty(model) ? provider.DisplayName : $"{provider.DisplayName} · {model}";
        }

        public static void EnsureInputModule()
        {
            var eventSystem = EventSystem.current != null
                ? EventSystem.current
                : Object.FindAnyObjectByType<EventSystem>();
            if (eventSystem == null)
                eventSystem = new GameObject("EventSystem").AddComponent<EventSystem>();
            if (eventSystem.GetComponent<BaseInputModule>() != null) return;
#if UNAI_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
#else
            eventSystem.gameObject.AddComponent<StandaloneInputModule>();
#endif
        }

        // ------------------------------------------------------------------ Emoji

        // File names of the color emoji fonts shipped with Windows, macOS/iOS and Android/Linux.
        private static readonly string[] EmojiFontFiles = { "seguiemj", "apple color emoji", "notocoloremoji" };
        private static TMP_FontAsset s_emojiFont;
        private static bool s_emojiChecked;

        /// <summary>
        /// Models like to answer with emojis, which TMP's default font cannot draw.
        /// Uses the operating system's color emoji font when available (Windows, macOS, iOS, Android);
        /// otherwise <see cref="PrepareText"/> removes emojis so players never see empty boxes.
        /// </summary>
        public static void EnableEmoji()
        {
            if (s_emojiChecked) return;
            s_emojiChecked = true;
            if (TMP_Settings.instance == null) return;

            foreach (string path in Font.GetPathsToOSFonts())
            {
                string file = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (System.Array.FindIndex(EmojiFontFiles, name => file.StartsWith(name)) < 0) continue;
                var font = TMP_FontAsset.CreateFontAsset(path, 0, 90, 6, GlyphRenderMode.COLOR, 1024, 1024);
                if (font == null || font.material == null || font.material.shader == null) continue;
                font.name = $"{file} (runtime emoji)";
                s_emojiFont = font;
                break;
            }
            if (s_emojiFont == null) return;

            // TMP_Settings is a project asset: add the runtime font only for this session.
            TMP_Settings.emojiFallbackTextAssets ??= new List<TMP_Asset>();
            TMP_Settings.emojiFallbackTextAssets.Insert(0, s_emojiFont);
            Application.quitting += () =>
            {
                TMP_Settings.emojiFallbackTextAssets?.Remove(s_emojiFont);
                s_emojiFont = null;
                s_emojiChecked = false;
            };
        }

        /// <summary>
        /// Returns text ready for TMP. With an emoji font, joined sequences (e.g. a wizard + ZWJ + male sign)
        /// and skin-tone modifiers are reduced to the base emoji, since TMP draws them as separate glyphs.
        /// Without one, emojis are removed so they don't show as empty boxes.
        /// </summary>
        public static string PrepareText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            bool keepEmoji = s_emojiFont != null;
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                int cp = char.ConvertToUtf32(text, i);
                int units = char.IsSurrogatePair(text, i) ? 2 : 1;

                if (cp == 0x200D)
                {
                    // Zero-width joiner: drop it and the joined code point that follows.
                    if (i + 1 < text.Length) i += char.IsSurrogatePair(text, i + 1) ? 2 : 1;
                    continue;
                }
                bool skinTone = cp >= 0x1F3FB && cp <= 0x1F3FF;
                bool emoji = cp >= 0x1F000 || cp == 0xFE0F || (cp >= 0x2600 && cp <= 0x27BF);
                if (skinTone || (emoji && !keepEmoji))
                {
                    i += units - 1;
                    continue;
                }
                sb.Append(text, i, units);
                i += units - 1;
            }
            if (keepEmoji) return sb.ToString();
            // Tidy the gaps left by removed emojis: "Potions 🧪, swords" -> "Potions, swords".
            string result = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @" +([,.!?;:])", "$1");
            return System.Text.RegularExpressions.Regex.Replace(result, @"[ \t]{2,}", " ").Trim();
        }

        private static UnaiGlobalConfig FindConfigInProject()
        {
#if UNITY_EDITOR
            // Editor convenience: use the config created by the Setup Wizard. Assign it on the
            // UnaiManager for player builds.
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:UnaiGlobalConfig"))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var config = UnityEditor.AssetDatabase.LoadAssetAtPath<UnaiGlobalConfig>(path);
                if (config != null) return config;
            }
#endif
            return null;
        }
    }
}
