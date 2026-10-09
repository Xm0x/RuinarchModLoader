using System;
using System.Threading;
using UnityEngine;

namespace Ruinarch.ModContent
{
	/// <summary>
	/// Counts errors logged on any thread while a travel runs (pathfinding workers log off the
	/// main thread) and remembers the stage reached. The shipped game switches Unity's logger
	/// off (<c>WorldConfigManager.Awake</c>, again on every scene load), which would hide every
	/// error from us, so the logger is kept on for the duration of a travel.
	/// </summary>
	internal sealed class TransitionGuard : IDisposable
	{
		private readonly bool loggingWasEnabled;
		private int _errors;

		internal TravelStage Stage = TravelStage.Validate;

		internal TransitionGuard()
		{
			loggingWasEnabled = Debug.unityLogger.logEnabled;
			Debug.unityLogger.logEnabled = true;
			Application.logMessageReceivedThreaded += OnLog;
		}

		internal int Errors => Volatile.Read(ref _errors);

		internal string FirstError { get; private set; }

		/// <summary>Call every frame: a scene load during the travel switches logging off again.</summary>
		internal void KeepLogging()
		{
			if (!Debug.unityLogger.logEnabled)
			{
				Debug.unityLogger.logEnabled = true;
			}
		}

		private void OnLog(string message, string trace, LogType type)
		{
			if (type != LogType.Error && type != LogType.Exception)
			{
				return;
			}
			if (Interlocked.Increment(ref _errors) == 1)
			{
				FirstError = message;
			}
		}

		public void Dispose()
		{
			Application.logMessageReceivedThreaded -= OnLog;
			Debug.unityLogger.logEnabled = loggingWasEnabled;
		}
	}
}
