using Rewired;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using static Dodad.XSplitscreen.Log;
using UnityEngine;
using RoR2;
using R2API.Utils;
using RoR2.UI;

namespace Dodad.XSplitscreen
{
	public static class HookManager
	{
		public static void OnStateChange(bool state)
		{
			Log.Print($"HookManager::OnStateChange -> '{state}'");

			ResizePlayerCount(state);
			UpdateCameraRects(state);
			UpdateMultiplayerColors(state);
			UpdateDisplayHook(state);
			UpdateTrailHooks(state);
			UpdateMPEventHooks(state);
			UpdateChatHooks(state);
			UpdateSubscriptions(state);
			UpdateRunCameraManager(state);
			UpdatePauseScreenBehaviourHook(state);
		}

		private static void ResizePlayerCount(bool state)
		{
			int playerCount = 4;

			if (state)
				playerCount = Math.Max(4, SplitscreenUserManager.LocalUsers.Count);

			RoR2.LobbyManager.cvSteamLobbyMaxMembers.defaultValue = playerCount.ToString();
			Reflection.SetPropertyValue<int>((object) RoR2.LobbyManager.cvSteamLobbyMaxMembers, "value", playerCount);
			RoR2.Networking.NetworkManagerSystem.SvMaxPlayersConVar.instance.SetString(playerCount.ToString());
		}

		private static MethodInfo RunCameraManager_Update_Orig;
		private static MethodInfo RunCameraManager_Update_ExtraPlayers_Patch;

		private static void UpdateRunCameraManager(bool isSplitscreenEnabled)
		{
			RunCameraManager_Update_Orig ??= typeof(RoR2.RunCameraManager).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
			RunCameraManager_Update_ExtraPlayers_Patch ??= typeof(HookManager).GetMethod("RunCameraManager_Update_ExtraPlayers", BindingFlags.Static | BindingFlags.NonPublic);

			if (isSplitscreenEnabled)
			{
				Plugin.Patcher.Patch(RunCameraManager_Update_Orig, postfix: new HarmonyLib.HarmonyMethod(RunCameraManager_Update_ExtraPlayers_Patch));
			}
			else
			{
				Plugin.Patcher.Unpatch(RunCameraManager_Update_Orig, RunCameraManager_Update_ExtraPlayers_Patch);
			}
		}

		private static FieldInfo _runCameraManagerCamerasField;
		private static FieldInfo _runCameraManagerCanvasesField;

		// (specter) Vanilla's own character-select canvas setup only has cases for PlayerMain/
		// Player2/Player3/Player4, so any local player beyond that (this mod clones Rewired players
		// up to Player16) never gets a canvas. This mirrors vanilla's own instantiate+wire steps
		// (RunCameraManager.Update) for those unmatched names, using a postfix instead of an IL
		// patch, since an exact-instruction-sequence IL match breaks the moment vanilla's method
		// body is rewritten, as happened with the 1.5.0 update.
		private static void RunCameraManager_Update_ExtraPlayers(RoR2.RunCameraManager __instance)
		{
			if (!__instance.isLobby || !RoR2Application.isInLocalMultiPlayer)
				return;

			_runCameraManagerCamerasField ??= typeof(RoR2.RunCameraManager).GetField("cameras", BindingFlags.Instance | BindingFlags.NonPublic);
			_runCameraManagerCanvasesField ??= typeof(RoR2.RunCameraManager).GetField("canvases", BindingFlags.Instance | BindingFlags.NonPublic);

			var cameras = (Dictionary<string, CameraRigController>) _runCameraManagerCamerasField.GetValue(__instance);
			var canvases = (Dictionary<string, Canvas>) _runCameraManagerCanvasesField.GetValue(__instance);

			var localPlayers = NetworkUser.readOnlyLocalPlayersList;

			for (int i = 0; i < localPlayers.Count; i++)
			{
				var localUser = localPlayers[i]?.localUser;

				if (localUser?.inputPlayer == null)
					continue;

				string name = localUser.inputPlayer.name;

				if (name == "PlayerMain" || name == "Player2" || name == "Player3" || name == "Player4")
					continue; // vanilla already handles these

				if (canvases.TryGetValue(name, out var existingCanvas) && existingCanvas != null)
					continue;

				if (!cameras.TryGetValue(name, out var cameraRig) || cameraRig == null)
					continue; // this player's camera isn't ready yet this frame

				var canvasObject = UnityEngine.Object.Instantiate(__instance.CharacterSelectUILocal);
				var provider = canvasObject.GetComponent<MPEventSystemProvider>();
				provider.eventSystem = MPEventSystem.FindByPlayer(localUser.inputPlayer);

				var voteInfo = canvasObject.GetComponentInChildren<VoteInfoPanelController>();
				if (voteInfo != null)
					voteInfo.voteController = __instance.PreGameController.GetComponent<VoteController>();

				var canvas = canvasObject.GetComponent<Canvas>();
				canvas.renderMode = RenderMode.ScreenSpaceCamera;
				canvas.planeDistance = 100 * (i + 1);
				canvas.worldCamera = cameraRig.uiCam;
				canvases[name] = canvas;
			}
		}

