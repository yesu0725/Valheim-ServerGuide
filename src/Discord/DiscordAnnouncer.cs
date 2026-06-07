using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using ValheimServerGuide.Config;

namespace ValheimServerGuide.Discord
{
    /// Posts a guidance event to a Discord webhook. SERVER-SIDE ONLY — the
    /// webhook URL is a secret stored in BepInEx config on the server and
    /// never travels over RPC to clients. Triggering players send a small
    /// announcement-request RPC; the server reads its local config and posts.
    public static class DiscordAnnouncer
    {
        public static void Announce(GuidanceEntry entry, string playerName)
        {
            var url = Plugin.DiscordWebhookUrl?.Value;
            if (string.IsNullOrWhiteSpace(url))
            {
                Plugin.Log.LogInfo($"[discord] '{entry.Id}' wanted to announce but DiscordWebhookUrl is empty.");
                return;
            }

            var template = entry.Announce?.Discord;
            if (string.IsNullOrEmpty(template))
                template = Plugin.DiscordDefaultTemplate?.Value ?? "{playerName} triggered {id}";

            var message = ApplyTokens(template, entry, playerName);
            var username = Plugin.DiscordBotUsername?.Value;
            if (string.IsNullOrWhiteSpace(username)) username = "ValheimServerGuide";

            if (Plugin.Instance == null) return;
            Plugin.Instance.StartCoroutine(Post(url, message, username, entry.Id));
        }

        public static void AnnounceChainComplete(string playerName, string chainTitle)
        {
            if (Plugin.DiscordGuideEnabled?.Value == false) return;

            var url = Plugin.DiscordWebhookUrl?.Value;
            if (string.IsNullOrWhiteSpace(url))
            {
                Plugin.Log.LogInfo($"[discord] chain-complete '{chainTitle}' skipped: DiscordWebhookUrl is empty.");
                return;
            }

            var username = Plugin.DiscordBotUsername?.Value;
            if (string.IsNullOrWhiteSpace(username)) username = "ValheimServerGuide";

            var format = Plugin.DiscordGuideFormat?.Value ?? "plain";
            var isEmbed = string.Equals(format, "embed", System.StringComparison.OrdinalIgnoreCase);

            if (Plugin.Instance == null) return;
            Plugin.Instance.StartCoroutine(PostChainComplete(url, playerName ?? "", chainTitle ?? "", username, isEmbed));
        }

        private static IEnumerator PostChainComplete(string url, string playerName, string chainTitle, string username, bool isEmbed)
        {
            string payload;
            if (isEmbed)
            {
                payload = "{\"username\":\"" + JsonEscape(username) +
                          "\",\"embeds\":[{\"title\":\"Guide Complete\",\"description\":\"**" +
                          JsonEscape(playerName) + "** finished **" + JsonEscape(chainTitle) +
                          "\",\"color\":3066993,\"footer\":{\"text\":\"Hearthbound Valheim\"}}]," +
                          "\"allowed_mentions\":{\"parse\":[]}}";
            }
            else
            {
                var content = "**" + playerName + "** has completed the **" + chainTitle + "**! 🎉";
                if (content.Length > 1900) content = content.Substring(0, 1900) + "...";
                payload = "{\"content\":\"" + JsonEscape(content) +
                          "\",\"username\":\"" + JsonEscape(username) +
                          "\",\"allowed_mentions\":{\"parse\":[]}}";
            }

            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                    Plugin.Log.LogWarning($"[discord] chain-complete '{chainTitle}' webhook failed: {req.responseCode} {req.error}");
                else
                    Plugin.Log.LogInfo($"[discord] chain-complete '{chainTitle}' announced for {playerName}.");
            }
        }

        private static IEnumerator Post(string url, string content, string username, string id)
        {
            // Discord limits content to 2000 chars. Truncate defensively.
            if (content.Length > 1900) content = content.Substring(0, 1900) + "...";

            var payload = "{\"content\":\"" + JsonEscape(content) +
                          "\",\"username\":\"" + JsonEscape(username) +
                          "\",\"allowed_mentions\":{\"parse\":[]}}";

            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                    Plugin.Log.LogWarning($"[discord] '{id}' webhook failed: {req.responseCode} {req.error}");
                else
                    Plugin.Log.LogInfo($"[discord] '{id}' announced.");
            }
        }

        private static string ApplyTokens(string template, GuidanceEntry entry, string playerName)
        {
            return template
                .Replace("{playerName}", playerName ?? "")
                .Replace("{id}", entry.Id ?? "")
                .Replace("{topic}", entry.Display?.Topic ?? "")
                .Replace("{text}", entry.Display?.Text ?? "");
        }

        private static string JsonEscape(string s)
        {
            if (s == null) return string.Empty;
            var sb = new StringBuilder(s.Length + 16);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
