using System;
using UnityEngine;

[Serializable]
public class RecurrenceSettings
{
    public RecurrenceCycle Cycle = RecurrenceCycle.Weekly;

    [Header("The value cannot be less than 1")]
    public int Interval = 1;

    [Header("If it is equal to 0 then the occurrences are infinite")]
    [Range(0, 365)] public int MaxOccurrences = 0;
}