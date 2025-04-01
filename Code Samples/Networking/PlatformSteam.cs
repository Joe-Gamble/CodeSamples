using Gamble.BattleCards.Logging;
using Steamworks;
using Steamworks.Data;
using System;
using System.Collections;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;

namespace Gamble.BattleCards.Infrastructure.Platform
{
    public class PlatformSteam : PlatformShared
    {
        public override event Action<string, string> OnPlatformMessageRecieved;

        public override IEnumerator InitialisePlatform()
        {
            if (!Steamworks.SteamClient.IsValid)
            {
                try
                {
                    Steamworks.SteamClient.Init(480, true); // Second argument 'true' ensures no overlay issues
                    LOG.Log("Steam initialized successfully!", LOG.Type.PLATFORM);
                }
                catch (System.Exception e)
                {
                    LOG.LogError($"Steam initialization failed: {e.Message}", LOG.Type.PLATFORM);
                }
            }
            else
            {
                LOG.Log("Steam already initialised.", LOG.Type.PLATFORM);
            }

            yield return new WaitUntil(() => Steamworks.SteamClient.IsValid);

            SteamFriends.OnChatMessage += OnSteamChatMessageReceived;
            SteamFriends.ListenForFriendsMessages = true;

            yield return null;
        }

        public override void ShutdownPlatform()
        {
            SteamFriends.OnChatMessage -= OnSteamChatMessageReceived;
        }

        public override string GetPlatformName()
        {
            return Steamworks.SteamClient.Name;
        }

        public override ulong GetClientID()
        {
            return Steamworks.SteamClient.SteamId;
        }

        public override void GetPlatformAvatar(ulong id, Action<Sprite> onComplete)
        {
            if (!SteamClient.IsValid)
            {
                LOG.LogError("Steam is not initialized!", LOG.Type.PLATFORM);
                onComplete.Invoke(null);
                return;
            }

            Task.Run(async () =>
            {
                Image avatarImage = await GetAvatarAsync(id);
                await Awaitable.MainThreadAsync();

                Texture2D avatarTexture = Covert(avatarImage);
                Sprite spirte = Sprite.Create(avatarTexture, new Rect(0, 0, avatarTexture.width, avatarTexture.height), new Vector2(0.5f, 0.5f));
                onComplete?.Invoke(spirte);
            });
        }

        private async Task<Image> GetAvatarAsync(ulong id)
        {
            Friend friend = new Friend(id);
            Image? avatar = await friend.GetSmallAvatarAsync();

            return avatar.Value;
        }

        private static Texture2D Covert(Image image)
        {
            // Create a new Texture2D
            Texture2D avatar = new Texture2D((int)image.Width, (int)image.Height, TextureFormat.ARGB32, false);

            // Set filter type, or else its really blury
            avatar.filterMode = FilterMode.Trilinear;

            // Flip image
            for (int x = 0; x < image.Width; x++)
            {
                for (int y = 0; y < image.Height; y++)
                {
                    var p = image.GetPixel(x, y);
                    avatar.SetPixel(x, (int)image.Height - y, new UnityEngine.Color(p.r / 255.0f, p.g / 255.0f, p.b / 255.0f, p.a / 255.0f));
                }
            }

            avatar.Apply();
            return avatar;
        }

        public override bool IsRunningAsAdmin()
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/C net session",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process process = Process.Start(psi);
            process.WaitForExit();

            return process.ExitCode == 0; // Exit code 0 = Admin
        }

        private void OnSteamChatMessageReceived(Friend friend, string messageType, string message)
        {
            if (messageType == "Typing")
                return;

            OnPlatformMessageRecieved?.Invoke(friend.Name, message);
        }
    }
}