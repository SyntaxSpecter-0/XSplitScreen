using Rewired;
using RoR2;
using RoR2.UI;
using RoR2.UI.MainMenu;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MonoMod.Cil;

namespace Dodad.XSplitscreen.Components
{
	public class SplitscreenMenuController : BaseMainMenuScreen
	{
		public static SplitscreenMenuController Singleton { get; private set; }

		internal static GameObject TextPrefab { get; private set; }
		internal static UILayerKey UiLayerKey { get; private set; }
		internal static LocalUserSlot.InputBank MainInput
		{
			get => mainInput;
		}
		public static float LoadTimer
		{
			get => loadTimer;
		}
		public static bool ReadyToLoad
		{
			get => AllowLoad;
		}

		public CarouselController gameModePicker;

		// Buttons and player input references
		private static HGButton discordButton;
		private static HGButton multiMonitorButton;
		private static HGButton backButton;
		private static HGButton creditsButton;
		private static HGButton gameModeButton;
		private static Player mainPlayer;
		private static LocalUserSlot.InputBank mainInput;
		private static CanvasGroup notificationGroup;


		internal int monitorId;

		public Canvas MenuCanvas => _canvas;
		public Camera MenuCamera => _camera;
		private bool hasInitialized;

		private int framesSinceMultiMonitorEnabled = 3; // Wait 2 frames to invoke Juiced event

		private Canvas _canvas;
		private Camera _camera;

		private static float loadTimer;
		private static bool AllowLoad;
		private static GameObject countdownBanner;
		private static TMPro.TextMeshProUGUI countdownText;

		/// <summary>
		/// (specter) RoR2's own UI palette. Colors are hardcoded (color-picked off the real game),
		/// not sampled at runtime - a real HGButton's Image.color is just a neutral white tint, its
		/// visible color comes from the sprite texture, so there's nothing useful to read there.
		/// Font is still sampled live, since that does read correctly.
		/// </summary>
		public static Color RoR2ButtonColor { get; private set; } = new Color(0.259f, 0.322f, 0.380f); // #425261
		public static Color RoR2TextColor { get; private set; } = Color.white; // #ffffff
		public static Color RoR2AccentColor { get; private set; } = new Color(0.902f, 0.271f, 0.243f); // #e6453e
		public static Color RoR2MutedColor { get; private set; } = new Color(0.541f, 0.553f, 0.588f); // #8a8d96
		public static TMPro.TMP_FontAsset RoR2Font { get; private set; }
		public static Material RoR2FontMaterial { get; private set; }

		private static void SampleRoR2Colors(HGButton sourceButton)
		{
			// (specter) Only the font is sampled live - colors are the hardcoded values above.
			var text = sourceButton.GetComponentInChildren<TMPro.TextMeshProUGUI>();
			if (text != null)
			{
				RoR2Font = text.font;
				RoR2FontMaterial = text.fontSharedMaterial;
			}
		}
		/// <summary>
		/// (specter) Main Panel is full-screen (its background needs to be, to cover edge-to-edge
		/// like the real game's own menu backdrop), so the Credits/Discord/Back button clones -
		/// positioned with raw pixel offsets relative to it, not anchor percentages - need their
		/// own margin instead of inheriting one from Main Panel like they used to.
		/// </summary>
		private const float ScreenEdgeMargin = 60f;

		private static bool ShowBackupWarning = true;
		private static float BackupWarningTimer = 20f;
		private const float BackupTimeout = 20f;

		//-----------------------------------------------------------------------------------------------------------

		public new void Awake()
		{
			Log.Print($"[{GetType().Name}.{MethodBase.GetCurrentMethod().Name}] : Alive");

			PrintDebugInfo();

			if (Singleton == null)
			{
				Singleton = this;
				RoR2Application.isInLocalMultiPlayer = SplitscreenUserManager.IsSplitscreenEnabled;
			}
			else
			{
				Singleton.onEnter.AddListener(OnEnterHandler);
			}

			onEnter = new UnityEngine.Events.UnityEvent();
			onExit = new UnityEngine.Events.UnityEvent();

			_canvas = GetComponent<Canvas>();

			var baseType = typeof(BaseMainMenuScreen);
			var fsoType = baseType.Assembly.GetType("FirstSelectedObjectProvider");
			var fsoField = baseType.GetField("firstSelectedObjectProvider", BindingFlags.NonPublic | BindingFlags.Instance);
			var fsoComponent = gameObject.AddComponent(fsoType);
			fsoField.SetValue(this, fsoComponent);

			desiredCameraTransform = transform.parent.Find("World Position").transform;

			var eventProvider = gameObject.AddComponent<MPEventSystemProvider>();
			eventProvider.fallBackToMainEventSystem = true;

			if (monitorId == 0)
			{
				mainPlayer = LocalUserManager.GetRewiredMainPlayer();
				mainInput = new LocalUserSlot.InputBank();
				_camera = Camera.main;
			}

			gameObject.SetActive(false);
		}

