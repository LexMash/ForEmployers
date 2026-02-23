using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UniRx;
using UnityEngine;

/// <summary>
/// Сalculates and updates the event state and its phases based on the event configuration
/// </summary>
public class EventService : IDisposable, IEventStatusProvider
{
    private const int SingleOccurrence = 1;

    private readonly EventSettings eventSettings;
    private readonly ITimeProvider timeProvider;
    private readonly ReactiveProperty<EventState> currentState;
    private readonly ReactiveProperty<TimeSpan> timeUpdate;
    private readonly Subject<EventChange> eventChanges;
    private readonly CompositeDisposable subscriptions = new();

    /// <summary>
    /// Cached index to speed up iteration over event phases
    /// </summary>
    private int currentPhaseIndex = 0;

    public IReadOnlyReactiveProperty<EventState> CurrentState => currentState;
    public IObservable<EventChange> EventChanges => eventChanges;

    public IObservable<Unit> OnEventStarted => eventChanges
        .Where(change => change.ChangeType == EventChangeType.EventStarted)
        .AsUnitObservable();

    public IObservable<Unit> OnEventEnded => eventChanges
        .Where(change => change.ChangeType == EventChangeType.EventEnded)
        .AsUnitObservable();

    public IObservable<string> OnPhaseStarted => eventChanges
        .Where(change => change.ChangeType == EventChangeType.PhaseStarted)
        .Select(change => change.PhaseId)
        .DistinctUntilChanged();

    public IObservable<string> OnPhaseEnded => eventChanges
        .Where(change => change.ChangeType == EventChangeType.PhaseEnded)
        .Select(change => change.PhaseId)
        .DistinctUntilChanged();

    public IObservable<PhaseProgressInfo> PhaseProgressUpdates => currentState
        .Where(_ => currentState.Value.IsEventActive && !string.IsNullOrEmpty(currentState.Value.ActivePhaseId))
        .Select(_ => currentState.Value.PhaseProgress)
        .DistinctUntilChanged();

    public IObservable<TimeSpan> EventTimeRemainingUpdates => timeUpdate
        .Where(_ => currentState.Value.IsEventActive)
        .DistinctUntilChanged();

    public IObservable<TimeSpan> EventTimeUntilStartUpdates => timeUpdate
        .Where(_ => !currentState.Value.IsEventActive)
        .DistinctUntilChanged();

    public EventService(EventSettings eventSettings, ITimeProvider timeProvider)
    {
        this.eventSettings = eventSettings != null ? eventSettings : throw new ArgumentNullException(nameof(eventSettings));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider)); ;
#if UNITY_EDITOR
            if (!eventSettings.IsValid())
                throw new ArgumentException($"Invalid settings for event {eventSettings.name}");
