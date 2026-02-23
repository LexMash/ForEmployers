using System;
using System.Linq;
using UnityEngine;

public static class EventSettingsExtension
{
    public static bool IsValid(this EventSettings eventSettings)
    {
        if (eventSettings.StartDate > eventSettings.EndDate)
        {
            Debug.LogWarning($"[{nameof(EventSettings)}] - The event starts later than its end.");
            return false;
        }

        if (eventSettings.StartDate == eventSettings.EndDate)
        {
            Debug.LogWarning($"[{nameof(EventSettings)}] - The event starts date equals end date.");
            return false;
        }

        var allPhasesDuration = eventSettings.Phases.Sum(phase => phase.DurationHours);
        var eventDuration = (eventSettings.EndDate - eventSettings.StartDate).TotalHours;

        if (allPhasesDuration != eventDuration)
        {
            Debug.LogWarning($"[{nameof(EventSettings)}] - The event duration not equals the duration of all its phases. Check the event phase settings.");
            return false;
        }

        if (eventSettings.IsRecurring)
        {
            int interval = eventSettings.RecurrenceSettings.Interval;
            int intervalInHours = eventSettings.RecurrenceSettings.Cycle switch
            {
                RecurrenceCycle.Hourly => interval,
                RecurrenceCycle.Daily => 24 * interval,
                RecurrenceCycle.Weekly => 168 * interval,
                RecurrenceCycle.Monthly => 672 * interval, //min value
                RecurrenceCycle.Yearly => 8760 * interval,
                _ => throw new NotImplementedException(),
            };

            if (eventDuration > intervalInHours)
            {
                Debug.LogWarning($"[{nameof(EventSettings)}] - The event duration exceeds the recurrence interval. Please check the recurrence settings.");
                return false;
            }
        }

        return true;
    }
}