		//-----------------------------------------------------------------------------------------------------------

		public void LateUpdate()
		{
			if (monitorId != 0) return;
			framesSinceMultiMonitorEnabled++;
		}

		public new void OnEnable()
		{
			base.OnEnable();

			if (monitorId != 0)
				return;
		}

		//-----------------------------------------------------------------------------------------------------------

		public new void Update()
		{
			base.Update();

			if (monitorId != 0) return;

			mainInput.Update(mainPlayer);

			HandleNavigationInput();
			HandleMenuExit();
			HandleLoadGame();
			HandleNotificationUpdate();

			if (framesSinceMultiMonitorEnabled == 2)
				Singleton.onEnter.Invoke();
		}

		public void OnDestroy()
		{
			if(monitorId == 0)
				AssignmentConfigurator.OnClaimUpdated -= OnClaimUpdated;
		}
		
		private void HandleNotificationUpdate()
		{
			if (notificationGroup == null)
				return;

			float groupAlphaTarget = 1;

			if(ShowBackupWarning)
			{
				BackupWarningTimer -= Time.unscaledDeltaTime;

				if (Mathf.Abs(BackupTimeout - BackupWarningTimer) >= 0.4f)
				{
					if (mainInput.MouseLeft || mainInput.South || BackupWarningTimer <= 0)
					{
						ShowBackupWarning = false;
						groupAlphaTarget = 0f;
						LocalUserPanel.AllowChanges = true;
					}
				}
			}
			else
			{
				groupAlphaTarget = 0f;
			}

			if(Mathf.Abs(notificationGroup.alpha - groupAlphaTarget) > 0.01f)
				notificationGroup.alpha = Mathf.MoveTowards(notificationGroup.alpha, groupAlphaTarget, Time.unscaledDeltaTime * 10f);
			else
			{
				if(notificationGroup.alpha < 1)
					Destroy(notificationGroup.gameObject);
			}
		}

		private void HandleNavigationInput()
		{
			if (ShowBackupWarning)
				return;

			var last = mainPlayer.controllers.GetLastActiveController();

			if (last is Keyboard || last is Mouse) return;

			if (mainInput.Down || mainInput.Up)
			{
				EnsureSelectedObject();
			}
		}

		private void HandleMenuExit()
		{
			if (mainPlayer.controllers.GetLastActiveController() is Keyboard || CreditsController.ShowCredits || ShowBackupWarning) 
				return;

			if (mainInput.East)
			{
				MainMenuController.instance.SetDesiredMenuScreen(MainMenuController.instance.titleMenuScreen);
			}
		}

