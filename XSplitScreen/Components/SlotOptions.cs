using RoR2.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dodad.XSplitscreen.Components
{
	/// <summary>
	/// (specter) Manages a user slot's Profile/Color/Trails tabs and their UI.
	///
	/// Gamepad: North (Y) opens/closes the panel; LB/RB switch tabs; the active tab's own axis
	/// previews/acts on a value; South confirms without closing.
	/// Keyboard/mouse: South opens/confirms/closes; Up/Down browses tabs.
	/// </summary>
	public class SlotOptions : MonoBehaviour
	{
		#region Properties and Fields

		/// <summary>
		/// Reference to the parent LocalUserSlot.
		/// </summary>
		internal LocalUserSlot Slot { get; private set; }

		/// <summary>
		/// (specter) All configurators (used by the keyboard/mouse flow, and by GetConfigurator&lt;T&gt;()).
		/// </summary>
		private List<OptionConfigurator> _configurators = new List<OptionConfigurator>();

		/// <summary>
		/// (specter) The tabs LB/RB cycle between (Profile/Color/Trails) - Screen and Monitor are
		/// excluded, both handled elsewhere (Screen: always-on cursor; Monitor: LB/RB display-move
		/// shortcut).
		/// </summary>
		private List<OptionConfigurator> _cyclable = new List<OptionConfigurator>();

		/// <summary>
		/// (specter) The tabs shown in the row's tab bar - same set as <see cref="_cyclable"/> for
		/// both input devices.
		/// </summary>
		private List<OptionConfigurator> ActiveList => _cyclable;

		/// <summary>
		/// (specter) Index of the currently selected configurator within ActiveList.
		/// </summary>
		private int _configuratorIndex;

		/// <summary>
		/// (specter) Whether the options panel is currently open (a configurator is active).
		/// </summary>
		public bool IsExpanded { get; private set; }

		/// <summary>
		/// (specter) The 3 pre-built tab slots in userslot.prefab (Tab0/Tab1/Tab2), mapped 1:1 to _cyclable.
		/// </summary>
		private Transform[] _tabNodes;

		/// <summary>
		/// (specter) Coverflow tab bar: all 3 tabs stay visible, one Centered and the other two
		/// shrunk/dimmed to Left/Right. Plain per-frame Lerp, not UIJuice - its internals are
		/// stripped in the stub assembly this mod compiles against.
		/// </summary>
		private enum TabRole { Center, Left, Right }
		private TabRole[] _tabRoles;
		private CanvasGroup[] _tabCanvasGroups;

		private const float CarouselSideOffset = 85f;
		private const float CarouselSideScale = 0.62f;
		private const float CarouselSideAlpha = 0.45f;

		/// <summary>
		/// (specter) Each tab's content panel (ProfileContent/ColorContent/TrailsContent), keyed
		/// by configurator type since panel shape differs per type.
		/// </summary>
		private Dictionary<System.Type, Transform> _contentPanels;

		/// <summary>
		/// (specter) The screen-region cursor - not one of the tabs, opened/closed with this
		/// component's own enabled state and ticked unconditionally in Update().
		/// </summary>
		private AssignmentConfigurator _assignmentConfigurator;

		#endregion

		#region Unity Lifecycle

		/// <summary>
		/// Initialize the options UI when the component awakens.
		/// </summary>
		public void Awake()
		{
			Slot = GetComponentInParent<LocalUserSlot>();

			CreateOptions();
			SetupRedesignedUI();
			SubscribeToSlot();
		}

		/// <summary>
		/// (specter) Finds the tab bar's 3 pre-built tab slots and each configurator's content
		/// panel in the redesigned userslot.prefab.
		/// </summary>
		private void SetupRedesignedUI()
		{
			var expandedContent = Slot.transform.Find("ExpandedContent");
			var tabBar = expandedContent.Find("TabBar");
			var tabsContainer = tabBar.Find("TabsContainer");

			_tabNodes = new[]
			{
				tabsContainer.Find("Tab0"),
				tabsContainer.Find("Tab1"),
				tabsContainer.Find("Tab2"),
			};

			// (specter) Pulled out of TabsContainer's layout flow and positioned by hand per TabRole.
			_tabRoles = new TabRole[_tabNodes.Length];
			_tabCanvasGroups = new CanvasGroup[_tabNodes.Length];
			for (int i = 0; i < _tabNodes.Length; i++)
			{
				_tabNodes[i].gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
				_tabCanvasGroups[i] = _tabNodes[i].gameObject.AddComponent<CanvasGroup>();
			}
			// (specter) Snap to starting roles immediately - _configuratorIndex is 0 before the
			// first Open(), so index 0 starts Center, the last tab starts Left, the rest Right.
			for (int i = 0; i < _tabNodes.Length; i++)
			{
				_tabRoles[i] = i == 0 ? TabRole.Center : i == _tabNodes.Length - 1 ? TabRole.Left : TabRole.Right;
				SnapTabToRole(i);
			}

			_contentPanels = new Dictionary<System.Type, Transform>
			{
				{ typeof(ProfileConfigurator), expandedContent.Find("ProfileContent") },
				{ typeof(ColorConfigurator), expandedContent.Find("ColorContent") },
				{ typeof(TrailsConfigurator), expandedContent.Find("TrailsContent") },
			};

			// (specter) LB/RB hint text is baked grey (#8a8d96) into the prefab and never
			// re-colored at runtime elsewhere - override it here to match the white tab text.
			foreach (var shoulder in new[] { "LBButton", "RBButton" })
			{
				var button = tabBar.Find(shoulder);
				if (button == null) continue;
				foreach (var tmp in button.GetComponentsInChildren<TextMeshProUGUI>(true))
					tmp.color = Color.white;
			}
		}

		/// <summary>
		/// (specter) Opens the screen-region cursor as soon as this slot becomes occupied.
		/// </summary>
		public void OnEnable()
		{
			_assignmentConfigurator?.Open();
		}

		/// <summary>
		/// Clean up when the options are disabled.
		/// </summary>
		public void OnDisable()
		{
			ForceClose();
			_assignmentConfigurator?.ForceClose();
		}

		/// <summary>
		/// Handle input and updates for the options UI.
		/// </summary>
		public void Update()
		{
			if (!LocalUserPanel.AllowChanges)
				return;

			_assignmentConfigurator?.ConfiguratorUpdate();

			UpdateInputFlow();
			AnimateTabCarousel();
		}

		/// <summary>
		/// (specter) North opens/closes the panel; LB/RB switch tabs while open; the active tab
		/// reads its own axis and confirms via South. Keyboard maps Tab/Q/E/Enter/Escape to the same.
		/// </summary>
		private void UpdateInputFlow()
		{
			// (specter) Works regardless of panel state - panels now commonly stay open, so this is
			// the only way to hold-to-remove a player.
			HandleHoldToRemove();

			if (Slot.Input.North)
			{
				if (IsExpanded)
					CloseConfigurator();
				else
					OpenConfigurator();
			}

			if (!IsExpanded)
				return;

			if (_cyclable.Count == 0)
				return;

			if (Slot.Input.LB)
				SwitchCyclableTab(-1);
			else if (Slot.Input.RB)
				SwitchCyclableTab(1);

			_cyclable[_configuratorIndex].ConfiguratorUpdate();

			if (Slot.Input.Up)
				_cyclable[_configuratorIndex].OnNavigate(-1);
			else if (Slot.Input.Down)
				_cyclable[_configuratorIndex].OnNavigate(1);

			DisplayOptionName(); // (specter) keeps tab/panel state in sync every frame, not just on switch
		}

		/// <summary>
		/// (specter) Hold East (B) to remove this player - now works regardless of whether the
		/// panel is open or closed (see the comment in UpdateInputFlow above).
		/// </summary>
		private void HandleHoldToRemove()
		{
			if (Slot.LocalPlayer != null && Slot.LocalPlayer.GetButton(15))
			{
				Slot.LocalPlayer.SetVibration(0, Slot.LocalPlayer.GetVibration(0) + (Time.deltaTime * 10f), true);
				if (Slot.LocalPlayer.GetButtonTimedPressDown(15, 0.5f))
					Slot.TryRemoveSlot();
			}
		}

		#endregion

		#region Configurator Methods

		public void ForceClose()
		{
			var list = ActiveList;
			if (IsExpanded && list.Count > 0)
				list[_configuratorIndex].ForceClose();

			CleanupConfigurator();
		}

		#endregion

		#region UI Setup

		/// <summary>
		/// Creates all available option configurators.
		/// </summary>
		private void CreateOptions()
		{
			var optionConfiguratorType = typeof(OptionConfigurator);

			// Find all non-abstract classes that inherit from OptionConfigurator
			foreach (var type in Assembly.GetExecutingAssembly().GetTypes().Where(x =>
					optionConfiguratorType.IsAssignableFrom(x) && !x.IsAbstract))
			{
				OptionConfigurator configurator = (OptionConfigurator) new GameObject(type.Name).AddComponent(type);
				configurator.gameObject.AddComponent<RectTransform>();
				// (specter) These are logic-only components parented onto CollapsedRow (since
				// SlotOptions lives there) - without this, each one (5 of them) still counts as an
				// active sibling in its HorizontalLayoutGroup, adding a 10px spacing gap apiece with
				// zero visual width to show for it. Confirmed via diagnostic: ~50px of dead space
				// sitting after the Y-toggle glyph, dwarfing any padding/spacer tweak.
				configurator.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
				_configurators.Add(configurator);

				// (specter) AssignmentConfigurator has its own lifecycle - it must not share OnFinished, or
				// cancelling its cursor (East) would also collapse whatever tab is open.
				if (configurator is not AssignmentConfigurator)
					configurator.OnFinished += OnFinished;

				configurator.Options = this;
				configurator.transform.SetParent(transform);
				configurator.transform.localPosition = Vector3.zero;
				configurator.gameObject.SetActive(true);
				configurator.name = type.Name;
			}

			// Sort configurators by priority
			_configurators = _configurators.OrderBy(x => x.GetPriority()).ToList();

			_assignmentConfigurator = _configurators.OfType<AssignmentConfigurator>().FirstOrDefault();

			_cyclable = _configurators
				.Where(x => x is not DisplayConfigurator && x is not AssignmentConfigurator)
				.ToList();
		}

		#endregion

		#region Navigation Methods

		private void SubscribeToSlot()
		{
			Slot.OnCancel += OnCancel;
			Slot.OnLoadProfile += OnLoadProfile;
			Slot.OnUnloadProfile += OnUnloadProfile;
		}

		/// <summary>
		/// (specter) Selects the Profile tab as default for a newly-occupied slot, without opening
		/// the panel.
		/// </summary>
		internal void OpenProfileConfigurator()
		{
			var list = ActiveList;
			if (list.Count == 0) return;

			int idx = list.FindIndex(x => x is ProfileConfigurator);
			_configuratorIndex = idx >= 0 ? idx : 0;

			DisplayOptionName();
		}

		/// <summary>
		/// (specter) Switches the active tab within the gamepad flow's cyclable list, wrapping
		/// around. Does not touch IsExpanded - the panel stays open across a tab switch.
		/// </summary>
		private void SwitchCyclableTab(int direction)
		{
			_cyclable[_configuratorIndex].ForceClose();

			_configuratorIndex = ((_configuratorIndex + direction) % _cyclable.Count + _cyclable.Count) % _cyclable.Count;

			_cyclable[_configuratorIndex].Open();
			DisplayOptionName();
		}

		private void OnUnloadProfile()
		{
			foreach (var config in _configurators)
				config.OnUnloadProfile();
		}

		private void OnLoadProfile()
		{
			foreach (var config in _configurators)
				config.OnLoadProfile();
		}

		/// <summary>
		/// Handles cancellation input from UI button.
		/// </summary>
		private void OnCancel()
		{
			if (IsExpanded)
				ActiveList[_configuratorIndex].OnCancel();
		}

		/// <summary>
		/// Opens the currently selected configurator.
		/// </summary>
		internal void OpenConfigurator()
		{
			var list = ActiveList;
			if (IsExpanded || list.Count == 0 || !list[_configuratorIndex].CanOpen())
				return;

			IsExpanded = true;
			list[_configuratorIndex].Open();
			DisplayOptionName();
		}

		/// <summary>
		/// Closes the currently active configurator.
		/// </summary>
		internal void CloseConfigurator()
		{
			var list = ActiveList;
			if (!IsExpanded || list.Count == 0)
				return;

			list[_configuratorIndex].ForceClose();

			CleanupConfigurator();
		}

		/// <summary>
		/// Called when a configurator finishes its operation.
		/// </summary>
		internal void OnFinished()
		{
			CleanupConfigurator();
		}

		private void CleanupConfigurator()
		{
			IsExpanded = false;

			DisplayOptionName();
		}
		#endregion

		#region UI Methods

		// (specter) Read live off SplitscreenMenuController - not cached, since these tabs can
		// render before its own HGButton color-sampling runs.
		private static Color TabTextColor => SplitscreenMenuController.RoR2TextColor;
		private static Color TabMutedColor => SplitscreenMenuController.RoR2MutedColor;
		private static readonly Color Transparent = new Color(0, 0, 0, 0);

		/// <summary>
		/// (specter) Assigns which tab currently holds Center/Left/Right. AnimateTabCarousel (ticked
		/// every frame) handles the actual motion, so a role change here just animates on its own.
		/// </summary>
		private void DisplayOptionName()
		{
			var list = ActiveList;
			if (list.Count == 0) return;

			int centerIndex = _configuratorIndex;
			int leftIndex = (centerIndex - 1 + list.Count) % list.Count;
			int rightIndex = (centerIndex + 1) % list.Count;

			for (int i = 0; i < _tabNodes.Length; i++)
			{
				bool inRange = i < list.Count;
				_tabNodes[i].gameObject.SetActive(inRange);
				if (!inRange) continue;

				var configurator = list[i];
				bool canOpen = configurator.CanOpen();
				bool isCenter = i == centerIndex;

				var label = _tabNodes[i].Find("LabelRow/Label").GetComponent<TextMeshProUGUI>();
				var check = _tabNodes[i].Find("LabelRow/Check").gameObject;
				var underline = _tabNodes[i].Find("Underline").GetComponent<Image>();

				label.text = ResolveToken(configurator.GetName());
				check.SetActive(false); // (specter) tab checkmark deemed unneeded, always hidden
				label.color = isCenter
					? (!canOpen ? TabMutedColor : TabTextColor)
					: new Color(TabMutedColor.r, TabMutedColor.g, TabMutedColor.b, CarouselSideAlpha);
				underline.color = isCenter ? Color.white : Transparent;

				_tabRoles[i] = isCenter ? TabRole.Center : i == leftIndex ? TabRole.Left : TabRole.Right;
				// (specter) Center tab draws on top of the side tabs as they pass behind it mid-rotation.
				if (isCenter)
					_tabNodes[i].SetAsLastSibling();
			}

			var active = list[_configuratorIndex];
			foreach (var pair in _contentPanels)
				pair.Value.gameObject.SetActive(IsExpanded && pair.Key == active.GetType());
		}

		/// <summary>
		/// (specter) Eases each tab's position/scale/alpha toward its current role's target every
		/// frame, so it keeps smoothing even across multiple quick LB/RB presses.
		/// </summary>
		private void AnimateTabCarousel()
		{
			if (_tabRoles == null) return;
			float t = Mathf.Clamp01(Time.unscaledDeltaTime * 10f);

			for (int i = 0; i < _tabNodes.Length; i++)
			{
				if (!_tabNodes[i].gameObject.activeSelf) continue;

				var rect = (RectTransform) _tabNodes[i];
				Vector2 targetPos = TargetPosForRole(_tabRoles[i]);
				float targetScale = TargetScaleForRole(_tabRoles[i]);
				float targetAlpha = TargetAlphaForRole(_tabRoles[i]);

				rect.anchoredPosition = Vector2.Lerp(rect.anchoredPosition, targetPos, t);
				rect.localScale = Vector3.one * Mathf.Lerp(rect.localScale.x, targetScale, t);
				_tabCanvasGroups[i].alpha = Mathf.Lerp(_tabCanvasGroups[i].alpha, targetAlpha, t);
			}
		}

		/// <summary>
		/// (specter) Snaps a tab directly to its current role with no easing - used once at setup so
		/// tabs don't visibly animate in from a default (0,0,1,1) state before the first real switch.
		/// </summary>
		private void SnapTabToRole(int i)
		{
			var rect = (RectTransform) _tabNodes[i];
			rect.anchoredPosition = TargetPosForRole(_tabRoles[i]);
			rect.localScale = Vector3.one * TargetScaleForRole(_tabRoles[i]);
			_tabCanvasGroups[i].alpha = TargetAlphaForRole(_tabRoles[i]);
		}

		private static Vector2 TargetPosForRole(TabRole role) => role switch
		{
			TabRole.Center => Vector2.zero,
			TabRole.Left => new Vector2(-CarouselSideOffset, 0f),
			_ => new Vector2(CarouselSideOffset, 0f),
		};

		private static float TargetScaleForRole(TabRole role) => role == TabRole.Center ? 1f : CarouselSideScale;

		private static float TargetAlphaForRole(TabRole role) => role == TabRole.Center ? 1f : CarouselSideAlpha;

		/// <summary>
		/// (specter) Resolves a RoR2 localization token to its display string, or returns it as-is.
		/// </summary>
		private static string ResolveToken(string token) =>
			token != null && RoR2.Language.currentLanguage.TokenIsRegistered(token) ? RoR2.Language.GetString(token) : token;

		public void SetMessage(string token) => SetMessage(token, TabTextColor);

		/// <summary>
		/// (specter) Writes a value into the active tab's ValueText (ColorContent has none, so a
		/// no-op there).
		/// </summary>
		public void SetMessage(string token, Color color)
		{
			var list = ActiveList;
			if (list.Count == 0) return;
			if (!_contentPanels.TryGetValue(list[_configuratorIndex].GetType(), out var panel)) return;

			// (specter) ValueText used to sit under a nested "ValueRow" child (part of the old
			// 3-tier vertical panel shape) - it's a direct child now that panel is one row.
			var valueTextTransform = panel.Find("ValueText");
			if (valueTextTransform == null) return;

			var valueText = valueTextTransform.GetComponent<TextMeshProUGUI>();
			valueText.text = token == null ? "" : ResolveToken(token);
			valueText.color = color;
			// (specter) No size floor means a long value forces siblings (glyphs, accent bar) to
			// overflow instead. Let it shrink first.
			valueText.enableAutoSizing = true;
			valueText.fontSizeMin = 12;
			valueText.fontSizeMax = 19;
			// (specter) Baked prefab LayoutElement has minWidth=140, a rigid floor same as the
			// NameText issue above - lower it so the box can actually shrink under pressure.
			var valueLayout = valueTextTransform.GetComponent<LayoutElement>();
			if (valueLayout != null)
				valueLayout.minWidth = 50;

			LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform) panel);
		}

		#endregion

		#region Public Methods

		public T GetConfigurator<T>() where T : OptionConfigurator
		{
			foreach (var c in _configurators)
				if (c is T b)
					return b;

			return default;
		}

		/// <summary>
		/// (specter) YToggle's onClick - mirrors the gamepad's North-press open/close toggle.
		/// </summary>
		public void ToggleExpanded()
		{
			if (IsExpanded)
				CloseConfigurator();
			else
				OpenConfigurator();
		}

		/// <summary>
		/// (specter) TabBar's LB/RB onClick - mirrors the gamepad's shoulder-button tab switch.
		/// </summary>
		public void ClickShoulder(int direction)
		{
			if (!IsExpanded || _cyclable.Count == 0)
				return;

			SwitchCyclableTab(direction);
		}

		/// <summary>
		/// (specter) Each panel's A-button onClick - mirrors the gamepad's South-press confirm.
		/// </summary>
		public void ClickConfirm()
		{
			if (IsExpanded && ActiveList.Count > 0)
				ActiveList[_configuratorIndex].OnConfirm();
		}

		#endregion
	}
}
