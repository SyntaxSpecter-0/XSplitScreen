using Dodad.XSplitscreen.Components;
using Rewired;
using RoR2;
using RoR2.UI.MainMenu;
using System;
using System.Collections.Generic;
using UnityEngine;
using static Dodad.XSplitscreen.Log;

namespace Dodad.XSplitscreen
{
	/// <summary>
	/// Data structure representing user assignment info from the assignment screen
	/// </summary>
	public class UserAssignmentData
	{
		/// <summary>
		/// User Profile
		/// </summary>
		public UserProfile Profile { get; set; }

		/// <summary>
		/// Local index for the player (0 for first player, 1 for second, etc.)
		/// </summary>
		public int UserIndex { get; set; }

		public Player InputPlayer { get; set; }

		public UnityEngine.Rect CameraRect { get; set; }

		public int Display { get; set; }
	}

	/// <summary>
	/// Represents a local user in the splitscreen session
	/// </summary>
	public class LocalUser
	{
		/// <summary>
		/// User Profile
		/// </summary>
		public UserProfile Profile { get; set; }

		/// <summary>
		/// Local index for the player (0 for first player, 1 for second, etc.)
		/// </summary>
		public int UserIndex { get; private set; }

		/// <summary>
		/// Local Rewired input player
		/// </summary>
		public Player InputPlayer { get; private set; }

		/// <summary>
		/// Screen area for the player
		/// </summary>
		public UnityEngine.Rect CameraRect { get; private set; }

		public int Display { get; private set; }

		public Transform ParticleSystem;

		/// <summary>
		/// Initializes a new local user from assignment data
		/// </summary>
		/// <param name="data">User assignment data</param>
		public LocalUser(UserAssignmentData data)
		{
			Profile = data.Profile;
			UserIndex = data.UserIndex;
			InputPlayer = data.InputPlayer;
			CameraRect = data.CameraRect;
			Display = data.Display;
		}
	}

	/// <summary>
	/// Static manager for splitscreen users and hooks
	/// </summary>
	public static class SplitscreenUserManager
	{
		/// <summary>
		/// List of local users for splitscreen
		/// </summary>
		public static IReadOnlyList<LocalUser> LocalUsers => localUsers.AsReadOnly();

		private static readonly List<LocalUser> localUsers = new List<LocalUser>();

		/// <summary>
		/// Invoked when splitscreen is enabled or disabled.
		/// </summary>
		public static Action<bool> OnStateChange;

		public static bool IsSplitscreenEnabled => isSplitscreenEnabled;

		/// <summary>
		/// Indicates if splitscreen is currently enabled
		/// </summary>
		private static bool isSplitscreenEnabled = false;

		/// <summary>
		/// Set to true before enabling splitscreen for online play.
		/// </summary>
		public static bool OnlineMode;

		/// <summary>
		/// Receives user assignment data and creates local users
		/// </summary>
		/// <param name="userAssignments">List of user assignment info (saveID, userIndex)</param>
		public static void InitializeUsers(List<UserAssignmentData> userAssignments)
		{
			localUsers.Clear();

			foreach (var assignment in userAssignments)
			{
				LocalUser user = new LocalUser(assignment);
				localUsers.Add(user);
				Print($"Added local user: '{user.Profile.name}', UserIndex: '{user.UserIndex}', Display: '{user.Display}',", ELogChannel.Debug);
			}
		}

		/// <summary>
		/// Enables splitscreen mode by applying runtime hooks
		/// </summary>
		public static void EnableSplitscreen(string gamemode)
		{
			if (isSplitscreenEnabled || localUsers.Count <= 1)
			{
				if (localUsers.Count <= 1)
				{
					Print("Cannot enable splitscreen with fewer than 2 users", ELogChannel.Warning);
				}
				return;
			}

			Print($"Enabling splitscreen mode for {localUsers.Count} users", ELogChannel.Info);

			isSplitscreenEnabled = true;

			RoR2Application.SetIsInLocalMultiplayer(true);
			LoadLocalUsers();
			OnStateChange?.Invoke(true);
			Diagnostics.ReportLocalUsers("splitscreen enabled");
			WaitForEntitlementsThenTransition(gamemode, retriesLeft: 120);
		}

