using Lyra.Core.Auth;

namespace Lyra.Application.Abstractions;

public interface ICurrentUserAccessor
{
    CurrentUser User { get; }
}