		private static RoR2.UI.ChatBox ChatInstance;
		private static MethodInfo ChatBox_Update_Orig;
		private static MethodInfo ChatBox_Update_Patch;
		private static void UpdateChatHooks(bool isSplitscreenEnabled)
		{
			ChatBox_Update_Orig ??= typeof(RoR2.UI.ChatBox).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
			ChatBox_Update_Patch ??= typeof(HookManager).GetMethod("ChatBox_Update", BindingFlags.Static | BindingFlags.NonPublic);

			if (isSplitscreenEnabled)
			{
				Plugin.Patcher.Patch(ChatBox_Update_Orig, prefix: new HarmonyLib.HarmonyMethod(ChatBox_Update_Patch));
			}
			else
			{
				Plugin.Patcher.Unpatch(ChatBox_Update_Orig, ChatBox_Update_Patch);
			}
		}

		private static bool ChatBox_Update(RoR2.UI.ChatBox __instance)
		{
			if (ChatInstance == null)
				ChatInstance = __instance;

			if (ChatInstance != __instance)
				return false;

			return true;
		}

		private static MethodInfo PlayerCharacterMasterController_OnBodyStart_Orig;
		private static MethodInfo PlayerCharacterMasterController_OnBodyStart_Patch;
		private static void UpdateTrailHooks(bool isSplitscreenEnabled)
		{
			PlayerCharacterMasterController_OnBodyStart_Orig ??= typeof(RoR2.PlayerCharacterMasterController).GetMethod("SetBody", BindingFlags.Instance | BindingFlags.NonPublic);
			PlayerCharacterMasterController_OnBodyStart_Patch ??= typeof(HookManager).GetMethod("PlayerCharacterMasterController_SetBody", BindingFlags.Static | BindingFlags.NonPublic);

			if (isSplitscreenEnabled)
			{
				Plugin.Patcher.Patch(PlayerCharacterMasterController_OnBodyStart_Orig, postfix: new HarmonyLib.HarmonyMethod(PlayerCharacterMasterController_OnBodyStart_Patch));
			}
			else
			{
				Plugin.Patcher.Unpatch(PlayerCharacterMasterController_OnBodyStart_Orig, PlayerCharacterMasterController_OnBodyStart_Patch);
			}
		}

		private static void PlayerCharacterMasterController_SetBody(RoR2.PlayerCharacterMasterController __instance, GameObject __0)
		{
			if (__0 == null)
				return;

			if (__instance.networkUser != null && __instance.networkUser.isLocalPlayer && __instance.networkUser.localUser != null)
			{
				var user = SplitscreenUserManager.GetUserByInputName(__instance.networkUser.localUser.inputPlayer.name);

				if (user.ParticleSystem != null)
					return;

				if (__instance.networkUser.localUser.userProfile == null || __instance.networkUser.localUser.userProfile.fileName == null)
					return;

				var trailKey = SplitScreenSettings.GetOrCreateUserModule<TrailsSettingsModule>(__instance.networkUser.localUser.userProfile.fileName).TrailKey;

				if (trailKey == "none")
				{
					user.ParticleSystem = new GameObject("Dummy").transform;

					return;
				}

				var particleSystem = ParticleSystemFactory.GetParticleSystem(trailKey);
				particleSystem.transform.SetParent(__instance.body.transform);
				particleSystem.transform.localPosition = Vector3.zero;
				particleSystem.gameObject.SetActive(true);
				user.ParticleSystem = particleSystem.transform;
			}
		}

		private static void UpdateMultiplayerColors(bool isSplitscreenEnabled)
		{
			var multiplayerColors = new Color[4]
			{
					new Color32(252, 62, 62, byte.MaxValue),
					new Color32(62, 109, 252, byte.MaxValue),
					new Color32(129, 252, 62, byte.MaxValue),
					new Color32(252, 241, 62, byte.MaxValue)
			};

			if (isSplitscreenEnabled)
			{
				var localUsers = SplitscreenUserManager.LocalUsers;

				multiplayerColors = new Color[localUsers.Count];

				for (int e = 0; e < localUsers.Count; e++)
					multiplayerColors[e] = SplitScreenSettings.GetUserModule<ColorSettingsModule>(localUsers[e].Profile?.fileName ?? string.Empty)?.Color ?? Color.white;
			}

			Plugin.UpdateMultiplayerColors(multiplayerColors);
		}