#endif
        currentState = new ReactiveProperty<EventState>(new EventState());
        eventChanges = new Subject<EventChange>();
        timeUpdate = new ReactiveProperty<TimeSpan>(TimeSpan.Zero);
    }

    public bool TryInitialize()
    {
        if (!eventSettings.IsEnable)
        {
            Debug.LogWarning($"[{nameof(EventService)}] Unable to initialize event {eventSettings.name}. It is not active.");
            return false;
        }

        UpdateEventState();

        if (IsComplete())
        {
            if (!eventSettings.IsRecurring)
            {
                Debug.Log($"[{nameof(EventService)}] Unable to initialize event {eventSettings.name}. It has completed and will not occur again.");
                return false;
            }

            if (MaxReccuranceReached())
            {
                Debug.Log($"[{nameof(EventService)}] Unable to initialize event {eventSettings.name}. It is complete and will not recur. The maximum number of recurrences {currentState.Value.OccurrenceIndex} has been reached.");
                return false;
            }
        }

        InitializeObservables();
        Debug.Log($"[{nameof(EventService)}] Event service for event {eventSettings.name} initialized.\n TimeMode - {eventSettings.TimeMode}, StartTime {currentState.Value.CurrentStart.ToLocalTime()}, EndTime {currentState.Value.CurrentEnd.ToLocalTime()}, Phase {currentState.Value.ActivePhaseId}");
        return true;
    }

    public bool IsEventActive() => currentState.Value.IsEventActive;
    public bool IsPhaseActive(string phaseId) => currentState.Value.IsEventActive && currentState.Value.ActivePhaseId.Equals(phaseId);
    public PhaseProgressInfo GetPhaseProgress() => currentState.Value.PhaseProgress;
    public TimeSpan GetTimeRemaining() => currentState.Value.CurrentEnd - GetCurrentEventTime();
    public TimeSpan GetTimeUntilStart() => currentState.Value.CurrentStart - GetCurrentEventTime();
    public IObservable<bool> ObserveEventActiveState() => currentState.Select(state => state.IsEventActive).DistinctUntilChanged();
    public IObservable<string> ObserveActivePhase() => currentState.Select(state => state.ActivePhaseId).DistinctUntilChanged();
    public DateTime GetCurrentEventTime() => eventSettings.TimeMode == DateTimeKind.Utc ? timeProvider.Utc : timeProvider.Now;

    public void Dispose()
    {
        subscriptions?.Dispose();
        eventChanges?.OnCompleted();
        eventChanges?.Dispose();
        currentState?.Dispose();
    }

    private void UpdateEventState()
    {
        DateTime currentTime = GetCurrentEventTime();

        if (currentTime < currentState.Value.CurrentStart) //it hasn't started yet
        {
            timeUpdate.Value = GetTimeUntilStart();
            return;
        }

        if (IsComplete() && MaxReccuranceReached())
        {
            subscriptions.Dispose();
            Debug.Log($"[{nameof(EventService)}] Unable to update event {eventSettings.EventType} state. It is complete and will not recur. The maximum number of recurrences {currentState.Value.OccurrenceIndex} has been reached.");
            return;
        }

        EventState previousState = currentState.Value;
        EventState newState = CalculateCurrentState(currentTime);
        List<EventChange> changes = GetChangesBetweenStates(in previousState, in newState);

        currentState.Value = newState;

        foreach (var change in changes)
            eventChanges.OnNext(change);

        timeUpdate.Value = GetTimeRemaining();
    }

    private void InitializeObservables()
    {
        Observable.Interval(TimeSpan.FromSeconds(1))
            .Subscribe(_ => UpdateEventState())
            .AddTo(subscriptions);
    }

    private EventState CalculateCurrentState(DateTime checkTime)
    {
        var state = new EventState();

        if (eventSettings.IsRecurring)
        {
            CalculateCurrentRecurrence(checkTime, out state.CurrentStart, out state.CurrentEnd, out state.OccurrenceIndex);
        }
        else
        {
            state.CurrentStart = eventSettings.StartDate;
            state.CurrentEnd = eventSettings.EndDate;
            state.OccurrenceIndex = SingleOccurrence;
        }

        bool isActive = IsActive(in checkTime, in state);
        state.IsEventActive = isActive;

        if (isActive)
            UpdateActivePhase(ref checkTime, ref state);

        return state;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsActive(in DateTime checkTime, in EventState state) => DateTimeInRange(checkTime, state.CurrentStart, state.CurrentEnd);

    private void CalculateCurrentRecurrence(DateTime checkTime, out DateTime newStart, out DateTime newEnd, out int occurrenceIndex)
    {
        TimeSpan eventDuration = eventSettings.EndDate - eventSettings.StartDate;
        var recurrenceSettings = eventSettings.RecurrenceSettings;

        int maxOccurrences = recurrenceSettings.MaxOccurrences == 0
            ? int.MaxValue
            : recurrenceSettings.MaxOccurrences;

        DateTime startDate = eventSettings.StartDate;
        DateTime endDate = eventSettings.EndDate;

        if (DateTimeInRange(checkTime, startDate, endDate) || checkTime < startDate)
        {
            newStart = startDate;
            newEnd = endDate;
            occurrenceIndex = 1;
            return;
        }

        int estimatedOccurrence = CalculateEstimatedOccurrence(checkTime, startDate, recurrenceSettings);
        estimatedOccurrence = Math.Clamp(estimatedOccurrence, SingleOccurrence, maxOccurrences);

        newStart = CalculateOccurrenceStart(estimatedOccurrence, startDate, recurrenceSettings);
        newEnd = newStart + eventDuration;

        for (occurrenceIndex = estimatedOccurrence; occurrenceIndex <= maxOccurrences; occurrenceIndex++)
        {
            if (DateTimeInRange(checkTime, newStart, newEnd) || checkTime < newStart)
                return;

            newStart = CalculateNextOccurrence(newStart);
            newEnd = newStart + eventDuration;
        }

        occurrenceIndex = maxOccurrences;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool DateTimeInRange(DateTime checkTime, DateTime startRange, DateTime endRange)
        => checkTime >= startRange && checkTime <= endRange;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CalculateEstimatedOccurrence(DateTime checkTime, DateTime startDate, RecurrenceSettings settings)
    {
        TimeSpan timeSinceStart = checkTime - startDate;

        return settings.Cycle switch
        {
            RecurrenceCycle.Daily => (int)(timeSinceStart.TotalDays / settings.Interval) + 1,
            RecurrenceCycle.Weekly => (int)(timeSinceStart.TotalDays / (7 * settings.Interval)) + 1,
            RecurrenceCycle.Monthly => CalculateMonthlyOccurrence(startDate, checkTime, settings.Interval),
            RecurrenceCycle.Yearly => ((checkTime.Year - startDate.Year) / settings.Interval) + 1,
            _ => (int)(timeSinceStart.TotalHours / settings.Interval) + 1
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CalculateMonthlyOccurrence(DateTime startDate, DateTime checkTime, int interval)
    {
        int monthsDifference = ((checkTime.Year - startDate.Year) * 12) + (checkTime.Month - startDate.Month);
        return (monthsDifference / interval) + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private DateTime CalculateOccurrenceStart(int occurrenceIndex, DateTime startDate, RecurrenceSettings settings)
    {
        if (occurrenceIndex == 1) return startDate;

        return settings.Cycle switch
        {
            RecurrenceCycle.Daily => startDate.AddDays((occurrenceIndex - 1) * settings.Interval),
            RecurrenceCycle.Weekly => startDate.AddDays((occurrenceIndex - 1) * 7 * settings.Interval),
            RecurrenceCycle.Monthly => startDate.AddMonths((occurrenceIndex - 1) * settings.Interval),
            RecurrenceCycle.Yearly => startDate.AddYears((occurrenceIndex - 1) * settings.Interval),
            _ => startDate.AddHours((occurrenceIndex - 1) * settings.Interval)
        };
    }

    private DateTime CalculateNextOccurrence(DateTime currentStart)
    {
        var settings = eventSettings.RecurrenceSettings;

        return settings.Cycle switch
        {
            RecurrenceCycle.Daily => currentStart.AddDays(settings.Interval),
            RecurrenceCycle.Weekly => currentStart.AddDays(7 * settings.Interval),
            RecurrenceCycle.Monthly => currentStart.AddMonths(settings.Interval),
            RecurrenceCycle.Yearly => currentStart.AddYears(settings.Interval),
            _ => currentStart.AddHours(settings.Interval),
        };
    }

    private void UpdateActivePhase(ref DateTime checkTime, ref EventState state)
    {
        TimeSpan timeSinceStart = checkTime - state.CurrentStart;
        float totalHoursPassed = (float)timeSinceStart.TotalHours;
        float accumulatedHours = 0;

        state.ActivePhaseId = null;
        state.PhaseProgress = new PhaseProgressInfo(0f, TimeSpan.Zero);
        EventPhase[] phases = eventSettings.Phases;

        for (int i = currentPhaseIndex; i < phases.Length; i++)
        {
            EventPhase phase = phases[i];
            float phaseEndHours = accumulatedHours + phase.DurationHours;
            bool isPhaseActive =
                (totalHoursPassed > accumulatedHours || Mathf.Approximately(accumulatedHours, totalHoursPassed))
                && totalHoursPassed < phaseEndHours;

            if (isPhaseActive)
            {
                state.ActivePhaseId = phase.Id;
                float progress = (totalHoursPassed - accumulatedHours) / phase.DurationHours; //TODO measure the loss of accuracy and its impact
                float hoursRemaining = phaseEndHours - totalHoursPassed;
                state.PhaseProgress = new PhaseProgressInfo(progress, TimeSpan.FromHours(hoursRemaining));
                currentPhaseIndex = i;
                return;
            }

            accumulatedHours = phaseEndHours;
        }

        currentPhaseIndex = 0; //if all phases are completed, then the event is completed - then we reset the index
    }

    private List<EventChange> GetChangesBetweenStates(in EventState previous, in EventState current)
    {
        var changes = new List<EventChange>();

        if (previous.Equals(current))
            return changes;

        if (previous.IsEventActive != current.IsEventActive)
        {
            changes.Add(new EventChange
            {
                ChangeType = current.IsEventActive ? EventChangeType.EventStarted : EventChangeType.EventEnded,
                PhaseId = current.ActivePhaseId
            });
        }

        if (previous.ActivePhaseId != current.ActivePhaseId)
        {
            if (!string.IsNullOrEmpty(previous.ActivePhaseId))
            {
                changes.Add(new EventChange
                {
                    ChangeType = EventChangeType.PhaseEnded,
                    PhaseId = previous.ActivePhaseId
                });
            }

            if (!string.IsNullOrEmpty(current.ActivePhaseId))
            {
                changes.Add(new EventChange
                {
                    ChangeType = EventChangeType.PhaseStarted,
                    PhaseId = current.ActivePhaseId
                });
            }
        }

        return changes;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsComplete() => currentState.Value.CurrentEnd <= GetCurrentEventTime();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MaxReccuranceReached()
    {
        int maxOccurence = eventSettings.RecurrenceSettings.MaxOccurrences;
        if (maxOccurence == 0) return false;
        return currentState.Value.OccurrenceIndex == maxOccurence;
    }
}