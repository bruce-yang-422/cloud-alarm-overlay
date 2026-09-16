namespace CloudAlarmOverlay.Core;

/// <summary>A callable contract exists, but its business behavior is outside Milestone 0.</summary>
public sealed class MilestoneNotImplementedException(string operation)
    : NotImplementedException($"{operation} is a scaffold; business behavior is not implemented in Milestone 0.");

