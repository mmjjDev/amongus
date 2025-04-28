using BepInEx;
using BepInEx.Configuration;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Hazel;

[BepInPlugin("com.mmjjdev.amongusdiscordmuter", "Among Us Discord Muter", "1.0.0")]
public class AmongUsDiscordMuter : BaseUnityPlugin
{
    private static readonly HttpClient client = new HttpClient();
    private ConfigEntry<string> botEndpoint;
    private ConfigEntry<bool> enableLogging;
    private HashSet<string> mutedPlayers = new HashSet<string>();
    private bool lobbyAnnounced = false;
    private string lastRoomCode = "";

    private void Log(string msg)
    {
        if (enableLogging.Value)
        {
            Logger.LogInfo(msg);
            System.IO.File.AppendAllText(Paths.PluginPath + "/AmongUsDiscordMuter.log", $"[{System.DateTime.Now}] {msg}\n");
        }
    }

    public void Awake()
    {
        botEndpoint = Config.Bind("General", "BotEndpoint", "http://localhost:5000", "Discord bot API endpoint");
        enableLogging = Config.Bind("General", "EnableLogging", true, "Enable file/console logging");
        StartCoroutine(PlayerCheckLoop());
        Log("Among Us Discord Muter loaded.");
    }

    private IEnumerator PlayerCheckLoop()
    {
        while (true)
        {
            var players = PlayerControl.AllPlayerControls;
            // Announce lobby once the host is in a game (lobby code available)
            if (AmongUsClient.Instance && AmongUsClient.Instance.GameId != 0)
            {
                string code = GameCode();
                if (!lobbyAnnounced || lastRoomCode != code)
                {
                    lastRoomCode = code;
                    AnnounceLobby();
                    lobbyAnnounced = true;
                }
            }
            else
            {
                lobbyAnnounced = false;
            }

            foreach (var player in players)
            {
                bool isDead = player.Data.IsDead;
                string playerName = player.Data.PlayerName;
                if (string.IsNullOrWhiteSpace(playerName)) continue;
                bool isMuted = mutedPlayers.Contains(playerName);

                if (isDead && !isMuted)
                {
                    MuteOnDiscord(playerName);
                    mutedPlayers.Add(playerName);
                }
                else if (!isDead && isMuted)
                {
                    UnmuteOnDiscord(playerName);
                    mutedPlayers.Remove(playerName);
                }
            }
            // Unmute all at end of game
            if (AmongUsClient.Instance && AmongUsClient.Instance.GameState == InnerNet.InnerNetClient.GameStates.Ended)
            {
                UnmuteAll();
                mutedPlayers.Clear();
            }
            yield return new WaitForSeconds(1f);
        }
    }

    private string GameCode()
    {
        if (AmongUsClient.Instance && AmongUsClient.Instance.GameId != 0)
        {
            uint gameId = AmongUsClient.Instance.GameId;
            string code = "";
            for (int i = 0; i < 6; i++)
            {
                code += (char)('A' + (gameId % 26));
                gameId /= 26;
            }
            char[] arr = code.ToCharArray();
            System.Array.Reverse(arr);
            return new string(arr);
        }
        return "";
    }

    private async void AnnounceLobby()
    {
        try
        {
            List<string> players = new List<string>();
            foreach (var pc in PlayerControl.AllPlayerControls)
                players.Add(pc.Data.PlayerName);

            var json = $"{{\"code\":\"{GameCode()}\",\"players\":{JsonList(players)}}}";
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var res = await client.PostAsync(botEndpoint.Value + "/embed", content);
            Log($"Announced lobby: {json} (status {res.StatusCode})");
        }
        catch (System.Exception ex)
        {
            Log($"[ERROR] Failed to announce lobby: {ex.Message}");
        }
    }

    private string JsonList(List<string> l)
    {
        StringBuilder sb = new StringBuilder("[");
        for (int i = 0; i < l.Count; i++)
        {
            sb.Append("\"").Append(l[i].Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"");
            if (i < l.Count - 1) sb.Append(",");
        }
        sb.Append("]");
        return sb.ToString();
    }

    private async void MuteOnDiscord(string playerName)
    {
        try
        {
            var json = $"{{\"name\":\"{playerName}\"}}";
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var res = await client.PostAsync(botEndpoint.Value + "/mute", content);
            Log($"Muted {playerName} (status {res.StatusCode})");
        }
        catch (System.Exception ex)
        {
            Log($"[ERROR] Failed to mute {playerName}: {ex.Message}");
        }
    }

    private async void UnmuteOnDiscord(string playerName)
    {
        try
        {
            var json = $"{{\"name\":\"{playerName}\"}}";
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var res = await client.PostAsync(botEndpoint.Value + "/unmute", content);
            Log($"Unmuted {playerName} (status {res.StatusCode})");
        }
        catch (System.Exception ex)
        {
            Log($"[ERROR] Failed to unmute {playerName}: {ex.Message}");
        }
    }

    private void UnmuteAll()
    {
        foreach (var playerName in mutedPlayers)
        {
            UnmuteOnDiscord(playerName);
        }
    }
}