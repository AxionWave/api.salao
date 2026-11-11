using Lyra.Core.Modules;
using Lyra.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Lyra.Infrastructure.AoVivo;

/// <summary>
/// Canal ao vivo da equipe: só o servidor fala (agenda mudou, aviso novo do sino). Um grupo por empresa,
/// e só entra quem tem o módulo Agenda.
/// </summary>
[Authorize]
public sealed class PainelHub : Hub
{
    public const string Rota = "/api/salao/hub/painel";

    public static string GrupoEmpresa(int empresaId) => $"empresa-{empresaId}";

    public override async Task OnConnectedAsync()
    {
        var usuario = HttpCurrentUserAccessor.FromPrincipal(Context.User);
        if (usuario.EmpresaId is not int empresaId || empresaId <= 0 || !usuario.HasModulo(ModuleCodes.Agenda))
        {
            Context.Abort();
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, GrupoEmpresa(empresaId));
        await base.OnConnectedAsync();
    }
}
