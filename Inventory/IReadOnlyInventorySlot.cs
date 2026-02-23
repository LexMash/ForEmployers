using R3;
using System;

public interface IReadOnlyInventorySlot : IDisposable
{
    Id Id { get; }
    ReadOnlyReactiveProperty<int> Amount { get; }
}

