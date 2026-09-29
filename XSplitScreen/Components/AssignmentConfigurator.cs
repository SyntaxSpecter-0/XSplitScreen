using RoR2;
using RoR2.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static RoR2.UI.CursorIndicatorController;

namespace Dodad.XSplitscreen.Components
{
	public class AssignmentConfigurator : OptionConfigurator
	{
		public static Action<bool> OnClaimUpdated;

		public static List<AssignmentConfigurator> Instances { get; } = new List<AssignmentConfigurator>();

		internal UnityEngine.Rect Rect => _claim?.ScreenRect ?? new Rect(0, 0, 1, 1);
		internal bool HasUser => Options.Slot.LocalPlayer != null;
		internal bool IsReady => _claim != null;

		private float _maxAcceleration = 5000;
		private float _maxSpeed = 500;
		private float _animationSpeed = 100f;

		private RectTransform _cursor;
		private Image _cursorImage;

		private bool _isOpen;
		private bool _showCursor;

		private Selectable _hoverTarget;
		private DisplayClaim _claim;

		// Velocity state for this cursor instance
		private Vector2 _velocity;

		private RectTransform _controllerRect;

		// (specter) The panel this cursor is currently anchored to. Tracked so a slot move to a
		// different monitor's panel (LocalUserSlot.MoveSlotByDirection, via LB/RB) can be detected
		// and the cursor re-anchored - see RefreshAnchorIfPanelChanged.
		private LocalUserPanel _anchoredPanel;

		/// <summary>
		/// Move a single cursor on the UI.
		/// target: RectTransform of the cursor
		/// input: Vector2 gamepad input (x = leftRight, y = upDown)
		/// maxSpeed: maximum speed per second
		/// acceleration: acceleration per second
		/// </summary>
		public void MoveCursor(RectTransform target, Vector2 input, float maxSpeed, float acceleration)
		{
			if (_claim != null)
				return;

			// Decelerate if no input
			if (input.sqrMagnitude < 0.01f)
			{
				_velocity = Vector2.MoveTowards(_velocity, Vector2.zero, acceleration * Time.unscaledDeltaTime);
			}
			else
			{
				Vector2 desiredVelocity = input.normalized * maxSpeed;
				_velocity = Vector2.MoveTowards(_velocity, desiredVelocity, acceleration * Time.unscaledDeltaTime);
			}

			Vector2 newPos = target.anchoredPosition + _velocity * Time.unscaledDeltaTime;

			if (target.parent is RectTransform parentRect)
			{
				Vector2 min = parentRect.rect.min - target.rect.min;
				Vector2 max = parentRect.rect.max - target.rect.max;
				newPos.x = Mathf.Clamp(newPos.x, min.x, max.x);
				newPos.y = Mathf.Clamp(newPos.y, min.y, max.y);
			}

			if ((target.anchoredPosition - newPos).sqrMagnitude > 0.01f)
			{
				target.anchoredPosition = newPos;
			}
		}

		private Vector3 originalScale;
		private Quaternion originalRotation;

		/// <summary>
		/// Call this every frame to smoothly scale and rotate a RectTransform.
		/// </summary>
		/// <param name="target">RectTransform to modify</param>
		/// <param name="active">If true, animate to target values; if false, return to original</param>
		/// <param name="targetScale">Target scale if active</param>
		/// <param name="rotationDegrees">Target Z rotation in degrees if active</param>
		/// <param name="speed">Speed for scale and rotation change (units/sec and degrees/sec)</param>
		public void Animate(RectTransform target, bool active, float targetScale, float rotationDegrees, float speed)
		{
			// Scale
			Vector3 currentScale = target.localScale;
			Vector3 desiredScale = active ? new Vector3(targetScale, targetScale, targetScale) : originalScale;
			Vector3 newScale = Vector3.MoveTowards(currentScale, desiredScale, speed * Time.unscaledDeltaTime);

			Quaternion currentRot = target.localRotation;
			Quaternion desiredRot = active
				? Quaternion.Euler(0f, 0f, rotationDegrees)
				: originalRotation;
			Quaternion newRot = Quaternion.RotateTowards(currentRot, desiredRot, speed * 4f * Time.unscaledDeltaTime);

			if ((currentScale - newScale).sqrMagnitude > 0.0001f)
				target.localScale = newScale;
			if (Quaternion.Angle(currentRot, newRot) > 0.01f)
				target.localRotation = newRot;
		}

