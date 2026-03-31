using MauiMds.Models;

namespace MauiMds.Services;

public interface ISessionStateService
{
    SessionState Load();
    void Save(SessionState state);
}
