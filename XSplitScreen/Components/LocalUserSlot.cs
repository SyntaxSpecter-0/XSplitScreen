using Rewired;
using RoR2;
using RoR2.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dodad.XSplitscreen.Components
{
	/// <summary>
	/// Manages a local user slot for multiplayer input and user assignment.
	/// Handles controller/keyboard assignment, UI updates, and slot management.
	/// </summary>
	public class LocalUserSlot : MonoBehaviour
	{
		#region Static Properties and Fields

		/// <summary>
		/// Collection of all active user slots.
		/// </summary>
		internal static List<LocalUserSlot> Instances { get; private set; }

		/// <summary>
		/// Dictionary mapping device types to UI icons.
		/// </summary>
		internal static Dictionary<string, Sprite> DeviceIcons;

		/// <summary>
		/// Minimum alpha value for device icons when not active.
		/// </summary>
		private const float MIN_DEVICE_ALPHA = 0.4f;

		/// <summary>
		/// Event triggered when a player is assigned or removed from a slot.
		/// </summary>
		public static Action<LocalUserSlot> OnPlayerChanged;

		public static Action OnRemovedKeyboardUser;

		/// <summary>
		/// (specter) Remembers which slot a controller last belonged to, so reconnecting
		/// (LocalUserPanel.OnControllerAddedEvent) sends it back to its previous slot instead of
		/// whatever empty one happens to be found first.
		/// </summary>
		private static readonly Dictionary<int, LocalUserSlot> _lastSlotForController = new();

		internal static LocalUserSlot GetLastSlotForController(int controllerId)
		{
			return _lastSlotForController.TryGetValue(controllerId, out var slot) && Instances != null && Instances.Contains(slot)
				? slot
				: null;
		}
		#endregion

		#region Public Properties

		public UnityEngine.Rect ScreenRect => _options.GetConfigurator<AssignmentConfigurator>()?.Rect ?? new(0, 0, 1, 1);

		/// <summary>
		/// (specter) Releases this slot's profile and resets its accent color. Call before
		/// removing a player from a slot that stays alive, or the next occupant inherits their
		/// profile/color/trails.
		/// </summary>
		internal void ReleaseProfile()
		{
			_options?.GetConfigurator<ProfileConfigurator>()?.ReleaseProfile();
			MainColor = Color.white;
		}

		/// <summary>
		/// The Rewired Player assigned to this slot. Setting this updates associated systems.
		/// </summary>
		public Player LocalPlayer
		{
			get => _localPlayer;
			internal set
			{
				EnsureEventSystemProviderExists();
				SetLocalPlayerListenerState(false);
				_localPlayer = value;
				SetLocalPlayerListenerState(true);
				_provider.eventSystem = value == null ? null : MPEventSystem.FindByPlayer(value);
				name = $"[{(value == null ? "Open" : value.name)}] Local User Slot {Instances.IndexOf(this)}";
				OnPlayerChanged?.Invoke(this);
			}
		}

		/// <summary>
		/// The user profile associated with this slot.
		/// </summary>
		public UserProfile Profile
		{
			get => _profile;
			set
			{
				if (_profile != null)
				{
					OnUnloadProfile?.Invoke();
				}

				_profile = value;

				if (_nameText != null)
					_nameText.text = value != null ? value.name : "Player";

				OnLoadProfile?.Invoke();
			}
		}

		public Color MainColor = Color.white;

		/// <summary>
		/// Determines if this slot is using a keyboard/mouse as input.
		/// </summary>
		public bool IsKeyboardUser => LocalPlayer?.controllers.hasKeyboard ?? false;

		public Action<int> OnNavigateIndex;
		public Action OnCancel;
		public Action OnUnloadProfile;
		public Action OnLoadProfile;
		
		// (specter) No-op stubs - the redesign dropped the dot indicator and the separate
		// Confirm/Cancel buttons, but SlotOptions still calls these.
		public int NavigatorCount { set { } }
		public int NavigatorIndex { set { } }
		public bool EnableConfirmButton { get; set; }
		public bool EnableCancelButton { get; set; }

		#endregion

		#region Internal Fields

		/// <summary>
		/// Input bank to process and debounce player input.
		/// </summary>
		internal InputBank Input = new InputBank();
		internal LocalUserPanel Panel => _panel;
		internal MPEventSystem EventSystem => _provider?.eventSystem;
		#endregion

		#region Private Fields

		private LocalUserPanel _panel;
		private Player _localPlayer;
		private UserProfile _profile;
		private MPEventSystemProvider _provider;
		private UIJuice _juice;

		// (specter) UI elements - matches the userslot.prefab layout (collapsed row that expands in place)
		private Image _deviceIcon;
		private Transform _configuratorContainer; // (specter) CollapsedRow - always active, hosts SlotOptions
		private Transform _collapsedRow;
		private Transform _expandedContent;
		private Image _accentStrip;
		private TextMeshProUGUI _nameText;
		private GameObject _titleTextGO;
		private LanguageTextMeshController _titleController;
		private GameObject _readyCheck;
		private MPButton _yToggleButton;
		private RectTransform _yChevron;
		private SlotOptions _options;
		private TextMeshProUGUI _yLabel, _lbLabel, _rbLabel;
		private TextMeshProUGUI[] _aButtonLabels;
		private Outline _yOutline;
		private Outline[] _aButtonOutlines;
		private bool _themeApplied;
		#endregion

		#region Unity Lifecycle

		/// <summary>
		/// Initializes the slot when added to the scene.
		/// </summary>
		public void Awake()
		{
			if (Instances == null)
			{
				Instances = new List<LocalUserSlot>();
				OnPlayerChanged += ResolveSlotState;
			}

			Instances.Add(this);

			_panel = GetComponentInParent<LocalUserPanel>();

			InitializeUIComponents();
			ResolveSlotState(this);
		}

		/// <summary>
		/// Updates input processing and UI elements.
		/// </summary>
		public void Update()
		{
			if (!LocalUserPanel.AllowChanges) return;

			UpdateInput();
			UpdateDeviceIconAlpha();
			HandleDisplaySlotMovement();
			UpdateExpandedVisibility();
		}

		/// <summary>
		/// (specter) Shows/hides ExpandedContent to match SlotOptions.IsExpanded, and flips the chevron.
		/// </summary>
		private void UpdateExpandedVisibility()
		{
			bool expanded = _options != null && _options.IsExpanded;

			if (_expandedContent.gameObject.activeSelf != expanded)
				_expandedContent.gameObject.SetActive(expanded);

			_yChevron.localRotation = Quaternion.Euler(0, 0, expanded ? 180f : 0f);
		}

		/// <summary>
		/// Cleanup when the slot is destroyed.
		/// </summary>
		public void OnDestroy()
		{
			Instances.Remove(this);
			if (Instances.Count == 0)
			{
				Instances = null;
				OnPlayerChanged -= ResolveSlotState;
			}
			SetLocalPlayerListenerState(false);
		}

		#endregion

		#region Initialization Methods

		/// <summary>
		/// Sets up all UI components for the slot.
		/// </summary>
		private void InitializeUIComponents()
		{
			name = $"[Open] User Slot {Instances.IndexOf(this)}";

			EnsureEventSystemProviderExists();
			SetupTitleUI();
			SetupNavigatorUI();
			SetupOptionsUI();
			SetupJuiceEffects();
		}

		private void SetupOptionsUI()
		{
			// (specter) Lives on CollapsedRow, not ExpandedContent, so it keeps running while
			// collapsed - it's what detects the Y press that opens the panel.
			_configuratorContainer = transform.Find("CollapsedRow");
			_options = _configuratorContainer.gameObject.AddComponent<SlotOptions>();
		}

		/// <summary>
		/// Ensures an event system provider exists for this slot.
		/// </summary>
		private void EnsureEventSystemProviderExists()
		{
			_provider ??= gameObject.AddComponent<MPEventSystemProvider>();
			_provider.fallBackToMainEventSystem = true;
		}

		/// <summary>
		/// (specter) Sets up the collapsed row's UI components (swatch, name, ready check,
		/// title/placeholder text, Y toggle).
		/// </summary>
		private void SetupTitleUI()
		{
			_collapsedRow = transform.Find("CollapsedRow");
			_expandedContent = transform.Find("ExpandedContent");

			_deviceIcon = _collapsedRow.Find("DeviceIcon").GetComponent<Image>();
			_deviceIcon.enabled = false;

			_accentStrip = _collapsedRow.Find("AccentStrip").GetComponent<Image>();
			_nameText = _collapsedRow.Find("NameText").GetComponent<TextMeshProUGUI>();
			_readyCheck = _collapsedRow.Find("ReadyCheck").gameObject;

			// (specter) Localized "press start" placeholder, using RoR2's own SimpleText prefab in
			// place of the plain placeholder Text the prefab-builder script left here.
			var placeholder = _collapsedRow.Find("TitleText");
			if (placeholder != null) Destroy(placeholder.gameObject);

			var messageText = UIHelper.GetPrefab(UIHelper.EUIPrefabIndex.SimpleText);
			messageText.transform.SetParent(_collapsedRow, false);
			messageText.name = "TitleText";
			var messageLayout = messageText.AddComponent<LayoutElement>();
			messageLayout.flexibleWidth = 1;
			_titleController = messageText.GetComponentInChildren<LanguageTextMeshController>();
			var messageHgText = messageText.GetComponent<HGTextMeshProUGUI>();
			messageHgText.maxVisibleLines = 1;
			messageHgText.overflowMode = TextOverflowModes.Overflow;
			// (specter) The stock SimpleText prefab has TMP auto-sizing on, which shrinks the font
			// for the longer keyboard string but not the shorter gamepad one - lock the size so
			// both render identically.
			messageHgText.enableAutoSizing = false;
			messageHgText.fontSize = 15f;
			messageHgText.alignment = TextAlignmentOptions.MidlineLeft;
			messageText.AddComponent<MPButton>();
			_titleTextGO = messageText;
			SetMessage("XSS_PRESS_START_KBM");
			messageText.gameObject.SetActive(true);
		}

		/// <summary>
		/// (specter) Wires the Y-toggle and LB/RB shoulder buttons for mouse clicks (gamepad reads
		/// these directly via InputBank/SlotOptions - this just gives mouse users the same actions).
		/// </summary>
		private void SetupNavigatorUI()
		{
			var yToggle = _collapsedRow.Find("YToggle");
			_yToggleButton = yToggle.gameObject.AddComponent<MPButton>();
			_yToggleButton.allowAllEventSystems = true;
			_yToggleButton.onClick.AddListener(() => _options.ToggleExpanded());
			_yChevron = yToggle.Find("Chevron").GetComponent<RectTransform>();
			_yLabel = yToggle.Find("YCircle/Label").GetComponent<TextMeshProUGUI>();
			_yOutline = yToggle.Find("YCircle").GetComponent<Outline>();

			var lbButtonNode = _expandedContent.Find("TabBar/LBButton");
			var lbButton = lbButtonNode.gameObject.AddComponent<MPButton>();
			lbButton.allowAllEventSystems = true;
			lbButton.onClick.AddListener(() => _options.ClickShoulder(-1));
			_lbLabel = lbButtonNode.Find("Label").GetComponent<TextMeshProUGUI>();

			var rbButtonNode = _expandedContent.Find("TabBar/RBButton");
			var rbButton = rbButtonNode.gameObject.AddComponent<MPButton>();
			rbButton.allowAllEventSystems = true;
			rbButton.onClick.AddListener(() => _options.ClickShoulder(1));
			_rbLabel = rbButtonNode.Find("Label").GetComponent<TextMeshProUGUI>();

			_aButtonLabels = new TextMeshProUGUI[3];
			_aButtonOutlines = new Outline[3];
			var contentNames = new[] { "ProfileContent", "ColorContent", "TrailsContent" };
			for (int i = 0; i < contentNames.Length; i++)
			{
				var content = _expandedContent.Find(contentNames[i]);
				// (specter) Profile/Trails' AButton is a direct child - the old "ValueRow" wrapper
				// was removed when the panel became a single row.
				var aButtonPath = contentNames[i] == "ColorContent" ? "SwatchRow/AButton" : "AButton";
				var aButtonNode = content.Find(aButtonPath);
				var aButton = aButtonNode.gameObject.AddComponent<MPButton>();
				aButton.allowAllEventSystems = true;
				aButton.onClick.AddListener(() => _options.ClickConfirm());
				_aButtonLabels[i] = aButtonNode.Find("Label").GetComponent<TextMeshProUGUI>();
				_aButtonOutlines[i] = aButtonNode.GetComponent<Outline>();
			}

			UpdateInputGlyphs();
		}

		/// <summary>
		/// (specter) (asset, confirm sprite, north sprite, LB/L1 sprite, RB/R1 sprite) per gamepad
		/// family, matching RoR2.Glyphs' own registration calls. Can't use
		/// RoR2.Glyphs.GetGlyphString here - it resolves through Rewired's per-player action
		/// bindings, which our slot players don't have, so it always falls back to blank/unbound.
		/// This builds the same sprite tags directly instead.
		/// </summary>
		// (specter) internal, not private - SplitscreenMenuController reuses xbox/ps4 LB/RB tags
		// verbatim for the static "swap monitors" legend.
		internal static readonly Dictionary<string, (string asset, string confirm, string north, string lb, string rb)> GamepadGlyphs = new()
		{
			["xbox"] = ("tmpsprXboxOneGlyphs", "texXBoxOneGlyphs_0", "texXBoxOneGlyphs_11", "texXBoxOneGlyphs_2", "texXBoxOneGlyphs_6"),
			["ps4"] = ("tmpsprPS4GlyphsUnified", "texPS4GlyphsUnified_0", "texPS4GlyphsUnified_1", "texPS4GlyphsUnified_4", "texPS4GlyphsUnified_5"),
			["ps5"] = ("tmpsprPS5GlyphsUnified", "texPS5GlyphsUnified_Cross", "texPS5GlyphsUnified_Triangle", "texPS5GlyphsUnified_L1", "texPS5GlyphsUnified_R1"),
		};

		/// <summary>
		/// (specter) Shows a controller-family button glyph for Y/North, each A-button/South, and
		/// LB/RB, matching RoR2's own art style. Keyboard has no icon atlas in RoR2 either
		/// (Glyphs.RegisterKeyboard is empty upstream too), so it shows the real bound key text
		/// instead, same as RoR2 does.
		/// </summary>
		private void UpdateInputGlyphs()
		{
			string deviceKey = GetDeviceKeyForCurrentController(_localPlayer);

			// (specter) RoR2's own glyph registry doesn't distinguish 360 vs One (both register the
			// same "tmpsprXboxOneGlyphs" sprite sheet) - only our own device-icon badge does.
			string glyphKey = deviceKey == "xbox360" ? "xbox" : deviceKey;

			if (GamepadGlyphs.TryGetValue(glyphKey, out var glyphs))
			{
				_yLabel.text = $"<sprite=\"{glyphs.asset}\" name=\"{glyphs.north}\">";
				foreach (var label in _aButtonLabels)
					label.text = $"<sprite=\"{glyphs.asset}\" name=\"{glyphs.confirm}\">";
				_lbLabel.text = $"<sprite=\"{glyphs.asset}\" name=\"{glyphs.lb}\">";
				_rbLabel.text = $"<sprite=\"{glyphs.asset}\" name=\"{glyphs.rb}\">";
			}
			else
			{
				// (specter) Keyboard: no icon atlas, so show the real bound key text (matches
				// RoR2's own keyboard fallback behavior).
				_yLabel.text = "Tab";
				foreach (var label in _aButtonLabels)
					label.text = "Enter";
				_lbLabel.text = "Q";
				_rbLabel.text = "E";
			}

			// (specter) Reuse RoR2's own button color for the badge outlines instead of a guessed hex value.
			var outlineColor = SplitscreenMenuController.RoR2ButtonColor;
			_yOutline.effectColor = outlineColor;
			foreach (var outline in _aButtonOutlines)
				outline.effectColor = outlineColor;

			ApplyRoR2Theme();
		}

		/// <summary>
		/// (specter) Applies RoR2's real font (not its material - the shadow/underlay it carries
		/// is tuned for larger native text and reads as a gray haze at this size) and its real
		/// button/text colors, so the row matches the game's own panels instead of a bare outline.
		/// </summary>
		private void ApplyRoR2Theme()
		{
			if (_themeApplied || SplitscreenMenuController.RoR2Font == null)
				return;
			_themeApplied = true;

			foreach (var tmp in GetComponentsInChildren<TextMeshProUGUI>(true))
				tmp.font = SplitscreenMenuController.RoR2Font;

			var buttonColor = SplitscreenMenuController.RoR2ButtonColor;
			var collapsedImage = _collapsedRow.GetComponent<Image>();
			collapsedImage.color = buttonColor;
			AddBoxHighlight(collapsedImage.gameObject);

			var expandedImage = _expandedContent.GetComponent<Image>() ?? _expandedContent.gameObject.AddComponent<Image>();
			expandedImage.color = buttonColor;
			expandedImage.raycastTarget = false;
			AddBoxHighlight(expandedImage.gameObject);
		}

		/// <summary>
		/// (specter) RoR2's own panels have a thin lighter border around their fill color - an
		/// Outline component on a solid Image gives the same look, since its 4 diagonal shadow
		/// copies only peek out past the un-shifted original at the edges.
		/// </summary>
		private static void AddBoxHighlight(GameObject target)
		{
			var outline = target.GetComponent<Outline>() ?? target.AddComponent<Outline>();
			outline.effectColor = SplitscreenMenuController.RoR2MutedColor;
			outline.effectDistance = new Vector2(1.5f, 1.5f);
			outline.useGraphicAlpha = false;
		}

		/// <summary>
		/// Sets up transition effects for the UI.
		/// </summary>
		private void SetupJuiceEffects()
		{
			var canvas = gameObject.AddComponent<CanvasGroup>();
			_juice = gameObject.AddComponent<UIJuice>();
			_juice.canvasGroup = canvas;
			_juice.transitionDuration = 0.5f;
			_juice.originalAlpha = 1f;
		}

		#endregion

		#region Update Methods

		/// <summary>
		/// Updates input state from the assigned player.
		/// </summary>
		private void UpdateInput()
		{
			Input.Update(LocalPlayer, _options != null && _options.IsExpanded);
		}

		/// <summary>
		/// Updates the alpha of the device icon based on input activity.
		/// </summary>
		private void UpdateDeviceIconAlpha()
		{
			var currentColor = new Color(MainColor.r, MainColor.g, MainColor.b, _deviceIcon.color.a);
			float alphaDirection = Time.deltaTime * 10f * (LocalPlayer != null && (Input.MouseActive || Input.Any) ? 1 : -1);
			var newAlpha = Mathf.Clamp(currentColor.a + alphaDirection, MIN_DEVICE_ALPHA, 1f);
			_deviceIcon.color = new Color(currentColor.r, currentColor.g, currentColor.b, newAlpha);

			_accentStrip.color = MainColor; // (specter) reflects the player's chosen color
		}

		/// <summary>
		/// Handles movement of this slot between display panels.
		/// </summary>
		private void HandleDisplaySlotMovement()
		{
			if (IsKeyboardUser)
				return;

			// (specter) LB/RB switch tabs while the panel is open - don't also move the slot to
			// another display.
			if (_options.IsExpanded)
				return;

			int displayDirection = GetDisplayDirection();
			if (displayDirection == 0) return;

			MoveSlotByDirection(displayDirection);
		}

		/// <summary>
		/// Gets the direction for display movement based on shoulder button input.
		/// </summary>
		private int GetDisplayDirection()
		{
			if (Input.LB) return -1;
			if (Input.RB) return 1;
			return 0;
		}

		/// <summary>
		/// Moves the slot to another display panel.
		/// </summary>
		internal void MoveSlotByDirection(int displayDirection)
		{
			var currentPanel = _panel;
			int panelIndex = LocalUserPanel.Instances.IndexOf(_panel);
			if (panelIndex == -1) return;

			bool foundDisplay = false;
			int panelCount = LocalUserPanel.Instances.Count;

			while (!foundDisplay)
			{
				// Calculate next panel index with wraparound
				panelIndex = (panelIndex + displayDirection + panelCount) % panelCount;

				var targetPanel = LocalUserPanel.Instances[panelIndex];
				foundDisplay = targetPanel.TryAddSlot(this);

				// If found and the panel isn't full, add a free slot if possible
				if (foundDisplay)
				{
					_panel = targetPanel;
					_options.ForceClose();
					TryAddFreeSlotToPanel(currentPanel);
				}
			}
		}

		/// <summary>
		/// Attempts to add a free slot to the panel if needed.
		/// </summary>
		private void TryAddFreeSlotToPanel(LocalUserPanel panel)
		{
			if (panel.transform.childCount != LocalUserPanel.MAX_USERS)
			{
				var freeSlot = panel.GetFreeSlot();
				if (freeSlot == null)
				{
					panel.AddSlot();
				}
			}
		}

		#endregion

		#region Slot State Management

		/// <summary>
		/// Try to remove this player or invoke OnCancel to notify the options
		/// </summary>
		public void TryRemoveSlot()
		{
			if (!LocalUserPanel.AllowChanges)
				return;

			if(!_options.IsExpanded)
			{
				bool isKeyboard = IsKeyboardUser;

				_panel.TryRemovePlayerFromSlot(_localPlayer, this);

				if (isKeyboard)
				{
					OnRemovedKeyboardUser?.Invoke();
				}
			}
			else
			{
				OnCancel?.Invoke();
			}
		}

		/// <summary>
		/// Enable, disable or delete the slot based on state.
		/// </summary>
		private static void ResolveSlotState(LocalUserSlot slot)
		{
			if (slot.LocalPlayer != null)
			{
				slot.ActivateOccupiedSlot();
			}
			else
			{
				slot.HandleEmptySlot();
			}
		}

		/// <summary>
		/// Configure the slot for an assigned player.
		/// </summary>
		private void ActivateOccupiedSlot()
		{
			ResolveDeviceIcon();
			SetSlotUIState(true);
			_options.OpenProfileConfigurator();
			_juice.TransitionAlphaFadeIn();
		}

		/// <summary>
		/// Handle the slot when it's empty (available for a new player).
		/// </summary>
		private void HandleEmptySlot()
		{
			// (specter) Compare by panel, not direct parent - slots now live in one of 2 columns
			// (LocalUserPanel.CreateColumn), so a parent check would only see one column's slots.
			var siblings = Instances.Where(x => x._panel == _panel);
			int instanceCount = siblings.Count();

			bool hasOtherEmptySlot = siblings.Any(x => !(x == null) && x._localPlayer == null && x != this);
			bool canRemove = instanceCount > 1 && Instances.Count < RoR2Application.maxLocalPlayers;

			if (canRemove && hasOtherEmptySlot)
			{
				gameObject.SetActive(false);
				gameObject.transform.SetParent(null);
				Destroy(gameObject);
			}
			else
			{
				SetSlotUIState(false);

				bool hasKeyboardUser = Instances.Any(x => x.IsKeyboardUser);

				_titleController.GetComponent<MPButton>().onClick.RemoveAllListeners();

				if (_panel.MonitorId != 0 || hasKeyboardUser)
				{
					SetMessage("XSS_PRESS_START");
				}
				else
				{
					_titleController.GetComponent<MPButton>().onClick.AddListener(() =>
					{
						var controllers = new Controller[2];
						controllers[0] = ReInput.controllers.Keyboard as Controller;
						controllers[1] = ReInput.controllers.Mouse as Controller;

						GetComponentInParent<LocalUserPanel>().TryAddControllersToSlot(controllers);
					});

					SetMessage("XSS_PRESS_START_KBM");
				}

				transform.SetAsLastSibling();
				_juice.TransitionAlphaFadeIn();
			}
		}

		/// <summary>
		/// (specter) Sets the UI state based on whether the slot is filled or empty. CollapsedRow
		/// itself stays active either way (it hosts SlotOptions and the empty-state placeholder
		/// text) - only its occupied-state children and the SlotOptions component itself toggle.
		/// </summary>
		private void SetSlotUIState(bool isOccupied)
		{
			_deviceIcon.enabled = isOccupied;
			_accentStrip.gameObject.SetActive(isOccupied);
			_nameText.gameObject.SetActive(isOccupied);
			_readyCheck.SetActive(isOccupied);
			_yToggleButton.gameObject.SetActive(isOccupied);
			_titleTextGO.SetActive(!isOccupied);
			_options.enabled = isOccupied;

			if (!isOccupied)
				_expandedContent.gameObject.SetActive(false);

			if(!isOccupied)
			{
				OnRemovedKeyboardUser += RefreshIfEmpty;
			}
			else
			{
				OnRemovedKeyboardUser -= RefreshIfEmpty;
			}

		}

		private void RefreshIfEmpty()
		{
			OnRemovedKeyboardUser -= RefreshIfEmpty;

			HandleEmptySlot();
		}

		#endregion

		#region Player Management

		/// <summary>
		/// Subscribe or unsubscribe from player events.
		/// </summary>
		private void SetLocalPlayerListenerState(bool subscribe)
		{
			if (_localPlayer != null)
			{
				if (subscribe)
				{
					_localPlayer.controllers.ControllerAddedEvent += OnControllerAdded;
					_localPlayer.controllers.ControllerRemovedEvent += OnControllerRemoved;
				}
				else
				{
					_localPlayer.controllers.ControllerAddedEvent -= OnControllerAdded;
					_localPlayer.controllers.ControllerRemovedEvent -= OnControllerRemoved;
				}
			}
		}

		/// <summary>
		/// Updates the device icon based on the current controller.
		/// </summary>
		private void ResolveDeviceIcon()
		{
			UpdateInputGlyphs();

			string deviceKey = GetDeviceKeyForCurrentController(_localPlayer);

			if (_deviceIcon.sprite != null && _deviceIcon.sprite.name == deviceKey)
				return;

			if (DeviceIcons.TryGetValue(deviceKey, out Sprite sprite) && sprite != null)
				_deviceIcon.sprite = Instantiate(sprite);
		}

		/// <summary>
		/// Gets the device key for the controller assigned to a player.
		/// </summary>
		public static string GetDeviceKeyForCurrentController(Player localPlayer)
		{
			if (localPlayer == null || !localPlayer.controllers.Controllers.Any())
				return "x";

			return GetDeviceKeyFromController(localPlayer.controllers.Controllers.First());
		}

		private static readonly HashSet<int> _loggedDeviceNames = new();

		/// <summary>
		/// (specter) Gets the device key for a controller. Checks Rewired's hardware GUID first
		/// (reliable regardless of what the OS/driver reports as the name), falling back to name
		/// substring matching for an unidentified HID device.
		/// </summary>
		public static string GetDeviceKeyFromController(Controller controller)
		{
			if (controller == null)
				return "x";

			if (controller is Keyboard || controller is Mouse)
				return "keyboard";

			if (controller is Joystick joystick)
			{
				var guid = joystick.hardwareTypeGuid;
				if (guid == RoR2.DefaultControllerMaps.xbox360ControllerGuid) return "xbox360";
				if (guid == RoR2.DefaultControllerMaps.xboneControllerGuid) return "xbox";
				if (guid == RoR2.DefaultControllerMaps.PS4Guid) return "ps4";
				if (guid == RoR2.DefaultControllerMaps.PS5Guid) return "ps5";
			}

			// (specter) `name` is often a generic driver label - also check `hardwareName`, and log
			// unrecognized values so this matching can be tightened later.
			string name = (controller.name ?? "").ToLower();
			string hardwareName = (controller is Joystick j ? j.hardwareName : null)?.ToLower() ?? "";

			if (!_loggedDeviceNames.Contains(controller.id))
			{
				_loggedDeviceNames.Add(controller.id);
				Log.Print($"XSplitscreen device-icon diagnostic: controller.name='{controller.name}', hardwareName='{(controller is Joystick j2 ? j2.hardwareName : null)}', hardwareTypeGuid='{(controller is Joystick j3 ? j3.hardwareTypeGuid.ToString() : null)}'");
			}

			bool Has(string s, string term) => s.Contains(term);

			if (Has(name, "dualsense") || Has(hardwareName, "dualsense") || Has(name, "ps5") || Has(hardwareName, "ps5"))
				return "ps5";

			if (Has(name, "sony") || Has(hardwareName, "sony") || Has(name, "dualshock") || Has(hardwareName, "dualshock")
				|| Has(name, "playstation") || Has(hardwareName, "playstation") || Has(name, "ps4") || Has(hardwareName, "ps4")
				|| Has(name, "wireless controller") || Has(hardwareName, "wireless controller"))
				return "ps4";

			if (Has(name, "360") || Has(hardwareName, "360"))
				return "xbox360";

			return "xbox";
		}

		/// <summary>
		/// Handler for when a controller is added to the player.
		/// </summary>
		public void OnControllerAdded(ControllerAssignmentChangedEventArgs args)
		{
			ResolveDeviceIcon();
		}

		/// <summary>
		/// Handler for when a controller is removed from the player.
		/// </summary>
		public void OnControllerRemoved(ControllerAssignmentChangedEventArgs args)
		{
			if (args.controller != null)
				_lastSlotForController[args.controller.id] = this;

			ResolveDeviceIcon();
		}

		#endregion

		#region UI Methods

		/// <summary>
		/// Sets the message text in the title area.
		/// </summary>
		public void SetMessage(string token)
		{
			if (token == null)
				_titleTextGO.SetActive(false);
			else
			{
				_titleController.token = token;
				_titleTextGO.SetActive(true);
			}
		}

		#endregion

		#region Input Bank Class

		/// <summary>
		/// Provides input from an available input player, debounced with configurable press delay.
		/// </summary>
		public class InputBank
		{
			/// <summary>
			/// (specter) Rewired action IDs. 0/1 = analog stick, for continuous controls (cursor,
			/// hue scrub). 12/13 = UIHorizontal/UIVertical, the d-pad signal on controllers with no
			/// detectable Hat, for discrete steps. North/Y is RoR2's "Equipment" action.
			/// </summary>
			private const int UIHorizontalActionId = 12;
			private const int UIVerticalActionId = 13;
			private const int NorthButtonId = 6;

			public bool Any { get; private set; }
			public bool Left { get; private set; }
			public bool Right { get; private set; }
			public bool South { get; private set; }
			public bool East { get; private set; }
			public bool North { get; private set; }
			public bool Up { get; private set; }
			public bool Down { get; private set; }
			public bool LB { get; private set; }
			public bool RB { get; private set; }

			/// <summary>
			/// (specter) Raw (non-debounced) held state, for controls that need continuous scrub
			/// (e.g. the color hue bar) rather than one discrete step per press.
			/// </summary>
			public bool LeftHeld { get; private set; }
			public bool RightHeld { get; private set; }

			public float LeftRightDelta { get; private set; }
			public float UpDownDelta { get; private set; }
			public bool MouseLeft { get; private set; }
			public bool MouseActive { get; private set; }

			private float pressDelay = 0.3f;
			private float leftTimer, rightTimer, southTimer, eastTimer, northTimer, upTimer, downTimer, lbTimer, rbTimer;

			/// <summary>
			/// Updates the input bank, debouncing input events.
			/// </summary>
			/// <param name="panelExpanded">
			/// (specter) Whether this slot's settings panel is open - gamepad uses the d-pad to
			/// browse/adjust values while open, and the stick otherwise.
			/// </param>
			public void Update(Player player, bool panelExpanded = false)
			{
				Any = Left = Right = South = East = North = Up = Down = LB = RB = MouseLeft = MouseActive = false;
				LeftHeld = RightHeld = false;
				LeftRightDelta = UpDownDelta = 0f;

				leftTimer -= Time.deltaTime;
				rightTimer -= Time.deltaTime;
				southTimer -= Time.deltaTime;
				eastTimer -= Time.deltaTime;
				northTimer -= Time.deltaTime;
				upTimer -= Time.deltaTime;
				downTimer -= Time.deltaTime;
				lbTimer -= Time.deltaTime;
				rbTimer -= Time.deltaTime;

				if (player == null) return;

				// (specter) Analog stick - drives continuous controls (cursor, hue scrub).
				LeftRightDelta = player.GetAxis(0);
				UpDownDelta = player.GetAxis(1);

				// (specter) D-pad discrete steps (see class doc comment for why UIHorizontal/Vertical).
				float dpadHorizontal = player.GetAxis(UIHorizontalActionId);
				float dpadVertical = player.GetAxis(UIVerticalActionId);

				// (specter) UIHorizontal/Vertical also fires from the analog stick on some
				// controllers (confirmed on a DS4), which defeats the point of separating d-pad
				// from stick input. Read the Hat directly when the controller has one - that's
				// tied to the physical d-pad switch only.
				if (player.controllers.Joysticks.Count > 0)
				{
					var joystick = player.controllers.Joysticks[0];
					if (joystick.hatCount > 0)
					{
						var hat = joystick.Hats[0];
						dpadHorizontal = hat.buttonRight.value ? 1f : (hat.buttonLeft.value ? -1f : 0f);
						dpadVertical = hat.buttonUp.value ? 1f : (hat.buttonDown.value ? -1f : 0f);
					}
				}

				bool southValue = player.GetButtonDown(14);
				bool eastValue = player.GetButtonDown(15);
				bool northValue = player.GetButtonDown(NorthButtonId);
				bool lbValue = player.GetButtonDown(9);
				bool rbValue = player.GetButtonDown(10);
;
				if (player.controllers.hasKeyboard)
				{
					// (specter) Keyboard scheme: Tab opens/closes the panel, Q/E switch tabs, Up/Down
					// browse values (Profile/Trails), Left/Right scrub the hue (Color), Enter
					// confirms, Escape cancels/closes. Arrow keys are no longer double-booked as
					// confirm/cancel.
					LeftRightDelta +=
						(player.controllers.Keyboard.GetKey(KeyCode.LeftArrow) ? -1 : 0)
						+
						(player.controllers.Keyboard.GetKey(KeyCode.RightArrow) ? 1 : 0);

					UpDownDelta +=
						(player.controllers.Keyboard.GetKey(KeyCode.DownArrow) ? -1 : 0)
						+
						(player.controllers.Keyboard.GetKey(KeyCode.UpArrow) ? 1 : 0);

					var scrollDelta = player.controllers.Mouse.GetAxis(2);
					UpDownDelta += scrollDelta;

					if (scrollDelta != 0)
						upTimer = downTimer = 0;

					southValue |= player.controllers.Keyboard.GetKey(KeyCode.Return) || player.controllers.Keyboard.GetKey(KeyCode.KeypadEnter);
					eastValue |= player.controllers.Keyboard.GetKey(KeyCode.Escape);
					northValue |= player.controllers.Keyboard.GetKeyDown(KeyCode.Tab);
					lbValue |= player.controllers.Keyboard.GetKeyDown(KeyCode.Q);
					rbValue |= player.controllers.Keyboard.GetKeyDown(KeyCode.E);

					MouseLeft = player.controllers.Mouse.GetButton(0);
					MouseActive = new Vector2(LeftRightDelta, UpDownDelta).sqrMagnitude > 0.1f;
				}

				// (specter) Gamepad uses the d-pad while a panel is open, the stick otherwise.
				// Keyboard always uses arrow keys (already folded into LeftRightDelta/UpDownDelta above).
				bool useStick = player.controllers.hasKeyboard || !panelExpanded;
				float navHorizontal = useStick ? LeftRightDelta : dpadHorizontal;
				float navVertical = useStick ? UpDownDelta : dpadVertical;

				// (specter) Use navHorizontal, not the raw stick - keeps ColorConfigurator's
				// hue-scrub on the d-pad while a panel is open, same as Left/Right below.
				LeftHeld = navHorizontal < -0.3f;
				RightHeld = navHorizontal > 0.3f;

				if (leftTimer <= 0 && navHorizontal < -0.3f)
				{
					Left = true;
					leftTimer = pressDelay;
				}
				if (rightTimer <= 0 && navHorizontal > 0.3f)
				{
					Right = true;
					rightTimer = pressDelay;
				}
				if (upTimer <= 0 && navVertical > 0.3f)
				{
					Up = true;
					upTimer = pressDelay;
				}
				if (downTimer <= 0 && navVertical < -0.3f)
				{
					Down = true;
					downTimer = pressDelay;
				}
				if (southTimer <= 0 && southValue)
				{
					South = true;
					southTimer = pressDelay;
				}
				if (eastTimer <= 0 && eastValue)
				{
					East = true;
					eastTimer = pressDelay;
				}
				if (northTimer <= 0 && northValue)
				{
					North = true;
					northTimer = pressDelay;
				}
				if (lbTimer <= 0 && lbValue)
				{
					LB = true;
					lbTimer = pressDelay;
				}
				if (rbTimer <= 0 && rbValue)
				{
					RB = true;
					rbTimer = pressDelay;
				}

				Any = player.GetAnyButton();
			}
		}

		#endregion
	}
}