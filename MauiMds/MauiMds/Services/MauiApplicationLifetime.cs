namespace MauiMds;

internal sealed class MauiApplicationLifetime : IApplicationLifetime
{
    public bool IsTerminating => App.IsTerminating;
}
