namespace Ruinarch.ModContent
{
	/// <summary>Where a <see cref="Residency.Travel"/> call stopped.</summary>
	public enum TravelStage
	{
		None,
		Validate,
		Freeze,
		Load,
		Activate,
		Ready,
		Rollback
	}

	/// <summary>The outcome of one <see cref="Residency.Travel"/> call.</summary>
	public sealed class TravelResult
	{
		internal TravelResult(bool succeeded, TravelStage failedStage, string reason, bool warm, double seconds)
		{
			Succeeded = succeeded;
			FailedStage = failedStage;
			Reason = reason;
			Warm = warm;
			Seconds = seconds;
		}

		public bool Succeeded { get; }

		/// <summary><see cref="TravelStage.None"/> when the travel succeeded.</summary>
		public TravelStage FailedStage { get; }

		/// <summary>Null when the travel succeeded.</summary>
		public string Reason { get; }

		/// <summary>True for a switch back to the retained world.</summary>
		public bool Warm { get; }

		/// <summary>Wall time of the whole transition.</summary>
		public double Seconds { get; }

		public override string ToString()
		{
			return Succeeded
				? (Warm ? "warm" : "cold") + " travel succeeded in " + Seconds.ToString("F3") + " s"
				: (Warm ? "warm" : "cold") + " travel failed at " + FailedStage + ": " + Reason;
		}
	}
}
