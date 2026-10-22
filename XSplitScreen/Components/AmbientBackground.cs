using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Dodad.XSplitscreen.Components
{
	/// <summary>
	/// (specter) Soft drifting colored dots behind the splitscreen menu, drawn as UI images instead of a ParticleSystem.
	/// </summary>
	public class AmbientBackground : MonoBehaviour
	{
		private static readonly Color[] Palette =
		{
			new Color(0.42f, 0.24f, 0.40f), // (specter) #6a3c66 - purple dot
			new Color(0.18f, 0.42f, 0.45f), // (specter) #2e6b74 - teal dot
			new Color(0.30f, 0.20f, 0.45f),
			new Color(0.15f, 0.30f, 0.40f),
		};

		private class Dot
		{
			public RectTransform Rect;
			public Image Image;
			public Vector2 Origin;
			public float Speed;
			public float Phase;
			public float Radius;
			public float BaseAlpha;
		}

		private readonly List<Dot> _dots = new List<Dot>();

		private static Texture2D softDot;

		// (specter) Gaussian falloff that reaches zero before the edge, for a soft blurred dot
		private static Texture2D GetSoftDot()
		{
			if (softDot != null)
				return softDot;

			const int size = 128;
			softDot = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float dx = (x + 0.5f) / size * 2f - 1f;
					float dy = (y + 0.5f) / size * 2f - 1f;
					float distance = Mathf.Sqrt(dx * dx + dy * dy);
					float alpha = Mathf.Exp(-distance * distance * 5f) * Mathf.Clamp01(1f - distance);
					softDot.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
				}
			}

			softDot.Apply();
			return softDot;
		}

		public void Awake()
		{
			var sprite = UIHelper.GetSprite(GetSoftDot());

			var rect = (RectTransform) transform;
			var rand = new System.Random(12345); // (specter) fixed seed, for a deterministic layout

			const int count = 6;
			for (int i = 0; i < count; i++)
			{
				var go = new GameObject($"Dot{i}", typeof(RectTransform));
				var dotRect = (RectTransform) go.transform;
				dotRect.SetParent(rect, false);

				float size = Mathf.Lerp(40f, 90f, (float) rand.NextDouble());
				dotRect.sizeDelta = new Vector2(size, size);
				dotRect.anchorMin = dotRect.anchorMax = new Vector2((float) rand.NextDouble(), (float) rand.NextDouble());
				dotRect.anchoredPosition = Vector2.zero;

				var img = go.AddComponent<Image>();
				img.sprite = sprite;
				img.raycastTarget = false;
				var color = Palette[i % Palette.Length];
				float baseAlpha = Mathf.Lerp(0.18f, 0.4f, (float) rand.NextDouble());
				img.color = new Color(color.r, color.g, color.b, baseAlpha);

				_dots.Add(new Dot
				{
					Rect = dotRect,
					Image = img,
					Origin = dotRect.anchoredPosition,
					Speed = Mathf.Lerp(0.1f, 0.25f, (float) rand.NextDouble()),
					Phase = (float) rand.NextDouble() * Mathf.PI * 2f,
					Radius = Mathf.Lerp(60f, 140f, (float) rand.NextDouble()),
					BaseAlpha = baseAlpha,
				});
			}
		}

		public void Update()
		{
			float t = Time.time;
			foreach (var dot in _dots)
			{
				float x = Mathf.Sin(t * dot.Speed + dot.Phase) * dot.Radius;
				float y = Mathf.Cos(t * dot.Speed * 0.7f + dot.Phase) * dot.Radius * 0.6f;
				dot.Rect.anchoredPosition = dot.Origin + new Vector2(x, y);

				var img = dot.Image;
				float alpha = dot.BaseAlpha * (0.7f + 0.3f * Mathf.Sin(t * dot.Speed * 1.3f + dot.Phase));
				var c = img.color;
				img.color = new Color(c.r, c.g, c.b, alpha);
			}
		}
	}
}