		private static MethodInfo CameraRigController_Start_Orig;
		private static MethodInfo CameraRigController_Start_Patch;
		private static void UpdateDisplayHook(bool isSplitscreenEnabled)
		{
			CameraRigController_Start_Orig ??= typeof(RoR2.CameraRigController).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic);
			CameraRigController_Start_Patch ??= typeof(HookManager).GetMethod("CameraRigController_Start", BindingFlags.Static | BindingFlags.NonPublic);

			if (isSplitscreenEnabled)
			{
				Plugin.Patcher.Patch(CameraRigController_Start_Orig, postfix: new HarmonyLib.HarmonyMethod(CameraRigController_Start_Patch));
			}
			else
			{
				Plugin.Patcher.Unpatch(CameraRigController_Start_Orig, CameraRigController_Start_Patch);
			}
		}

		private static void CameraRigController_Start(RoR2.CameraRigController __instance)
		{
			Components.ExecuteNextFrame.Invoke(() =>
			{
				if (__instance == null || RunCameraManager.instance == null)
					return;

				var field = typeof(RunCameraManager).GetField("cameras", BindingFlags.Instance | BindingFlags.NonPublic);
				var cameraDict = (Dictionary<string, CameraRigController>) field.GetValue(RunCameraManager.instance);
				string userName = null;

				foreach (var kvp in cameraDict)
				{
					Log.Print($"CameraRigController_Start: kvp -> '{kvp.Key}'");
					if (kvp.Value == __instance)
					{
						Log.Print($"CameraRigController_Start: '{kvp.Key}' accepted");
						userName = kvp.Key;
						break;
					}
				}

				if (userName == null)
					return;

				int targetDisplay = SplitscreenUserManager.GetUserByInputName(userName).Display;
				Log.Print($"CameraRigController_Start: User '{userName}' on display '{targetDisplay}'");
				Log.Print($"Null -> sceneCam = '{__instance.sceneCam == null}', uiCam = '{__instance.uiCam == null}', skyboxCam = '{__instance.skyboxCam == null}'");
				if (__instance.sceneCam != null)
					__instance.sceneCam.targetDisplay = targetDisplay;
				if (__instance.uiCam != null)
					__instance.uiCam.targetDisplay = targetDisplay;
				if (__instance.skyboxCam != null)
					__instance.skyboxCam.targetDisplay = targetDisplay;
			});
		}

		private static MethodInfo MPEventSystem_OnLastActiveControllerChanged_Orig;
		private static MethodInfo MPEventSystem_OnLastActiveControllerChanged_Patch;
		private static void UpdateMPEventHooks(bool isSplitscreenEnabled)
		{
			MPEventSystem_OnLastActiveControllerChanged_Orig ??= typeof(RoR2.UI.MPEventSystem).GetMethod("OnLastActiveControllerChanged", BindingFlags.Instance | BindingFlags.NonPublic);
			MPEventSystem_OnLastActiveControllerChanged_Patch ??= typeof(HookManager).GetMethod("MPEventSystem_OnLastActiveControllerChanged", BindingFlags.Static | BindingFlags.NonPublic);

			if (isSplitscreenEnabled)
			{
				Plugin.Patcher.Patch(MPEventSystem_OnLastActiveControllerChanged_Orig, postfix: new HarmonyLib.HarmonyMethod(MPEventSystem_OnLastActiveControllerChanged_Patch));
			}
			else
			{
				Plugin.Patcher.Unpatch(MPEventSystem_OnLastActiveControllerChanged_Orig, MPEventSystem_OnLastActiveControllerChanged_Patch);
			}
		}

		private static void MPEventSystem_OnLastActiveControllerChanged(RoR2.UI.MPEventSystem __instance, Player __0, Controller __1)
		{
			if (__1 == null)
				__instance.currentInputSource = RoR2.UI.MPEventSystem.InputSource.Gamepad;
		}

		private static void UpdateSubscriptions(bool isSplitscreenEnabled)
		{
			if (isSplitscreenEnabled)
				ReInput.ControllerConnectedEvent += OnControllerConnected;
			else
				ReInput.ControllerConnectedEvent -= OnControllerConnected;
		}

		private static void OnControllerConnected(ControllerStatusChangedEventArgs args)
		{
			var localUsers = SplitscreenUserManager.LocalUsers;

			foreach (var user in localUsers)
			{
				// (specter) Exclude keyboard/mouse users (joystickCount==0 for them too) and stop
				// after the first match, or a reconnecting gamepad lands on whichever eligible user
				// is last in the list instead of the one that actually needs it.
				if (user.InputPlayer.controllers.joystickCount == 0 && !user.InputPlayer.controllers.hasKeyboard)
				{
					user.InputPlayer.controllers.AddController(args.controller, true);

					return;
				}
			}
		}