		// (specter) Vanilla RunCameraManager.Update indexes cameras before entitlements resolve -
		// waiting here (up to ~2s) shrinks that gap; RunCameraManager_Update_Finalizer covers the rest.
		private static void WaitForEntitlementsThenTransition(string gamemode, int retriesLeft)
		{
			if (!isSplitscreenEnabled)
				return;

			if (RoR2Application.IsAllUsersEntitlementsUpdated || retriesLeft <= 0)
			{
				TransitionToLobby(gamemode);
				return;
			}

			ExecuteNextFrame.Invoke(() => WaitForEntitlementsThenTransition(gamemode, retriesLeft - 1));
		}

		/// <summary>
		/// Disables splitscreen mode and removes hooks
		/// </summary>
		public static void DisableSplitscreen()
		{
			if (!isSplitscreenEnabled)
				return;

			Print("Disabling splitscreen mode", ELogChannel.Info);

			isSplitscreenEnabled = false;

			RoR2Application.SetIsInLocalMultiplayer(false);
			ClearLocalUsers();
			OnStateChange?.Invoke(false);
			Diagnostics.ReportLocalUsers("splitscreen disabled");
		}

		private static void TransitionToLobby(string gamemode)
		{
			//RoR2.Console.instance.SubmitCmd(null, "transition_command \"gamemode ClassicRun; host 0;\"");
			if (OnlineMode)
				MainMenuController.instance.SetDesiredMenuScreen(MainMenuController.instance.multiplayerMenuScreen);
			//	PlatformSystems.lobbyManager.EnterGameButtonPressed();
			else
				RoR2.Console.instance.SubmitCmd(null, $"transition_command \"gamemode {gamemode}; host 0;\"");
		}

		private static void LoadLocalUsers()
		{
			// Load local users

			List<LocalUserManager.LocalUserInitializationInfo> init = new();

			foreach (var user in localUsers)
			{
				init.Add(new LocalUserManager.LocalUserInitializationInfo()
				{
					player = user.InputPlayer,
					profile = user.Profile
				});

				user.InputPlayer.isPlaying = true;
			}

			LocalUserManager.ClearUsers();
			LocalUserManager.SetLocalUsers([.. init]);
		}

		private static void ClearLocalUsers()
		{
			localUsers.Clear();

			// (specter) Falls back to the platform's default profile (same as vanilla's own
			// bootstrap) rather than crashing if the local user list is ever unexpectedly empty here.
			var profile = LocalUserManager.readOnlyLocalUsersList.Count > 0
				? LocalUserManager.readOnlyLocalUsersList[0].userProfile
				: PlatformSystems.saveSystem.userProfile;

			// Sign the users out before moving their controllers, so they're never
			// signed in while their controllers are being stripped
			LocalUserManager.ClearUsers();

			// Back to the vanilla state: every device belongs to PlayerMain
			var playerMain = ReInput.players.GetPlayer("PlayerMain");

			foreach (var player in ReInput.players.Players)
			{
				if (player != playerMain)
					player.controllers.ClearAllControllers();
			}

			foreach (var controller in ReInput.controllers.Controllers)
			{
				if (!playerMain.controllers.ContainsController(controller.type, controller.id))
					playerMain.controllers.AddController(controller, true);
			}

			if (profile == null)
				return;

			LocalUserManager.SetLocalUsers(new LocalUserManager.LocalUserInitializationInfo[1]
			{
				new LocalUserManager.LocalUserInitializationInfo()
				{
					profile = profile
				}
			});
		}

		public static LocalUser GetUserByInputName(string name)
		{
			return localUsers.Find(user => user.InputPlayer.name == name);
		}
	}
}