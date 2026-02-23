using System;
using System.Linq;
using UnityEngine;
using Utilities.Extensions;

[CreateAssetMenu(fileName = nameof(EventSettings), menuName = "Events/EventSettings")]
public class EventSettings : ScriptableObject
{
    [field: SerializeField] public InGameEventType EventType { get; private set; }
    [field: SerializeField] public bool IsEnable { get; private set; } = true;
    [field: SerializeField] public DateTimeKind TimeMode { get; private set; } = DateTimeKind.Local;

    [SerializeField] private DateTimeSerializable startDate;
    [SerializeField] private DateTimeSerializable endDate;

    [field: Space, SerializeField] public bool IsRecurring { get; private set; }
    [field: Space, SerializeField] public RecurrenceSettings RecurrenceSettings { get; private set; }

    [Header("If you do not specify phases, the default \"active\" phase will be created")]
    [SerializeField] private EventPhase[] phases;

    public EventPhase[] Phases => phases.IsNullOrEmpty()
        ? new EventPhase[] { new("active", (int)(EndDate - StartDate).TotalHours) }
        : phases.ToArray();

    public DateTime StartDate => startDate.ConvertToDateTime(TimeMode);
    public DateTime EndDate => endDate.ConvertToDateTime(TimeMode);

    [Serializable]
    private struct DateTimeSerializable
    {
        [Range(1, 31)] public int Day;
        [Range(1, 12)] public int Month;
        [Range(2025, 2040)] public int Year;
        [Range(0, 23)] public int Hour;

        public DateTime ConvertToDateTime(DateTimeKind kind)
        {
            if (IsValid())
                return new(Year, Month, Day, Hour, 0, 0, kind);

            throw new ArgumentException();
        }

        public bool IsValid()
        {
            int maxDaysInMonth = DateTime.DaysInMonth(Year, Month);
            if (Day < 1 || Day > maxDaysInMonth)
            {
                Debug.LogWarning($"Day must be between 1 and {maxDaysInMonth} for {Month}/{Year}. Got: {Day}");
                return false;
            }

            return true;
        }
    }
}