		/// <summary>
		/// Raycast under a UI element's position, using only Unity APIs.
		/// </summary>
		/// <param name="uiElement">RectTransform of target UI element (e.g. cursor)</param>
		/// <param name="canvas">Canvas containing the UI element</param>
		/// <param name="camera">Camera for the canvas (can be null for ScreenSpaceOverlay)</param>
		/// <returns>First Selectable hit (MPButton or MPToggle), or null if none</returns>
		public static Selectable RaycastAtUIElement(RectTransform uiElement, Canvas canvas, Camera camera)
		{
			if (uiElement == null || canvas == null)
				return null;

			// Get world position of the UI element (center point)
			Vector3 worldPos = uiElement.position;

			// Get screen position for the correct canvas/display
			Vector2 screenPos;
			if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
			{
				screenPos = RectTransformUtility.WorldToScreenPoint(null, worldPos);
			}
			else
			{
				screenPos = RectTransformUtility.WorldToScreenPoint(camera, worldPos);
			}

			var raycaster = canvas.GetComponent<GraphicRaycaster>();
			var eventSystem = EventSystem.current;
			if (raycaster == null || eventSystem == null)
				return null;

			PointerEventData ped = new PointerEventData(eventSystem) { position = screenPos };
			List<RaycastResult> results = new List<RaycastResult>();
			raycaster.Raycast(ped, results);

			for (int i = 0; i < results.Count; i++)
			{
				var go = results[i].gameObject;
				Selectable s = go.GetComponent<MPButton>();
				if (s == null)
					s = go.GetComponentInParent<MPToggle>();
				if (s != null)
					return s;
			}
			return null;
		}

		public void Awake()
		{
			EnableCancelButton = true;

			Instances.Add(this);

			OnClaimUpdated += ClaimUpdatedHandler;
		}

		public void OnDestroy()
		{
			CleanupCursor();

			Instances.Remove(this);

			OnClaimUpdated -= ClaimUpdatedHandler;
		}

		private void ClaimUpdatedHandler(bool state)
		{
			UpdateName();
		}

		public override int GetPriority() => int.MaxValue;

		public override void ForceClose()
		{
			Cleanup();
			CleanupCursor();
		}

		public override bool CanOpen() => true;

		public override string GetName() => "XSS_SELECT_SCREEN";

		public override void OnCancel()
		{
			Cleanup();

			OnFinished?.Invoke();
		}

		public override void OnConfirm()
		{
			if (_claim != null)
				return;

			Claim();
			HandleClick();
		}

		public override void OnNavigate(int direction)
		{

		}

		public override void Open()
		{
			_showCursor = !Options.Slot.IsKeyboardUser;

			if(_showCursor)
			{
				if(_cursor == null)
				{
					_cursor = new GameObject("Cursor").AddComponent<RectTransform>();
					_cursor.transform.localScale = Vector3.one;
					_cursor.transform.position = transform.position;
					_cursorImage = _cursor.gameObject.AddComponent<Image>();
					var texture = Plugin.Resources.LoadAsset<Texture2D>("crosshair.png");
					_cursorImage.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
					_cursor.sizeDelta = new Vector2(64f, 64f);

					originalScale = _cursor.localScale;
					originalRotation = _cursor.localRotation;
				}

				RefreshAnchorIfPanelChanged();
				SetCursorVisibility(true);
			}

			_isOpen = true;
		}

		/// <summary>
		/// (specter) Re-anchors the cursor's bounds and parent to whichever panel this slot
		/// currently belongs to - needed since a player can move slots to a different monitor via
		/// LB/RB after Open() already ran once. Cheap comparison, safe to call every frame.
		/// </summary>
		private bool _anchorInitialized;

		private void RefreshAnchorIfPanelChanged()
		{
			var currentPanel = Options.Slot.Panel;
			if (currentPanel == null)
				return; // (specter) not assigned yet - retry next call instead of anchoring to nothing

			// (specter) _anchorInitialized guards the first call - comparing currentPanel to a null
			// _anchoredPanel would read "null == null" as no change and skip anchoring entirely.
			if (_anchorInitialized && currentPanel == _anchoredPanel)
				return;

			_anchorInitialized = true;
			_anchoredPanel = currentPanel;
			_controllerRect = currentPanel.Controller?.GetComponent<RectTransform>();

			// (specter) Parented to Main Panel, not just Assignment Panel, so MoveCursor's clamp
			// lets it reach the Credits/Discord/Back buttons too.
			if (_cursor != null)
			{
				_cursor.SetParent(currentPanel.transform.parent);
				_cursor.SetAsLastSibling();
			}
		}

