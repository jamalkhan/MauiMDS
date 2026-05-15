using Rizedown.Models;

namespace Rizedown.Services;

public interface ISessionStateService
{
    SessionState Load();
    void Save(SessionState state);
}
