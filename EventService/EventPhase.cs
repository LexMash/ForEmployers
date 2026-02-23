using System;

/// <summary>
/// Represents a separate stage/period within the active phase of an event
/// </summary>
[Serializable]
public class EventPhase
{
    public string Id;
    public int DurationHours;
    [NonSerialized] public bool IsActive;

    public EventPhase(string id, int durationHours)
    {
        Id = id;
        DurationHours = durationHours;
    }
}