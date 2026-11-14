using Loot.AI.Providers;

namespace Loot.ViewModels;

public sealed record ModelPickerItem(
    string DisplayName,
    ApiProvider? Provider,
    string? ModelId,
    bool IsGroupHeader)
{
    public bool IsSelectable => !IsGroupHeader;
    public bool IsModel => !IsGroupHeader;

    public static ModelPickerItem Group(ApiProvider provider) =>
        new(provider.Name, provider, null, true);

    public static ModelPickerItem Model(ApiProvider provider, string modelId) =>
        new(modelId, provider, modelId, false);
}
