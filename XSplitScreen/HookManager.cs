using Rewired;
using System;
using System.Collections.Generic;
using System.Linq;
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
			UpdatePickerPanelHooks(state);
			UpdateLookingGlassHooks(state);
			UpdateMeditateHook(state);
			UpdateClearCameras(state);
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
				Plugin.TryPatch(RunCameraManager_Update_Orig, postfix: RunCameraManager_Update_ExtraPlayers_Patch);
			}
			else
			{
				Plugin.TryUnpatch(RunCameraManager_Update_Orig, RunCameraManager_Update_ExtraPlayers_Patch);
			}
		}

		private static FieldInfo _runCameraManagerCamerasField;
		private static FieldInfo _runCameraManagerCanvasesField;

		// (specter) Vanilla only wires a canvas for PlayerMain/Player2/3/4 - mirrors that same
		// setup for players beyond 4 (this mod clones Rewired players up to Player16).
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
				Plugin.TryPatch(ChatBox_Update_Orig, prefix: ChatBox_Update_Patch);
			}
			else
			{
				Plugin.TryUnpatch(ChatBox_Update_Orig, ChatBox_Update_Patch);
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
				Plugin.TryPatch(PlayerCharacterMasterController_OnBodyStart_Orig, postfix: PlayerCharacterMasterController_OnBodyStart_Patch);
			}
			else
			{
				Plugin.TryUnpatch(PlayerCharacterMasterController_OnBodyStart_Orig, PlayerCharacterMasterController_OnBodyStart_Patch);
			}
		}

		private static void PlayerCharacterMasterController_SetBody(RoR2.PlayerCharacterMasterController __instance, GameObject __0)
		{
			if (__0 == null)
				return;

			var localUser = __instance.networkUser != null && __instance.networkUser.isLocalPlayer ? __instance.networkUser.localUser : null;

			if (localUser?.inputPlayer == null || localUser.userProfile?.fileName == null)
				return;

			var user = SplitscreenUserManager.GetUserByInputName(localUser.inputPlayer.name);

			// The trail is parented to the body, so it is gone (Unity null) once the body is destroyed
			if (user == null || user.ParticleSystem != null)
				return;

			var trailKey = SplitScreenSettings.GetOrCreateUserModule<TrailsSettingsModule>(localUser.userProfile.fileName)?.TrailKey;

			if (string.IsNullOrEmpty(trailKey) || trailKey == "none")
				return;

			var particleSystem = ParticleSystemFactory.GetParticleSystem(trailKey);

			if (particleSystem == null)
				return;

			particleSystem.SetParent(__0.transform);
			particleSystem.localPosition = Vector3.zero;
			particleSystem.gameObject.SetActive(true);
			user.ParticleSystem = particleSystem;
		}

		/// <summary>
		/// Vanilla multiplayer colors (RoR2.ColorCatalog)
		/// </summary>
		internal static readonly Color[] DefaultMultiplayerColors = new Color[4]
		{
				new Color32(252, 62, 62, byte.MaxValue),
				new Color32(62, 109, 252, byte.MaxValue),
				new Color32(129, 252, 62, byte.MaxValue),
				new Color32(252, 241, 62, byte.MaxValue)
		};

		private static void UpdateMultiplayerColors(bool isSplitscreenEnabled)
		{
			var multiplayerColors = DefaultMultiplayerColors;

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
				Plugin.TryPatch(CameraRigController_Start_Orig, postfix: CameraRigController_Start_Patch);
			}
			else
			{
				Plugin.TryUnpatch(CameraRigController_Start_Orig, CameraRigController_Start_Patch);
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
				Plugin.TryPatch(MPEventSystem_OnLastActiveControllerChanged_Orig, postfix: MPEventSystem_OnLastActiveControllerChanged_Patch);
			}
			else
			{
				Plugin.TryUnpatch(MPEventSystem_OnLastActiveControllerChanged_Orig, MPEventSystem_OnLastActiveControllerChanged_Patch);
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
			if (args.controllerType != ControllerType.Joystick)
				return;

			var localUsers = SplitscreenUserManager.LocalUsers;

			// Already back with a player (e.g. Rewired restored the assignment)
			if (localUsers.Any(x => x.InputPlayer.controllers.ContainsController(args.controllerType, args.controllerId)))
				return;

			// Give it to the first gamepad player who has no controller
			var user = localUsers.FirstOrDefault(x => x.InputPlayer.controllers.joystickCount == 0 && !x.InputPlayer.controllers.hasKeyboard);

			user?.InputPlayer.controllers.AddController(args.controller, true);
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

				rects = new UnityEngine.Rect[localUsers.Count + 1][];
				rects[0] = LayoutFor(0);
				rects[1] = LayoutFor(1);

				var uRects = new UnityEngine.Rect[localUsers.Count];

				for (int e = 0; e < uRects.Length; e++)
				{
					uRects[e] = localUsers[e].CameraRect;
					Log.Print($"Adding '{localUsers[e].UserIndex}' with rect '{localUsers[e].CameraRect}'");
				}

				// RunCameraManager indexes ScreenLayouts by the number of cameras created so far,
				// which can be lower than the player count while the lobby is loading
				for (int count = 2; count <= localUsers.Count; count++)
					rects[count] = uRects.Take(count).ToArray();
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

		// (specter) Vanilla's default layouts for 0-4 players; splitscreen players use their own rects.
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

			return new Rect[0];
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
				Plugin.TryPatch(PauseStopController_allowMultiplayerPause_Orig, prefix: PauseStopController_allowMultiplayerPause_Patch);
			}
			else
			{
				Plugin.TryUnpatch(PauseStopController_allowMultiplayerPause_Orig, PauseStopController_allowMultiplayerPause_Patch);
			}
		}

		private static bool PauseStopController_allowMultiplayerPause(PauseStopController __instance, bool __0)
		{
			__instance.Network_allowMultiplayerPause = !SplitscreenUserManager.OnlineMode || __0;

			return false;
		}

		private static MethodInfo PickupPickerPanel_DestroyIt_Orig;
		private static MethodInfo CraftingPanel_DestroyIt_Orig;
		private static MethodInfo PickerPanel_DestroyIt_Patch;
		private static void UpdatePickerPanelHooks(bool isSplitscreenEnabled)
		{
			PickupPickerPanel_DestroyIt_Orig ??= typeof(PickupPickerPanel).GetMethod("DestroyIt", BindingFlags.Instance | BindingFlags.NonPublic);
			CraftingPanel_DestroyIt_Orig ??= typeof(CraftingPanel).GetMethod("DestroyIt", BindingFlags.Instance | BindingFlags.NonPublic);
			PickerPanel_DestroyIt_Patch ??= typeof(HookManager).GetMethod("PickerPanel_DestroyIt", BindingFlags.Static | BindingFlags.NonPublic);

			if (isSplitscreenEnabled)
			{
				Plugin.TryPatch(PickupPickerPanel_DestroyIt_Orig, prefix: PickerPanel_DestroyIt_Patch);
				Plugin.TryPatch(CraftingPanel_DestroyIt_Orig, prefix: PickerPanel_DestroyIt_Patch);
			}
			else
			{
				Plugin.TryUnpatch(PickupPickerPanel_DestroyIt_Orig, PickerPanel_DestroyIt_Patch);
				Plugin.TryUnpatch(CraftingPanel_DestroyIt_Orig, PickerPanel_DestroyIt_Patch);
			}
		}

		// (specter) DestroyIt listens to the static scoreboard event, so any player's scoreboard closed
		// every player's picker. Only let it through for this panel's own HUD, or a pause (timeScale is 0).
		private static bool PickerPanel_DestroyIt(MonoBehaviour __instance)
		{
			var hud = __instance.GetComponentInParent<HUD>();

			return !hud || hud.scoreboardPanel.activeSelf || Time.timeScale == 0f;
		}

		private static MethodInfo ClosePickupPicker_Update_Orig;
		private static MethodInfo ClosePickupPicker_CloseMenuAfterFrame;
		private static MethodInfo ClosePickupPicker_Update_Patch;
		private static void UpdateLookingGlassHooks(bool isSplitscreenEnabled)
		{
			if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("droppod.lookingglass", out var lookingGlass))
				return;

			if (ClosePickupPicker_Update_Orig == null)
			{
				var closePickupPicker = lookingGlass.Instance.GetType().Assembly.GetType("LookingGlass.EscapeToCloseMenu.ClosePickupPicker");

				ClosePickupPicker_Update_Orig = closePickupPicker?.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public);
				ClosePickupPicker_CloseMenuAfterFrame = closePickupPicker?.GetMethod("CloseMenuAfterFrame", BindingFlags.Instance | BindingFlags.Public);
				ClosePickupPicker_Update_Patch = typeof(HookManager).GetMethod("ClosePickupPicker_Update", BindingFlags.Static | BindingFlags.NonPublic);
			}

			if (isSplitscreenEnabled)
				Plugin.TryPatch(ClosePickupPicker_Update_Orig, prefix: ClosePickupPicker_Update_Patch);
			else
				Plugin.TryUnpatch(ClosePickupPicker_Update_Orig, ClosePickupPicker_Update_Patch);
		}

		// (specter) LookingGlass closes pickers on Input.anyKeyDown, which includes every player's controller.
		// Only the picker owner's keyboard closes it now, matching its keyboard-only intent.
		private static bool ClosePickupPicker_Update(MonoBehaviour __instance, Player ___player)
		{
			if (___player != null && ___player.controllers.hasKeyboard && ReInput.controllers.Keyboard.GetAnyButtonDown()
				&& !___player.GetButton(5) && !Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.BackQuote) && !Input.GetKeyDown(KeyCode.F2))
				ClosePickupPicker_CloseMenuAfterFrame?.Invoke(__instance, null);

			return false;
		}

		private static MethodInfo Meditate_UpdateUIInputSequence_Orig;
		private static MethodInfo Meditate_UpdateUIInputSequence_Patch;
		private static void UpdateMeditateHook(bool isSplitscreenEnabled)
		{
			Meditate_UpdateUIInputSequence_Orig ??= typeof(EntityStates.Seeker.Meditate).GetMethod("UpdateUIInputSequence", BindingFlags.Instance | BindingFlags.NonPublic);
			Meditate_UpdateUIInputSequence_Patch ??= typeof(HookManager).GetMethod("Meditate_UpdateUIInputSequence", BindingFlags.Static | BindingFlags.NonPublic);

			if (isSplitscreenEnabled)
				Plugin.TryPatch(Meditate_UpdateUIInputSequence_Orig, prefix: Meditate_UpdateUIInputSequence_Patch);
			else
				Plugin.TryUnpatch(Meditate_UpdateUIInputSequence_Orig, Meditate_UpdateUIInputSequence_Patch);
		}

		private static readonly string[] MeditateArrowNames = { "ArrowInput1", "ArrowInput2", "ArrowInput3", "ArrowInput4", "ArrowInput5" };

		// (specter) A spectating player's HUD gets its own copy of the overlay, and vanilla only colours the newest one.
		private static bool Meditate_UpdateUIInputSequence(EntityStates.Seeker.Meditate __instance, RoR2.HudOverlay.OverlayController ___overlayController)
		{
			if (!__instance.isAuthority || ___overlayController == null)
				return false;

			var color = new Color32(255, 233, 124, 255);

			foreach (var instance in ___overlayController.instancesList)
			{
				var childLocator = instance.GetComponent<ChildLocator>();

				for (int i = 0; i < __instance.seekerController.meditationInputStep; i++)
					childLocator.FindChild(MeditateArrowNames[i]).GetComponent<UnityEngine.UI.Image>().color = color;
			}

			return false;
		}

		private static readonly List<GameObject> ClearCameras = new List<GameObject>();

		// (specter) Unity never clears screen area outside every camera, so empty regions smeared old frames.
		// A clear-only camera behind the player cameras keeps them black.
		private static void UpdateClearCameras(bool isSplitscreenEnabled)
		{
			foreach (var clearCamera in ClearCameras)
				if (clearCamera)
					UnityEngine.Object.Destroy(clearCamera);

			ClearCameras.Clear();

			if (!isSplitscreenEnabled)
				return;

			for (int display = 0; display < Display.displays.Length; display++)
			{
				if (display != 0 && !Display.displays[display].active)
					continue;

				var clearCamera = new GameObject($"XSS Clear Camera (Display {display})");
				UnityEngine.Object.DontDestroyOnLoad(clearCamera);

				var camera = clearCamera.AddComponent<Camera>();
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = Color.black;
				camera.cullingMask = 0;
				camera.depth = -1000f;
				camera.targetDisplay = display;
				camera.useOcclusionCulling = false;
				camera.allowHDR = false;
				camera.allowMSAA = false;

				ClearCameras.Add(clearCamera);
			}
		}
	}
}
