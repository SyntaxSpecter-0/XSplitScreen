using BepInEx.Logging;
using System;

namespace Dodad.XSplitscreen
{
	internal static class Log
	{
		private static ManualLogSource Source;
		private static ELogChannel ActiveChannels;

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
		/// <param name="channels">Channels enabled in the config</param>
		/// <param name="diagnostics">Print Diag messages</param>
		internal static void Init(ManualLogSource source, ELogChannel channels, bool diagnostics)
		{
			Source = source;
			ActiveChannels = channels;
			DiagnosticsEnabled = diagnostics;
			InputEnabled = channels.HasFlag(ELogChannel.Input);
		}

		/// <summary>
		/// Output a message to the console. Debug and Input messages are only printed when enabled in the config.
		/// </summary>
		/// <param name="data"></param>
		/// <param name="channel"></param>
		internal static void Print(object data, ELogChannel channel = ELogChannel.Debug)
		{
			if (Source == null || !ActiveChannels.HasFlag(channel))
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
					Source.LogDebug(data);
					break;
				case ELogChannel.Input:
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

		[Flags]
		internal enum ELogChannel
		{
			None = 0,
			Message = 1 << 0,
			Info = 1 << 1,
			Warning = 1 << 2,
			Error = 1 << 3,
			Fatal = 1 << 4,
			Debug = 1 << 5,
			Input = 1 << 6,
			All = Message | Info | Warning | Error | Fatal | Debug | Input,
		}
	}
}
