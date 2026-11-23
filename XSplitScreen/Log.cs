using BepInEx.Logging;

namespace Dodad.XSplitscreen
{
	internal static class Log
	{
		private static ManualLogSource Source;
		private static bool DebugEnabled;

		/// <summary>
		/// True when the user enabled the Diagnostics option (extra reports for bug reports)
		/// </summary>
		internal static bool DiagnosticsEnabled { get; private set; }

		/// <summary>
		/// True when the user enabled the Input option (a line for every button press, per player)
		/// </summary>
		internal static bool InputEnabled { get; private set; }

		/// <summary>
		/// Initialize the logger with the plugin log source
		/// </summary>
		/// <param name="source"></param>
		/// <param name="debug">Print Debug channel messages</param>
		/// <param name="diagnostics">Print Diag messages</param>
		/// <param name="input">Print Input messages (button presses)</param>
		internal static void Init(ManualLogSource source, bool debug, bool diagnostics, bool input)
		{
			Source = source;
			DebugEnabled = debug;
			DiagnosticsEnabled = diagnostics;
			InputEnabled = input;
		}

		/// <summary>
		/// Output a message to the console. Debug and Input messages are only printed when enabled in the config.
		/// </summary>
		/// <param name="data"></param>
		/// <param name="channel"></param>
		internal static void Print(object data, ELogChannel channel = ELogChannel.Debug)
		{
			if (Source == null)
				return;

			switch (channel)
			{
				case ELogChannel.Message:
					Source.LogMessage(data);
					break;
				case ELogChannel.Info:
					Source.LogInfo(data);
					break;
				case ELogChannel.Warning:
					Source.LogWarning(data);
					break;
				case ELogChannel.Error:
					Source.LogError(data);
					break;
				case ELogChannel.Fatal:
					Source.LogFatal(data);
					break;
				case ELogChannel.Debug:
					if (DebugEnabled)
						Source.LogDebug(data);
					break;
				case ELogChannel.Input:
					if (InputEnabled)
						Source.LogInfo($"[Input] {data}");
					break;
			}
		}

		/// <summary>
		/// Output a diagnostics message. Only printed when Diagnostics is enabled in the config.
		/// </summary>
		/// <param name="data"></param>
		internal static void Diag(object data)
		{
			if (Source == null || !DiagnosticsEnabled)
				return;

			Source.LogInfo($"[Diagnostics] {data}");
		}

		internal enum ELogChannel
		{
			Message,
			Info,
			Warning,
			Error,
			Fatal,
			Debug,
			Input,
		}
	}
}