		private void EnsureSelectedObject()
		{
			if (CreditsController.ShowCredits)
				return;

			if (EventSystem.current.currentSelectedGameObject == null)
				EventSystem.current.SetSelectedGameObject(discordButton.gameObject);

			/*if (EventSystem.current.currentSelectedGameObject != null) return;

			EventSystem.current.SetSelectedGameObject(creditsButton.gameObject);*/
			/*if (multiMonitorButton.interactable)
				EventSystem.current.SetSelectedGameObject(multiMonitorButton.gameObject);
			else
				EventSystem.current.SetSelectedGameObject(discordButton.gameObject);*/
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Called when the player clicks on the Multi-Monitor button
		/// </summary>
		public void EnableMultiMonitorMode()
		{
			Log.Print($"EnableMultiMonitorMode: '{Display.displays.Length}' displays");

			int displayCount = Display.displays.Length;
			if (displayCount == 1) return;

			// (specter) No active camera rig exists outside a run - bail instead of crashing on
			// an empty list.
			if (CameraRigController.instancesList.Count == 0)
			{
				Log.Print("EnableMultiMonitorMode: no active CameraRigController to clone from yet, skipping", Log.ELogChannel.Warning);
				return;
			}

			var mainCamera = CameraRigController.instancesList.First();

			for (int i = 1; i < displayCount; i++)
			{
				if (!Display.displays[i].active)
					Display.displays[i].Activate();

				var newMenu = Plugin.CreateMenuForDisplay(i);

				var camera = Instantiate(mainCamera);
				camera.name = $"[Display {i}] Camera";
				camera.sceneCam.targetDisplay = i;
				camera.sceneCam.transform.position = Singleton.desiredCameraTransform.position;
				camera.sceneCam.transform.rotation = Singleton.desiredCameraTransform.rotation;

				newMenu._camera = camera.sceneCam;
			}

			EventSystem.current.SetSelectedGameObject(null);
			framesSinceMultiMonitorEnabled = 0;
		}

		//-----------------------------------------------------------------------------------------------------------

		private void OnEnterHandler()
		{
			OnEnter(Singleton.myMainMenuController);
		}

		public override void OnEnter(MainMenuController mainMenuController)
		{
			if (monitorId == 0)
			{
				SplitscreenUserManager.DisableSplitscreen();
			}

			if (!hasInitialized)
				CreateUI();

			if (monitorId != 0)
			{
				gameObject.SetActive(true);

				return;
			}

			myMainMenuController = mainMenuController;

			if (SimpleDialogBox.instancesList.Count == 0)
				firstSelectedObjectProvider?.EnsureSelectedObject();

			onEnter.Invoke();

			if (discordButton == null)
			{
				Log.Print("Unable to create Splitscreen menu. Please post the log in the Discord server: https://discord.gg/maHhJSv62G", Log.ELogChannel.Error);
				MainMenuController.instance.SetDesiredMenuScreen(MainMenuController.instance.titleMenuScreen);
			}
		}

		//-----------------------------------------------------------------------------------------------------------

		public override void OnExit(MainMenuController mainMenuController)
		{
			if (monitorId != 0)
			{
				gameObject.SetActive(false);

				return;
			}

			if (myMainMenuController == mainMenuController)
				myMainMenuController = null;

			ReleaseAllControllersToMain();

			onExit.Invoke();

			SplitScreenSettings.BatchSaveDirtyUsers();
		}

		/// <summary>
		/// (specter) Returns every slot's controllers to the real Rewired main player before
		/// leaving this menu - otherwise a controller stays stuck on a splitscreen slot after
		/// backing out, since the main menu only reads "PlayerMain".
		/// </summary>
		private static void ReleaseAllControllersToMain()
		{
			if (LocalUserSlot.Instances == null) return;

			foreach (var slot in LocalUserSlot.Instances.ToList())
			{
				if (slot != null && slot.LocalPlayer != null && slot.Panel != null)
					slot.Panel.TryRemovePlayerFromSlot(slot.LocalPlayer, slot);
			}
		}

		//-----------------------------------------------------------------------------------------------------------
		private void BuildGameModeChoices()
		{
            List<CarouselController.Choice> list = new List<CarouselController.Choice>();
            List<string> list2 = gameModePicker.choices.Select((CarouselController.Choice choice) => choice.suboptionDisplayToken).ToList();
            for (GameModeIndex gameModeIndex = (GameModeIndex)0; (int)gameModeIndex < GameModeCatalog.gameModeCount; gameModeIndex++)
            {
                Run gameModePrefabComponent = GameModeCatalog.GetGameModePrefabComponent(gameModeIndex);
                RoR2.ExpansionManagement.ExpansionRequirementComponent component = gameModePrefabComponent.GetComponent<RoR2.ExpansionManagement.ExpansionRequirementComponent>();
                if (gameModePrefabComponent != null && gameModePrefabComponent.userPickable && (!component || !component.requiredExpansion || RoR2.EntitlementManagement.EntitlementManager.localUserEntitlementTracker.AnyUserHasEntitlement(component.requiredExpansion.requiredEntitlement)))
                {
                    list.Add(new CarouselController.Choice
                    {
                        suboptionDisplayToken = gameModePrefabComponent.nameToken,
                        convarValue = gameModePrefabComponent.name
                    });
                }
            }
            gameModePicker.choices = list.ToArray();
            gameModePicker.gameObject.SetActive(list.Count > 1);
            string text = Console.instance.FindConVar("gamemode").GetString();
            bool flag = false;
            for (int num = 0; num < list.Count; num++)
            {
                if (list[num].convarValue == text)
                {
                    flag = true;
                    break;
                }
            }
            if (list.Count == 1 || !flag)
            {
                Debug.LogFormat("Invalid gamemode {0} detected. Reverting to ClassicRun.", text);
                gameModePicker.SubmitSetting(list[0].convarValue);
            }
        }

		private void CreateUI()
		{
			var mainPanel = transform.Find("Main Panel");
			var localUserPanel = mainPanel.Find("User Panel");
			localUserPanel.gameObject.AddComponent<LocalUserPanel>().Initialize(this, monitorId);

			var assignmentPanel = mainPanel.Find("Assignment Panel");
			assignmentPanel.gameObject.AddComponent<AssignmentPanel>().Initialize(this);

			hasInitialized = true;

			notificationGroup = transform.Find("Notification Panel").GetComponent<CanvasGroup>();

			if (monitorId != 0)
			{
				Destroy(notificationGroup.gameObject);
				Log.Print($"[SplitscreenMenuController::CreateUI] Returning with 'monitorId' = '{monitorId}'");
				return;
			}

			var backPanelTemplate = MainMenuController.instance.extraGameModeMenuScreen.transform.Find("Main Panel/BackPanel");
			var menuButtonPanelTemplate = MainMenuController.instance.extraGameModeMenuScreen.transform.Find("Main Panel/GenericMenuButtonPanel");


			if (backPanelTemplate == null || menuButtonPanelTemplate == null)
			{
				return;
			}

			var backPanelClone = Instantiate(backPanelTemplate.gameObject);
			var backPanelRect = backPanelClone.GetComponent<RectTransform>();
			backPanelRect.SetParent(mainPanel);
			backPanelRect.offsetMax = new Vector2(700, 0);
			backPanelRect.offsetMin = new Vector2(ScreenEdgeMargin, ScreenEdgeMargin);
			backPanelRect.transform.localScale = Vector3.one;

			// Back button setup
			backButton = backPanelClone.transform.Find("ButtonPanel (JUICED)/Button, Return").GetComponent<HGButton>();

			var backHoverToken = backButton.hoverToken;
			UIHelper.ClearHGButton(backButton, false);

			backButton.hoverToken = backHoverToken;
			backButton.onClick.AddListener(() =>
			{
				MainMenuController.instance.SetDesiredMenuScreen(MainMenuController.instance.titleMenuScreen);
				backButton.OnClickCustom();
			});

			Destroy(backButton.GetComponent<DisableIfNoExpansion>());
			backButton.GetComponent<MPEventSystemLocator>().Awake();

			var backJuice = backButton.transform.parent.GetComponent<UIJuice>();
			onEnter.AddListener(() =>
			{
				backJuice.TransitionAlphaFadeIn();
				backJuice.TransitionPanFromLeft();
			});

			// Menu panel setup
			var menuButtonPanelClone = Instantiate(menuButtonPanelTemplate.gameObject);
			var menuButtonPanelRect = menuButtonPanelClone.GetComponent<RectTransform>();
			menuButtonPanelRect.SetParent(mainPanel);
			menuButtonPanelRect.offsetMax = Vector2.zero;
			// (specter) Bottom margin needs enough clearance for the hover-description text (which
			// sits at the bottom of this panel) to clear the Back panel below it (which only
			// insets 60px from the screen bottom) - 160 wasn't enough and the two visually overlapped.
			menuButtonPanelRect.offsetMin = new Vector2(ScreenEdgeMargin, 260);
			menuButtonPanelRect.transform.localScale = Vector3.one;

			// Discord button setup
			discordButton = menuButtonPanelRect.Find("JuicePanel/GenericMenuButton (Infinite Tower)").GetComponent<HGButton>();
			SampleRoR2Colors(discordButton); // (specter) before ClearHGButton touches anything else on it
			discordButton.name = "Discord";
			UIHelper.ClearHGButton(discordButton);
			discordButton.GetComponentInChildren<LanguageTextMeshController>().token = "XSS_OPTION_DISCORD";
			discordButton.hoverLanguageTextMeshController = menuButtonPanelRect.Find("JuicePanel/DescriptionPanel, Naked/ContentSizeFitter/DescriptionText").GetComponent<LanguageTextMeshController>();
			discordButton.hoverToken = "XSS_OPTION_DISCORD_HOVER";
			discordButton.updateTextOnHover = true;
			discordButton.GetComponent<MPEventSystemLocator>().Awake();
			discordButton.onClick.AddListener(() =>
			{
				Application.OpenURL("https://discord.gg/maHhJSv62G");
			});
			discordButton.gameObject.SetActive(true);

			// Remove extra buttons
			foreach (Transform child in menuButtonPanelRect.Find("JuicePanel"))
			{
				if(child.name == "Discord" || child.name == "DescriptionPanel, Naked")
					continue;
				Log.Print($"Destroying extra button: {child.name}", Log.ELogChannel.Debug);
				Destroy(child.gameObject);
			}

			// Gamemodes
			var gameModeButtonTemplate = MainMenuController.instance.multiplayerMenuScreen.transform.Find("Inner90/MainMultiplayerMenu/GenericMenuButtonPanel/JuicePanel/GameMode");
			gameModeButton = Instantiate((gameModeButtonTemplate.gameObject)).GetComponent<HGButton>();
			gameModeButton.hoverLanguageTextMeshController = menuButtonPanelRect.Find("JuicePanel/DescriptionPanel, Naked/ContentSizeFitter/DescriptionText").GetComponent<LanguageTextMeshController>();
			gameModeButton.transform.SetParent(discordButton.transform.parent);
			gameModeButton.transform.localScale = Vector3.one;
			gameModeButton.transform.SetSiblingIndex(0);
			gameModeButton.name = "GameMode";
			gameModeButton.hoverToken = "XSS_OPTION_GM_HOVER";

			gameModePicker = gameModeButton.GetComponent<CarouselController>();
			BuildGameModeChoices();

			gameModeButton.GetComponent<MPEventSystemLocator>().Awake();
			Destroy(gameModeButton.transform.Find("Canvas").gameObject);
			
			// Multi monitor
			multiMonitorButton = Instantiate(discordButton.gameObject).GetComponent<HGButton>();
			multiMonitorButton.transform.SetParent(discordButton.transform.parent);
			multiMonitorButton.transform.localScale = Vector3.one;
			multiMonitorButton.transform.SetSiblingIndex(0);
			multiMonitorButton.GetComponentInChildren<LanguageTextMeshController>().token = "XSS_OPTION_MMM";
			multiMonitorButton.name = "Multi Monitor Mode";
			multiMonitorButton.hoverToken = "XSS_OPTION_MMM_HOVER";
			multiMonitorButton.GetComponent<MPEventSystemLocator>().Awake();
			multiMonitorButton.onClick.RemoveAllListeners();
			multiMonitorButton.onClick.AddListener(() =>
			{
				EnableMultiMonitorMode();
				multiMonitorButton.interactable = false;
			});
			multiMonitorButton.gameObject.SetActive(true);

			int displayCount = Display.displays.Length;
			multiMonitorButton.interactable = displayCount != 1 && !Enumerable.Range(1, displayCount - 1).Any(i => Display.displays[i].active);

			if (!multiMonitorButton.interactable && displayCount != 1)
			{
				EnableMultiMonitorMode();
			}

			// Credits button setup
			creditsButton = Instantiate(discordButton.gameObject).GetComponent<HGButton>();
			creditsButton.transform.SetParent(discordButton.transform.parent);
			creditsButton.transform.localScale = Vector3.one;
			creditsButton.transform.SetSiblingIndex(0);
			creditsButton.GetComponentInChildren<LanguageTextMeshController>().token = "XSS_CREDITS";
			var creditsText = creditsButton.GetComponentInChildren<TMPro.TextMeshProUGUI>();
			if (creditsText != null)
			{
				creditsText.horizontalAlignment = TMPro.HorizontalAlignmentOptions.Center;
				creditsText.verticalAlignment = TMPro.VerticalAlignmentOptions.Middle;
			}
			creditsButton.name = "Credits";
			creditsButton.hoverToken = "XSS_CREDITS_HOVER";
			creditsButton.GetComponent<MPEventSystemLocator>().Awake();
			creditsButton.onClick.RemoveAllListeners();
			creditsButton.onClick.AddListener(() =>
			{
				if (!CreditsController.ShowCredits)
				{
					ExecuteNextFrame.Invoke(() =>
					{
						CreditsController.ShowCredits = true;
					});

					EventSystem.current.SetSelectedGameObject(null);
				}
			});
			creditsButton.gameObject.SetActive(true);

			// Link buttons navigation
			var creditsNav = creditsButton.navigation;
			creditsNav.selectOnDown = multiMonitorButton;
			creditsNav.mode = UnityEngine.UI.Navigation.Mode.Explicit;

			var multiMonitorNav = multiMonitorButton.navigation;
			multiMonitorNav.selectOnUp = creditsButton;
			multiMonitorNav.selectOnDown = discordButton;
			multiMonitorNav.mode = UnityEngine.UI.Navigation.Mode.Explicit;

			var discordNav = discordButton.navigation;
			discordNav.selectOnUp = multiMonitorButton;
			discordNav.mode = UnityEngine.UI.Navigation.Mode.Explicit;
			/*discordNav.selectOnDown = backButton;

			var backNav = backButton.navigation;
			backNav.selectOnUp = discordButton;*/

			creditsButton.navigation = creditsNav;
			multiMonitorButton.navigation = multiMonitorNav;
			discordButton.navigation = discordNav;
			//backButton.navigation = backNav;

			// Text prefab
			TextPrefab = new GameObject("SimpleText Prefab", typeof(RectTransform), typeof(HGTextMeshProUGUI));
			TextPrefab.SetActive(false);

			var textPrefabHg = TextPrefab.GetComponent<HGTextMeshProUGUI>();
			var textTemplate = backButton.GetComponentInChildren<HGTextMeshProUGUI>();

			textPrefabHg.font = textTemplate.font;
			textPrefabHg.color = textTemplate.color;
			textPrefabHg.material = textTemplate.material;
			textPrefabHg.colorGradient = textTemplate.colorGradient;
			textPrefabHg.fontSharedMaterial = textTemplate.fontSharedMaterial;
			textPrefabHg.fontSizeMax = textTemplate.fontSizeMax;
			textPrefabHg.fontSizeMin = textTemplate.fontSizeMin;
			textPrefabHg.fontSize = textTemplate.fontSize;
			textPrefabHg.horizontalAlignment = TMPro.HorizontalAlignmentOptions.Center;
			textPrefabHg.verticalAlignment = TMPro.VerticalAlignmentOptions.Middle;

			var textPrefabLang = TextPrefab.AddComponent<LanguageTextMeshController>();
			textPrefabLang.textMeshPro = textPrefabHg;
			textPrefabLang.token = "XSS_UNSET";

			UIHelper.AddPrefab(UIHelper.EUIPrefabIndex.SimpleText, textPrefabLang.gameObject);

			CreateCountdownBanner(assignmentPanel);

			// (specter) Hint for the LB/RB "swap monitors" shortcut (LocalUserSlot.
			// HandleDisplaySlotMovement) - gamepad-only (keyboard/mouse can't use it), shown
			// statically for both platforms since a gamepad may join later. Only shown with
			// more than one display to move a player to.
			if (Display.displays.Length > 1)
			{
				var swapLegend = UIHelper.GetPrefab(UIHelper.EUIPrefabIndex.SimpleText);
				swapLegend.name = "SwapMonitorLegend";

				// (specter) The SimpleText prefab's LanguageTextMeshController re-resolves its own
				// .token onto the text every frame - with no token set here it kept stomping our
				// glyph string back to its unset placeholder. Remove it; text is set once below.
				var languageController = swapLegend.GetComponentInChildren<LanguageTextMeshController>();
				if (languageController != null)
					Destroy(languageController);

				var legendText = swapLegend.GetComponentInChildren<TMPro.TextMeshProUGUI>();
				legendText.color = RoR2MutedColor;
				// (specter) Bumped from 16 - the Xbox/PS glyphs (separate atlases, different native
				// scales) looked small and mismatched at that size.
				legendText.fontSize = 22;
				legendText.horizontalAlignment = TMPro.HorizontalAlignmentOptions.Left;
				legendText.verticalAlignment = TMPro.VerticalAlignmentOptions.Middle;

				// (specter) Grouped by platform (all PlayStation, then all Xbox) rather than
				// interleaved LB/L1 pairs - reads less jarring in same-family blocks.
				var xbox = LocalUserSlot.GamepadGlyphs["xbox"];
				var ps = LocalUserSlot.GamepadGlyphs["ps4"];
				string psGlyphs = $"<sprite=\"{ps.asset}\" name=\"{ps.lb}\"><sprite=\"{ps.asset}\" name=\"{ps.rb}\">";
				string xboxGlyphs = $"<sprite=\"{xbox.asset}\" name=\"{xbox.lb}\"><sprite=\"{xbox.asset}\" name=\"{xbox.rb}\">";
				// (specter) Hardcoded rather than via RoR2.Language.GetString - the token kept
				// resolving to a stale value (see Plugin.LoadLanguage's language.json merge), not
				// worth chasing for a legend this minor.
				legendText.text = $"{psGlyphs}  {xboxGlyphs}  Move to Another Display";

				// (specter) Positioned relative to the Back button's own RectTransform, sharing its
				// anchors AND pivot, rather than guessing at backPanelRect's anchor math (which
				// landed near screen-center) or hardcoding pivot.y (which sat lower than "Back"
				// whenever its actual pivot wasn't 0.5).
				var backButtonRect = (RectTransform) backButton.transform;
				float backButtonRightEdge = backButtonRect.anchoredPosition.x + backButtonRect.rect.width * (1f - backButtonRect.pivot.x);

				swapLegend.transform.SetParent(backButton.transform.parent, false);
				var legendRect = (RectTransform) swapLegend.transform;
				legendRect.anchorMin = backButtonRect.anchorMin;
				legendRect.anchorMax = backButtonRect.anchorMax;
				legendRect.pivot = new Vector2(0f, backButtonRect.pivot.y);
				legendRect.sizeDelta = new Vector2(560f, backButtonRect.rect.height);
				legendRect.anchoredPosition = new Vector2(backButtonRightEdge + 20f, backButtonRect.anchoredPosition.y);

				swapLegend.gameObject.SetActive(true);
			}

			AssignmentConfigurator.OnClaimUpdated += OnClaimUpdated;

			// Backup warning

			if (ShowBackupWarning)
			{
				var warningText = UIHelper.GetPrefab(UIHelper.EUIPrefabIndex.SimpleText);
				warningText.GetComponentInChildren<LanguageTextMeshController>().token = "XSS_WARN";
				var warnLayout = warningText.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
				var warnRect = warningText.gameObject.GetComponent<RectTransform>();
				warnRect.anchorMax = new Vector2(0.8f, 1f);
				warnRect.anchorMin = new Vector2(0.2f, 0f);
				//warnLayout.GetComponent<RectTransform>().sizeDelta = new Vector2(512, 200);
				warningText.transform.SetParent(notificationGroup.transform.Find("Content").transform);
				warningText.transform.localScale = Vector3.one;
				warningText.gameObject.SetActive(true);

				var phase1 = notificationGroup.transform.Find("W1").gameObject.AddComponent<PhasingGraphicColor>();
				phase1.phaseOffset = 1;

				notificationGroup.transform.Find("W2").gameObject.AddComponent<PhasingGraphicColor>();

				LocalUserPanel.AllowChanges = false;
			}
			else
			{
				Destroy(notificationGroup.gameObject);
			}

			// Credits

			transform.Find("Credits Panel").gameObject.AddComponent<CreditsController>();
		}

		private static void LogHierarchy(Transform root)
		{
			if (root == null)
			{
				Log.Print("LogHierarchy: root is null");
				return;
			}

			void Recurse(Transform t, string path, int depth)
			{
				string indent = new string(' ', depth * 2);
				Log.Print($"{indent}{(string.IsNullOrEmpty(path) ? "" : path)}{t.name} (activeSelf: {t.gameObject.activeSelf}, activeInHierarchy: {t.gameObject.activeInHierarchy})");

				for (int i = 0; i < t.childCount; i++)
					Recurse(t.GetChild(i), string.IsNullOrEmpty(path) ? t.name + "/" : path + t.name + "/", depth + 1);
			}

			Recurse(root, "", 0);
		}

		public void OnClaimUpdated(bool state)
		{
			if (state)
			{
				var users = AssignmentConfigurator.Instances.Where(x => x.HasUser).ToList();
				if (users.Count > 1 && users.TrueForAll(x => x.IsReady))
				{
					AllowLoad = state;
				}
			}
			else
			{
				AllowLoad = false;
			}

			loadTimer = 5f;
		}

		/// <summary>
		/// (specter) A small always-visible countdown, shown once every player is ready and the
		/// game is about to auto-start - there was previously no feedback at all that this was
		/// happening. Releasing a claimed region during the countdown cancels it (see OnClaimUpdated).
		/// </summary>
		private void CreateCountdownBanner(Transform assignmentPanel)
		{
			countdownBanner = UIHelper.GetPrefab(UIHelper.EUIPrefabIndex.SimpleText);
			countdownBanner.transform.SetParent(assignmentPanel, false);
			countdownBanner.transform.SetAsFirstSibling();
			countdownBanner.name = "CountdownBanner";

			var rect = countdownBanner.GetComponent<RectTransform>();
			rect.anchorMin = new Vector2(0f, 1f);
			rect.anchorMax = new Vector2(1f, 1f);
			rect.pivot = new Vector2(0.5f, 1f);
			rect.sizeDelta = new Vector2(0f, 40f);
			rect.anchoredPosition = Vector2.zero;

			// (specter) Raw numeric text, not a localization token - drop the token-driven
			// controller so it doesn't stomp the countdown text every frame.
			Destroy(countdownBanner.GetComponentInChildren<LanguageTextMeshController>());

			countdownText = countdownBanner.GetComponent<TMPro.TextMeshProUGUI>();
			countdownText.color = new Color(0.227f, 0.820f, 0.361f); // (specter) matches the confirm-green used elsewhere
			countdownText.fontSize = 24;

			countdownBanner.SetActive(false);
		}

		private void UpdateCountdownBanner()
		{
			if (countdownBanner == null) return;

			if (!AllowLoad)
			{
				if (countdownBanner.activeSelf)
					countdownBanner.SetActive(false);
				return;
			}

			if (!countdownBanner.activeSelf)
				countdownBanner.SetActive(true);

			countdownText.text = $"Starting in {Mathf.CeilToInt(loadTimer)}... release your region to cancel";
		}

		public void HandleLoadGame()
		{
			UpdateCountdownBanner();

			if (!AllowLoad) return;

			if (loadTimer > 0f)
			{
				loadTimer -= Time.unscaledDeltaTime;
				return;
			}

			SplitScreenSettings.BatchSaveDirtyUsers();

			var users = LocalUserSlot.Instances.Where(x => x.LocalPlayer != null)
				.OrderBy(x => !x.IsKeyboardUser)
				.ToArray();

			List<UserAssignmentData> assignments = new();

			var controllers = users[users.Length - 1].LocalPlayer.controllers.Controllers.ToList();

			// Shift all input players down
			for (int i = users.Length - 1; i > 0; i--)
			{
				var nextControllers = users[i - 1].LocalPlayer.controllers.Controllers.ToList();

				users[i].LocalPlayer = users[i - 1].LocalPlayer;
				users[i].LocalPlayer.controllers.ClearAllControllers();

				foreach (var controller in controllers)
					users[i].LocalPlayer.controllers.AddController(controller, false);

				controllers = nextControllers;
			}

			// Assign PlayerMain to first slot
			users[0].LocalPlayer = ReInput.players.GetPlayer("PlayerMain");
			foreach (var controller in controllers)
				users[0].LocalPlayer.controllers.AddController(controller, false);

			// Prepare assignments
			for (int i = 0; i < users.Length; i++)
			{
				assignments.Add(new UserAssignmentData
				{
					Profile = users[i].Profile ?? PlatformSystems.saveSystem.CreateGuestProfile(),
					UserIndex = i,
					InputPlayer = users[i].LocalPlayer,
					CameraRect = users[i].ScreenRect,
					Display = users[i].Panel.Controller.monitorId
				});
			}

			SplitscreenUserManager.InitializeUsers(assignments);

			AllowLoad = false;
			
			CarouselController carousel = gameModeButton.GetComponent<CarouselController>();
			var gamemodeValue= carousel.GetCurrentValue();
			SplitscreenUserManager.EnableSplitscreen(gamemodeValue);
		}

		public static void PrintDebugInfo()
		{
			Log.Print($"Displays:");
			foreach (var d in Display.displays)
				Log.Print($" - '{d}', '{d.active}', '{d.systemWidth} x {d.systemHeight}'");

			Log.Print($"Controllers:");
			foreach (var c in ReInput.controllers.Controllers)
				Log.Print($" - '{c.id}', '{c.name}', '{c.type}', '{c.hardwareName}', '{c.inputSource}', '{c.hardwareIdentifier}', '{c.hardwareTypeGuid}'");
		}
	}
}