		public override void ConfiguratorUpdate()
		{
			if(_isOpen && _showCursor)
			{
				RefreshAnchorIfPanelChanged();

				// (specter) Kept in sync every frame - the cursor opens before a color is usually picked.
				if (_cursorImage != null)
					_cursorImage.color = Options.Slot.MainColor;

				MoveCursor(_cursor, new Vector2(Options.Slot.Input.LeftRightDelta, Options.Slot.Input.UpDownDelta), _maxSpeed, _maxAcceleration);
				CheckCursorHovering();

				if (Options.Slot.Input.South)
				{
					OnConfirm();
				}

				// (specter) East releases a claimed region only - the cursor is always-on now, so
				// calling Cleanup() here would hide it permanently with no way back.
				if (Options.Slot.Input.East && _claim != null)
					ResetClaim();
			}

			if(SplitscreenMenuController.ReadyToLoad && IsReady)
			{
				Options.SetMessage(((int) SplitscreenMenuController.LoadTimer).ToString(), Color.green);
			}
		}

		private void Cleanup()
		{
			ResetClaim();

			_isOpen = false;

			if (_showCursor)
				SetCursorVisibility(false);
		}

		private void SetCursorVisibility(bool visible)
		{
			if (_cursorImage == null)
				return;

			if(visible)
			{
				_cursorImage.color = Options.Slot.MainColor;
				_cursorImage.enabled = true;
			}
			else
			{
				_cursorImage.enabled = false;
			}
		}

		private void CheckCursorHovering()
		{
			var previousTarget = _hoverTarget;
			_hoverTarget = Utilities.Hovercast.GetSelectableUnderCursor(_controllerRect, _cursor);

			// (specter) Hovercast finds the Selectable but never fires a hover event, so a button's
			// highlight never triggered even though clicking already worked. Driving this player's
			// own MPEventSystem selection (per-player, not the global EventSystem) fixes that.
			if (_hoverTarget != previousTarget)
				Options.Slot.EventSystem?.SetSelectedObject(_hoverTarget != null ? _hoverTarget.gameObject : null);

			// Animate cursor based on hover state
			Animate(_cursor, _hoverTarget != null, 0.8f, 45f, _animationSpeed);
		}

		private void Claim()
		{
			if(_hoverTarget != null)
				_claim = _hoverTarget.GetComponent<DisplayClaim>();
			
			if(_claim != null)
			{
				if(_claim.IsClaimed)
				{
					_claim = null;

					return;
				}

				_claim.Claim(this);

				if (_showCursor)
					SetCursorVisibility(false);

				UpdateName();
				OnClaimUpdated?.Invoke(true);
			}
		}

		public void ResetClaim()
		{
			if(_claim != null)
				_claim.Claim(null);

			_claim = null;
			UpdateName();
			SetCursorVisibility(true);
			OnClaimUpdated?.Invoke(false);
		}

		private void UpdateName()
		{
			if (!_isOpen)
				return;

			if (_claim == null)
				Options.SetMessage("XSS_SELECT_SCREEN");
			else
			{
				int total = Instances.Where(x => x.Options.Slot.LocalPlayer != null).Count();
				int ready = Instances.Where(x => x.IsReady).Count();

				string s = Language.GetString("XSS_READY", Language.currentLanguage.name);

				Options.SetMessage($"{s} ({ready} / {total})", Color.yellow);
			}
		}

		private void HandleClick()
		{
			if (_claim == null && _hoverTarget != null && _hoverTarget.interactable)
			{
				if (_hoverTarget is MPButton button)
					button.InvokeClick();
				else if (_hoverTarget is MPToggle toggle)
					toggle.isOn = !toggle.isOn;
			}
		}

		private void CleanupCursor()
		{
			if (_cursor != null)
				Destroy(_cursor.gameObject);

			// (specter) Also reset the anchor state, not just _cursor - otherwise a rebuilt cursor
			// gets skipped by RefreshAnchorIfPanelChanged's "same panel, nothing to do" check and
			// never gets parented into the UI at all.
			_anchorInitialized = false;
			_anchoredPanel = null;
		}

		public void ReceiveClaim(DisplayClaim claim)
		{
			if (!_isOpen)
				return;

			if(_claim == claim)
			{
				ResetClaim();
			}
			else
			{
				if (_claim != null)
					return;

				_claim = claim;

				Claim();
			}
		}
	}
}
