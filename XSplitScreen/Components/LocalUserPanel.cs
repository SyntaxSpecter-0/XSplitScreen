using Rewired;
using RoR2;
using RoR2.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dodad.XSplitscreen.Components
{
	internal class LocalUserPanel : MonoBehaviour
	{
		internal static List<LocalUserPanel> Instances { get; private set; }

		/// <summary>
		/// Should players be allowed to make changes to any slots or slot options?
		/// </summary>
		public static bool AllowChanges { get; internal set; }

		private static bool subscribed;

		internal const int MAX_USERS = 16;

		public SplitscreenMenuController Controller => _controller;
		internal LocalUserSlot[] UserSlots => userContainer.GetComponentsInChildren<LocalUserSlot>();

		// (specter) Content's own direct children are now just the 2 column containers, not
		// slots (see Initialize()) - count actual LocalUserSlot components instead.
		internal int FilledSlots => UserSlots.Length;

		private GameObject userPrefab;
		private Transform userContainer;

		/// <summary>
		/// (specter) The 2 side-by-side columns slots are actually parented into - see the setup
		/// in Initialize(). userContainer (Content) itself only holds these two.
		/// </summary>
		private Transform _columnLeft;
		private Transform _columnRight;

		internal int MonitorId { get; private set; }

		private SplitscreenMenuController _controller;

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// (specter) Drives the 2-column layout by hand every frame: each column's width/X
		/// position, and Content's height (the taller column). HorizontalLayoutGroup's cross-axis
		/// positioning wouldn't hold a column at a fixed offset no matter how childControlHeight
		/// was set (confirmed via testing), so columns are plain top-left-anchored points (see
		/// CreateColumn) with no LayoutGroup involved at all - just field assignments here.
		/// </summary>
		public void LateUpdate()
		{
			if (userContainer == null || _columnLeft == null || _columnRight == null) return;

			const float halfGap = 4f; // (specter) half of an 8px gap between the columns

			var contentRect = (RectTransform) userContainer;
			var leftRect = (RectTransform) _columnLeft;
			var rightRect = (RectTransform) _columnRight;

			float columnWidth = Mathf.Max(0f, contentRect.rect.width / 2f - halfGap);

			leftRect.anchoredPosition = new Vector2(0f, 0f);
			leftRect.sizeDelta = new Vector2(columnWidth, leftRect.sizeDelta.y);

			rightRect.anchoredPosition = new Vector2(columnWidth + halfGap * 2f, 0f);
			rightRect.sizeDelta = new Vector2(columnWidth, rightRect.sizeDelta.y);

			contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, Mathf.Max(leftRect.rect.height, rightRect.rect.height));
		}

		public void Update()
		{
			if (!AllowChanges || MonitorId != 0)
				return;

			var playerList = ReInput.players.Players;

			for (int e = 0; e < playerList.Count; e++)
			{
				var currentPlayer = playerList[e];
				var slot = FindSlotByPlayer(currentPlayer);

				var controller = currentPlayer.controllers.GetLastActiveController();

				if (LocalUserSlot.GetDeviceKeyFromController(controller) == "keyboard")
					continue;

				if (currentPlayer.name == "PlayerMain")
				{
					if (slot != null)
						continue;

					if (currentPlayer.GetButtonDown(11))
						TryAddPlayerToSlot(currentPlayer);
				}
			}
		}

		internal void TryAddPlayerToSlot(Player currentPlayer)
		{
			TryAddControllersToSlot(new Controller[1] { currentPlayer.controllers.GetLastActiveController() });
		}

		internal void TryAddControllersToSlot(Controller[] controllers)
		{
			if (LocalUserSlot.Instances.Count >= RoR2Application.maxLocalPlayers)
				return;

			foreach (var panel in Instances)
			{
				if (panel.FilledSlots >= MAX_USERS)
					continue;

				var freeSlot = panel.GetFreeSlot();
				var freePlayer = panel.GetFreePlayer();

				if (freeSlot == null || freePlayer == null)
					continue;

				Log.Print($"LocalUserPanel.TryAddControllersToSlot: '{panel.name}' adding new player '{freePlayer.name}', slot '{freeSlot.name}' (existing slot is null)");

				foreach (var controller in controllers)
					freePlayer.controllers.AddController(controller, !(controller is Keyboard || controller is Mouse));

				freeSlot.LocalPlayer = freePlayer;

				if (panel.FilledSlots != MAX_USERS)
					panel.AddSlot();

				EventSystem.current.SetSelectedGameObject(null);
				break;
			}
		}

		internal void TryRemovePlayerFromSlot(Player currentPlayer, LocalUserSlot slot)
		{
			if (slot == null)
				return;

			Log.Print($"LocalUserPanel.TryRemovePlayerFromSlot: Removing '{slot.LocalPlayer.name}'");

			var controllers = slot.LocalPlayer.controllers.Controllers;
			var main = LocalUserManager.GetRewiredMainPlayer();

			foreach (var controller in controllers)
				main.controllers.AddController(controller, true);

			slot.ReleaseProfile();
			slot.LocalPlayer = null;
			currentPlayer.SetVibration(0, 0, true);
		}
		//-----------------------------------------------------------------------------------------------------------

		public void OnDestroy()
		{
			Instances.Remove(this);
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Attempt to transfer a slot to this panel
		/// </summary>
		/// <param name="slot"></param>
		/// <returns></returns>
		internal bool TryAddSlot(LocalUserSlot slot)
		{
			var freeSlot = GetFreeSlot();

			if (freeSlot == null)
				return false;

			// (specter) into one of the 2 columns, not Content directly - see Initialize().
			slot.transform.SetParent(GetTargetColumn());

			if (transform.childCount == MAX_USERS)
				Destroy(freeSlot.gameObject);
			else
				freeSlot.transform.SetAsLastSibling();

			return true;
		}

		//-----------------------------------------------------------------------------------------------------------

		internal void Initialize(SplitscreenMenuController controller, int id)
		{
			try
			{
				Log.Print($"[{this.GetType().Name}.{MethodBase.GetCurrentMethod().Name}] : id -> '{id}'");

				// Vars

				this.MonitorId = id;
				
				Instances ??= new();
				Instances.Add(this);

				_controller = controller;

				// Juice

				var juice = gameObject.AddComponent<UIJuice>();

				juice.canvasGroup = gameObject.GetComponent<CanvasGroup>();
				juice.panningRect = gameObject.GetComponent<RectTransform>();
				juice.panningMagnitude = 30;
				juice.transitionDuration = 0.5f;
				juice.transitionStartPosition = new Vector2(-30, 0);
				juice.originalAlpha = 1f;

				SplitscreenMenuController.Singleton.onEnter.AddListener(() =>
				{
					juice.TransitionAlphaFadeIn();
					juice.TransitionPanFromLeft();
				});

				// (specter) The visible list scrolls (see menu.prefab's Slots/Viewport/Content) -
				// slots are parented into Content, not the ScrollRect root itself.
				userContainer = transform.Find("Slots/Viewport/Content");

				// (specter) Content ships with a single-column VerticalLayoutGroup + ContentSizeFitter.
				// A HorizontalLayoutGroup couldn't hold the 2 columns at a fixed cross-axis offset no
				// matter the childControlHeight setting (confirmed via testing), so both are removed:
				// columns anchor directly to Content's corners instead (see CreateColumn), and
				// LateUpdate sizes Content to the taller column by hand.
				// DestroyImmediate, not Destroy: a GameObject can only carry one LayoutGroup, and
				// Destroy() doesn't remove it until end of frame - code right after this would still
				// see the old component present.
				var oldLayout = userContainer.GetComponent<VerticalLayoutGroup>();
				if (oldLayout != null)
					UnityEngine.Object.DestroyImmediate(oldLayout);
				var oldFitter = userContainer.GetComponent<ContentSizeFitter>();
				if (oldFitter != null)
					UnityEngine.Object.DestroyImmediate(oldFitter);

				_columnLeft = userContainer.Find("ColumnLeft");
				_columnRight = userContainer.Find("ColumnRight");
				if (_columnLeft == null || _columnRight == null)
				{
					_columnLeft = CreateColumn(userContainer, "ColumnLeft");
					_columnRight = CreateColumn(userContainer, "ColumnRight");
				}

				userPrefab ??= Plugin.Resources.LoadAsset<GameObject>("UserSlot.prefab");

				if (LocalUserSlot.Instances == null || LocalUserSlot.Instances.Count < RoR2Application.maxLocalPlayers)
					AddSlot();

				SplitscreenMenuController.Singleton.onEnter.AddListener(OnEnter);
				SplitscreenMenuController.Singleton.onExit.AddListener(OnExit);
			}
			catch (Exception e)
			{
				Log.Print(e, Log.ELogChannel.Fatal);

				return;
			}
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Add a new slot tracker
		/// </summary>
		internal void AddSlot()
		{
			var newSlot = GameObject.Instantiate(userPrefab, GetTargetColumn());

			newSlot.gameObject.AddComponent<LocalUserSlot>();

			newSlot.gameObject.SetActive(true);
		}

		/// <summary>
		/// (specter) Whichever of the 2 columns currently has fewer slots in it.
		/// </summary>
		private Transform GetTargetColumn() => _columnLeft.childCount <= _columnRight.childCount ? _columnLeft : _columnRight;

		/// <summary>
		/// (specter) Builds one of the 2 side-by-side columns. Anchored as a plain top-left point
		/// (not stretched, no parent LayoutGroup) - LateUpdate drives its position/size by hand.
		/// Internally still a plain VerticalLayoutGroup + ContentSizeFitter, same as Content's old
		/// single-column setup.
		/// </summary>
		private static Transform CreateColumn(Transform parent, string name)
		{
			var go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);

			var rt = (RectTransform) go.transform;
			rt.anchorMin = new Vector2(0f, 1f);
			rt.anchorMax = new Vector2(0f, 1f);
			rt.pivot = new Vector2(0f, 1f);

			var vertical = go.AddComponent<VerticalLayoutGroup>();
			vertical.padding = new RectOffset(0, 0, 0, 0);
			vertical.spacing = 2;
			vertical.childAlignment = TextAnchor.UpperCenter;
			vertical.childForceExpandWidth = true;
			vertical.childForceExpandHeight = false;
			vertical.childControlWidth = true;
			vertical.childControlHeight = true;

			var fitter = go.AddComponent<ContentSizeFitter>();
			fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			return go.transform;
		}

		//-----------------------------------------------------------------------------------------------------------

		internal LocalUserSlot GetFreeSlot()
		{
			foreach (var instance in LocalUserSlot.Instances)
			{
				// (specter) slots live inside one of the 2 columns now, not directly under
				// userContainer (Content) - see Initialize().
				if (instance.transform.parent != _columnLeft && instance.transform.parent != _columnRight)
					continue;

				if (instance.LocalPlayer == null)
					return instance;
			}

			return null;
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Get the first input player without an assigned controller
		/// </summary>
		private Player GetFreePlayer()
		{
			var players = ReInput.players.Players;
			int playerCount = players.Count;

			for (int e = 1; e < playerCount; e++)
			{
				if (players[e].controllers.joystickCount == 0 && !players[e].controllers.hasKeyboard)
					return players[e];
			}

			return null;
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Find the local slot for the provided player
		/// </summary>
		/// <param name="player"></param>
		/// <returns></returns>
		private LocalUserSlot FindSlotByPlayer(Player player)
		{
			foreach (var instance in LocalUserSlot.Instances)
			{
				if (instance.LocalPlayer == null || (instance.LocalPlayer.controllers.joystickCount == 0 && !instance.LocalPlayer.controllers.hasKeyboard))
					continue;

				if (instance.LocalPlayer.name == player.name)
					return instance;
			}

			return null;
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Temporarily assign all controllers to individual input users
		/// </summary>
		private void OnEnter()
		{
			if (!subscribed)
			{
				ReInput.ControllerConnectedEvent += OnControllerAddedEvent;
				subscribed = true;
			}

			// Load device icon resources for LocalUserSlot

			if (LocalUserSlot.DeviceIcons == null)
			{
				var availableIcons = Plugin.Resources.GetAllAssetNames()
					.Where(x => x.ToLower().Contains("device_")).ToList();

				LocalUserSlot.DeviceIcons = new Dictionary<string, Sprite>();

				foreach (var icon in availableIcons)
				{
					string fileName = icon.Substring(icon.LastIndexOf('/') + 1);
					string key = fileName.Replace("device_", "").Replace(".png", "");

					LocalUserSlot.DeviceIcons.Add(key, Plugin.Resources.LoadAsset<Sprite>(icon));
				}
			}
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// DEBUG: Resets controller changes
		/// </summary>
		private void OnExit()
		{
			if (subscribed)
			{
				ReInput.ControllerConnectedEvent -= OnControllerAddedEvent;
				subscribed = false;
			}
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Spread controllers out among input users
		/// </summary>
		/// <param name="args"></param>
		public void OnControllerAddedEvent(ControllerStatusChangedEventArgs args)
		{
			// (specter) Route a reconnecting controller back to its previous slot first - otherwise
			// two slots with zero controllers would race for whichever reconnects first.
			var previousSlot = LocalUserSlot.GetLastSlotForController(args.controllerId);

			if (previousSlot?.LocalPlayer != null && previousSlot.LocalPlayer.controllers.Controllers.Count() == 0)
			{
				previousSlot.LocalPlayer.controllers.AddController(args.controller, false);

				return;
			}

			foreach (var slot in LocalUserSlot.Instances)
			{
				if (slot.LocalPlayer != null &&
					slot.LocalPlayer.controllers.Controllers.Count() == 0)
				{
					slot.LocalPlayer.controllers.AddController(args.controller, false);

					return;
				}
			}

			var players = ReInput.players.Players;
			int playerCount = players.Count;
			int playerId = 2;

			for (int e = playerId; e < playerCount; e++)
			{
				if (players[e].controllers.joystickCount == 0)
				{
					players[e].controllers.AddController(args.controller, false);

					return;
				}
			}
		}
	}
}
