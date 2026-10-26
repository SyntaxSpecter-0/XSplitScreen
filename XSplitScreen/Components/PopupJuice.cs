using HG;
using UnityEngine;

namespace Dodad.XSplitscreen.Components
{
	// (specter) Fade, pop, shake and pulse for the menu popups, using the same curves as RoR2's UIJuice
	public class PopupJuice : MonoBehaviour
	{
		private const float FadeDuration = 0.2f;
		private const float ShakeDuration = 0.35f;
		private const float ShakeDistance = 10f;
		private const float PulseDuration = 0.25f;
		private const float PulseScale = 0.08f;
		private const float PopStartScale = 0.9f;

		private static readonly AnimationCurve easeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
		private static readonly AnimationCurve sharpCurve = new AnimationCurve(new Keyframe(0f, 0f, 3f, 3f), new Keyframe(1f, 1f, 0f, 0f));

		public bool IsShown { get; private set; }

		private CanvasGroup group;
		private RectTransform rect;
		private Vector2 basePosition;

		private float fadeElapsed = FadeDuration;
		private float fadeFrom;
		private float shakeElapsed = ShakeDuration;
		private float pulseElapsed = PulseDuration;

		private void Awake()
		{
			group = gameObject.EnsureComponent<CanvasGroup>();
			group.alpha = 0f;
			rect = (RectTransform) transform;
			basePosition = rect.anchoredPosition;
		}

		public void Show()
		{
			if (IsShown)
				return;

			IsShown = true;
			gameObject.SetActive(true);
			StartFade();
		}

		public void Hide()
		{
			if (!IsShown)
				return;

			IsShown = false;
			StartFade();
		}

		// (specter) For leaving the menu, where Update stops before a fade could finish
		public void HideImmediate()
		{
			IsShown = false;
			fadeElapsed = FadeDuration;
			group.alpha = 0f;
			gameObject.SetActive(false);
		}

		public void Shake() => shakeElapsed = 0f;

		public void Pulse() => pulseElapsed = 0f;

		private void StartFade()
		{
			fadeFrom = group.alpha;
			fadeElapsed = 0f;
		}

		private void Update()
		{
			float deltaTime = Time.unscaledDeltaTime;

			fadeElapsed = Mathf.Min(fadeElapsed + deltaTime, FadeDuration);
			shakeElapsed = Mathf.Min(shakeElapsed + deltaTime, ShakeDuration);
			pulseElapsed = Mathf.Min(pulseElapsed + deltaTime, PulseDuration);

			float fade = fadeElapsed / FadeDuration;
			group.alpha = Mathf.Lerp(fadeFrom, IsShown ? 1f : 0f, easeCurve.Evaluate(fade));

			float scale = IsShown ? Mathf.Lerp(PopStartScale, 1f, sharpCurve.Evaluate(fade)) : 1f;
			scale += PulseScale * (1f - sharpCurve.Evaluate(pulseElapsed / PulseDuration));
			rect.localScale = new Vector3(scale, scale, 1f);

			float shake = 1f - shakeElapsed / ShakeDuration;
			rect.anchoredPosition = basePosition + new Vector2(Mathf.Sin(shakeElapsed * 55f) * ShakeDistance * shake, 0f);

			if (!IsShown && fadeElapsed >= FadeDuration)
				gameObject.SetActive(false);
		}
	}
}
