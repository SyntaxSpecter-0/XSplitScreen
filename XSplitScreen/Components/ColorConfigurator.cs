using RoR2.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Dodad.XSplitscreen.Components
{
	internal class ColorConfigurator : OptionConfigurator
	{
		private static Texture2D Hue;
		private static Color[] ColorSlice;

		private ColorSettingsModule _colorModule;
		private RectTransform _hueGradient;
		private RectTransform _indicator;
		private Image _swatch;

		private float _indicatorPosition = 0.5f;

		private Coroutine _dragIndicatorRoutine;

		private bool _uiResolved;

		public override string GetName() => "XSS_CONFIG_COLOR";

		/// <summary>
		/// Updates the configurator based on input.
		/// </summary>
		public override void ConfiguratorUpdate()
		{
			// (specter) Left/Right on the d-pad scrubs the hue bar continuously while held, rather
			// than requiring analog stick precision - matches how a mouse-drag already works here.
			if (Options.Slot.Input.LeftHeld)
			{
				IsConfirmed = false;
				UpdateSelection(-1f);
				UpdateSelectedColor();
			}
			else if (Options.Slot.Input.RightHeld)
			{
				IsConfirmed = false;
				UpdateSelection(1f);
				UpdateSelectedColor();
			}
			else if (Options.Slot.Input.South)
			{
				OnConfirm();
			}
			else if (Options.Slot.Input.East)
			{
				OnCancel();
			}

			UpdateIndicatorPosition();
		}

		public override bool CanOpen() => true;

		public override void Open()
		{
			ResolveUI();

			IsConfirmed = true; // (specter) a color is always set; nudging clears this, A/confirm re-sets it
		}

		public override void ForceClose()
		{
			StopDragRoutine();
		}

		// (specter) The redesigned ColorContent panel is a single hue-scrub row (no separate
		// selection states) - Up/Down navigation no longer applies here.
		public override void OnNavigate(int direction) { }

		public override void OnCancel()
		{
			// (specter) Gamepad: East no longer closes the panel here (only North/Y does) - the
			// color already saves live as it's scrubbed, so there's nothing left to revert.
			if (Options.Slot.IsKeyboardUser)
				SaveAndClose();
		}

		public override void OnConfirm()
		{
			IsConfirmed = true;
		}

		public override void OnLoadProfile()
		{
			LoadColor();
		}

		private void SaveAndClose()
		{
			StopDragRoutine();
			OnFinished();
		}

		private void LoadColor()
		{
			ResolveUI();

			_colorModule = null;

			if (Options.Slot.Profile != null)
				_colorModule = SplitScreenSettings.GetOrCreateUserModule<ColorSettingsModule>(Options.Slot.Profile.fileName);

			var color = GetColor();

			SetColor(color, false);
			var index = GetIndicatorIndexFromColor(color);
			_indicatorPosition = index / 255f;
			UpdateIndicatorPosition(_indicatorPosition);
		}

		private void SaveColor()
		{
			if (Options.Slot.Profile == null)
				return;

			SplitScreenSettings.MarkUserDirty(Options.Slot.Profile.fileName);
		}

		public Color GetColor() => _colorModule == null ? Color.white : _colorModule.Color;

		public void SetColor(Color color, bool markDirty = true)
		{
			if (_colorModule != null)
			{
				_colorModule.MainR = color.r;
				_colorModule.MainG = color.g;
				_colorModule.MainB = color.b;
				_colorModule.MainA = color.a;
			}

			Options.Slot.MainColor = color;

			if (_swatch != null)
				_swatch.color = color;

			if (markDirty)
				SaveColor();
		}

		private void UpdateSelection(float direction)
		{
			float step = Mathf.Sign(direction) * 0.2f * Time.deltaTime;

			_indicatorPosition = Mathf.Clamp01(_indicatorPosition + step);
		}

		private void UpdateSelectedColor()
		{
			SetColor(ColorSlice[(int) (_indicatorPosition * 255)]);
		}

		private void UpdateIndicatorPosition(float? pos = null)
		{
			pos ??= Mathf.Lerp(_indicator.anchorMin.x, _indicatorPosition, 5f * Time.deltaTime);
			
			_indicator.anchorMin = new Vector2(pos.Value, 0.4f);
			_indicator.anchorMax = new Vector2(pos.Value, 0.6f);
		}

		/// <summary>
		/// (specter) Gets the gradient index matching a color, or 0 if not found.
		/// </summary>
		private int GetIndicatorIndexFromColor(Color color)
		{
			for(int e = 0; e < 256; e++)
			{
				if (ColorSlice[e] == color)
				{
					return e;
				}
			}

			return 0;
		}

		private void StartDragRoutine()
		{
			StopDragRoutine();
			_dragIndicatorRoutine = StartCoroutine(DragRoutine(() =>
			{
				UpdateIndicatorPosition(_indicatorPosition);
				UpdateSelectedColor();
			},
			() =>
			{
				MPEventSystem.current.SetSelectedGameObject(null);
			}));
		}

		private void StopDragRoutine()
		{
			if (_dragIndicatorRoutine != null)
				StopCoroutine(_dragIndicatorRoutine);
		}
		private IEnumerator DragRoutine(Action update, Action cleanup)
		{
			yield return new WaitForEndOfFrame();

			while(Options.Slot.Input.MouseLeft)
			{
				_indicatorPosition = GetCursorHorizontalNormalized(_hueGradient);

				update?.Invoke();

				yield return null;
			}

			cleanup?.Invoke();
		}

		/// <summary>
		/// (specter) Wires up to the pre-built ColorContent elements. Lazy (first Open(), not
		/// Awake()) since Options isn't assigned until just after Awake() runs.
		/// </summary>
		private void ResolveUI()
		{
			if (_uiResolved) return;
			_uiResolved = true;

			if (Hue == null)
			{
				Hue = GenerateColorSpectrumGradient(256, 1);
				ColorSlice = Hue.GetPixels(0, 0, 256, 1);
			}

			var colorContent = Options.Slot.transform.Find("ExpandedContent/ColorContent");

			_hueGradient = (RectTransform) colorContent.Find("HueRow/HueBarContainer");
			_hueGradient.GetComponent<Image>().sprite = Sprite.Create(Hue, new Rect(0, 0, 256, 1), new Vector2(0.5f, 0.5f));
			var colorButton = _hueGradient.gameObject.AddComponent<MPButton>();
			colorButton.onSelect = new UnityEngine.Events.UnityEvent();
			colorButton.onSelect.AddListener(() => StartDragRoutine());
			colorButton.allowAllEventSystems = true;

			_indicator = (RectTransform) _hueGradient.Find("Indicator");

			_swatch = colorContent.Find("SwatchRow/Swatch").GetComponent<Image>();
		}

		/// <summary>
		/// (specter) Generates a horizontal full-spectrum hue gradient texture.
		/// </summary>
		public static Texture2D GenerateColorSpectrumGradient(int width, int height)
		{
			Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

			for (int x = 0; x < width; x++)
			{
				// Calculate hue based on x position (0 to 1)
				float hue = (float) x / width;
				Color color = Color.HSVToRGB(hue, 1f, 1f);

				// Set this color for the entire column
				for (int y = 0; y < height; y++)
				{
					texture.SetPixel(x, y, color);
				}
			}

			texture.Apply();
			return texture;
		}

		/// <summary>
		/// (specter) Normalized horizontal cursor position over the RectTransform (0 = left, 1 = right).
		/// </summary>
		public static float GetCursorHorizontalNormalized(RectTransform rectTransform, Camera uiCamera = null)
		{
			Vector2 mouseScreenPos = Input.mousePosition;

			Vector2 localPoint;
			if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, mouseScreenPos, uiCamera, out localPoint))
			{
				float left = rectTransform.rect.xMin;
				float right = rectTransform.rect.xMax;

				if (localPoint.x <= left)
					return 0f;
				if (localPoint.x >= right)
					return 1f;

				return (Mathf.Clamp(localPoint.x, left, right) - left) / (right - left);
			}

			return 0f;
		}
	}
}