		private static void UpdateCameraRects(bool isSplitscreenEnabled)
		{
			var field = typeof(RunCameraManager).GetField("ScreenLayouts", BindingFlags.Static | BindingFlags.Public);

			Rect[][] rects = new Rect[5][];
			for (int count = 0; count < rects.Length; count++)
				rects[count] = LayoutFor(count);

			if (isSplitscreenEnabled)
			{
				var localUsers = SplitscreenUserManager.LocalUsers;

				// (specter) Every index up to localUsers.Count needs a valid entry, or vanilla's
				// per-frame camera update null-refs on the gap during a transient mismatch.
				rects = new UnityEngine.Rect[localUsers.Count + 1][];

				for (int count = 0; count < localUsers.Count; count++)
					rects[count] = LayoutFor(count);

				var uRects = new UnityEngine.Rect[localUsers.Count];

				for (int e = 0; e < uRects.Length; e++)
				{
					uRects[e] = localUsers[e].CameraRect;
					Log.Print($"Adding '{localUsers[e].UserIndex}' with rect '{localUsers[e].CameraRect}'");
				}

				rects[localUsers.Count] = uRects;
			}

			field.SetValue(null, rects);

			for (int e = 0; e < rects.Length; e++)
			{
				if (rects[e] == null)
					continue;

				for (int r = 0; r < rects[e].Length; r++)
				{
					Log.Print($"Rect[{e}][{r}] -> {rects[e][r]}");
				}
			}
		}

		/// <summary>
		/// (specter) Generates a default horizontal-bands split for `count` players - only used
		/// as the transient-count safety net above; players define their own split via the
		/// assignment-screen's region grid.
		/// </summary>
		internal static Rect[] LayoutFor(int count)
		{
			if (count <= 0)
				return new Rect[0];

			if (count == 1)
				return new[] { new Rect(0f, 0f, 1f, 1f) };

			if (count == 2)
				return new[] { new Rect(0f, 0.5f, 1f, 0.5f), new Rect(0f, 0f, 1f, 0.5f) };

			if (count == 3)
			{
				return new[]
				{
					new Rect(0f, 0.5f, 1f, 0.5f),
					new Rect(0f, 0f, 0.5f, 0.5f),
					new Rect(0.5f, 0f, 0.5f, 0.5f)
				};
			}

			if (count == 4)
			{
				return new[]
				{
					new Rect(0f, 0.5f, 0.5f, 0.5f),
					new Rect(0.5f, 0.5f, 0.5f, 0.5f),
					new Rect(0f, 0f, 0.5f, 0.5f),
					new Rect(0.5f, 0f, 0.5f, 0.5f)
				};
			}

			// (specter) count > 4: generic grid.
			int primary = Mathf.CeilToInt(Mathf.Sqrt(count));
			int columns = Mathf.CeilToInt((float) count / primary);
			int rows = Mathf.CeilToInt((float) count / columns);

			var result = new Rect[count];
			for (int i = 0; i < count; i++)
			{
				int row = i / columns;
				bool isLastRow = row == rows - 1;
				int itemsInRow = isLastRow ? count - columns * (rows - 1) : columns;
				int col = i % columns;

				float w = 1f / itemsInRow;
				float h = 1f / rows;
				result[i] = new Rect(col * w, 1f - (row + 1) * h, w, h);
			}

			return result;
		}

		private static MethodInfo PauseStopController_allowMultiplayerPause_Orig;
		private static MethodInfo PauseStopController_allowMultiplayerPause_Patch;
		private static void UpdatePauseScreenBehaviourHook(bool isSplitscreenEnabled)
		{
			PauseStopController_allowMultiplayerPause_Orig ??= typeof(RoR2.PauseStopController)
	.GetProperty("allowMultiplayerPause", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
	?.GetSetMethod(true);
			PauseStopController_allowMultiplayerPause_Patch ??= typeof(HookManager).GetMethod("PauseStopController_allowMultiplayerPause", BindingFlags.Static | BindingFlags.NonPublic);

			if (PauseStopController_allowMultiplayerPause_Orig == null)
				return;

			if (isSplitscreenEnabled)
			{
				Plugin.Patcher.Patch(PauseStopController_allowMultiplayerPause_Orig, prefix: new HarmonyLib.HarmonyMethod(PauseStopController_allowMultiplayerPause_Patch));
			}
			else
			{
				Plugin.Patcher.Unpatch(PauseStopController_allowMultiplayerPause_Orig, PauseStopController_allowMultiplayerPause_Patch);
			}
		}

		private static bool PauseStopController_allowMultiplayerPause(PauseStopController __instance, bool __0)
		{
			__instance.Network_allowMultiplayerPause = !SplitscreenUserManager.OnlineMode || __0;

			return false;
		}
	}
}
