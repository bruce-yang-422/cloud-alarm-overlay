using System.ComponentModel;
using System.Reflection;

namespace CloudAlarmOverlay.App.ViewModels;

/// <summary>In-memory, non-secret form drafts. Credentials never belong in this collection.</summary>
public sealed class AdminDraftState
{
    private sealed class Field(int page, object owner, PropertyInfo property)
    {
        public int Page { get; } = page;
        public object Owner { get; } = owner;
        public PropertyInfo Property { get; } = property;
        public object? Saved { get; set; } = property.GetValue(owner);
        public object? Value { get => Property.GetValue(Owner); set => Property.SetValue(Owner, value); }
        public bool Dirty => !Equals(Saved, Value);
    }
    private readonly List<Field> fields = [];
    private readonly HashSet<INotifyPropertyChanged> owners = [];
    private Dictionary<Field, object?>? pending;
    private bool restoring;
    public event Action? Changed;
    public void Track(int page, object owner, params string[] properties)
    {
        foreach (var name in properties)
            fields.Add(new(page, owner, owner.GetType().GetProperty(name) ?? throw new ArgumentException(name)));
        if (owner is INotifyPropertyChanged observable && owners.Add(observable))
            observable.PropertyChanged += (_, _) => { if (!restoring && pending is null) Changed?.Invoke(); };
    }
    public bool IsDirty(int page) => fields.Any(f => f.Page == page && f.Dirty);
    public bool IsDirty(int page, object owner) => fields.Any(f => f.Page == page && ReferenceEquals(f.Owner, owner) && f.Dirty);
    public void Accept(int page)
    {
        foreach (var field in fields.Where(f => f.Page == page)) field.Saved = field.Value;
        Changed?.Invoke();
    }
    public void Discard(int page)
    {
        restoring = true;
        try { foreach (var field in fields.Where(f => f.Page == page)) field.Value = field.Saved; }
        finally { restoring = false; Changed?.Invoke(); }
    }
    public void BeginReload() => pending ??= fields.Where(f => f.Dirty).ToDictionary(f => f, f => f.Value);
    public void EndReload()
    {
        restoring = true;
        try
        {
            foreach (var field in fields)
            {
                // A reload may not own this field (for example the main source form).
                // Do not accidentally accept a draft which the reload never touched.
                if(pending is null || !pending.TryGetValue(field,out var draft) || !Equals(field.Value,draft))
                    field.Saved = field.Value;
            }
            if (pending is not null) foreach (var (field, value) in pending) field.Value = value;
        }
        finally { pending = null; restoring = false; Changed?.Invoke(); }
    }
}
