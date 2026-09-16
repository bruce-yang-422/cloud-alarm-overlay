using System.Collections.ObjectModel;
using CloudAlarmOverlay.Core.Repositories;
namespace CloudAlarmOverlay.App.Services;

public sealed class EmojiLibrary(ISettingsRepository settings)
{
    public const string Defaults = "✅\n📦\n⚠️\n🛒\n🚚\n💰\n🧾\n📅\n📞\n⏰\n📌\n👍";
    public ObservableCollection<string> Items { get; } = new(Defaults.Split('\n'));
    public async Task LoadAsync()
    {
        var value = (await settings.GetAsync("EmojiLibrary"))?.Value ?? Defaults;
        Replace(Parse(value));
    }
    public static string[] Parse(string value)
    {
        var items = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToArray();
        if (items.Length > 60 || items.Any(i => i.Length > 24)) throw new ArgumentException("最多 60 個常用項目，每行最多 24 字元。");
        return items;
    }
    public async Task SaveAsync(string value)
    {
        var items = Parse(value);
        await settings.SaveAsync(new() { Key = "EmojiLibrary", Value = string.Join("\n", items) });
        Replace(items);
    }
    private void Replace(IEnumerable<string> values) { Items.Clear(); foreach (var value in values) Items.Add(value); }
}
