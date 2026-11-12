using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Lyra.Application.Abstractions;
using Lyra.Application.Agendamentos;
using Lyra.Application.Portal;
using Lyra.Infrastructure.Notificacoes;
using Lyra.Infrastructure.Persistence;
using Lyra.Infrastructure.Persistence.Entities;
using Lyra.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Lyra.Infrastructure.Portal;

/// <summary>Assina os códigos de acesso: no banco fica só o hash, que não serve sem a chave.</summary>
public sealed class ChaveCodigos(IConfiguration configuration)
{
    private readonly byte[] chave = new[]
        {
            Environment.GetEnvironmentVariable("JWT_SECRET"),
            configuration["Jwt:Secret"],
        }
        .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) is { } segredo
        ? HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), "lyra-codigos-acesso-v1"u8)
        : RandomNumberGenerator.GetBytes(32);

    public string Hash(string email, string codigo) =>
        Convert.ToBase64String(HMACSHA256.HashData(chave, Encoding.UTF8.GetBytes($"{email}\n{codigo}")));
}

public sealed class PortalService(
    LyraDbContext db,
    IAgendamentoService agendamentos,
    IConfiguracaoEmpresaService configuracao,
    LeitorEmpresa empresas,
    EmailCodigoAcesso emailCodigo,
    ChaveCodigos chave,
    ITokensCliente tokens) : IPortalService
{
    public const int ValidadeCodigoMinutos = 10;
    public const int MaxTentativas = 5;
    public const int MaxCodigosPorEmailHora = 5;
    public const int MaxCodigosPorIpHora = 30;
    private static readonly TimeSpan IntervaloMinimo = TimeSpan.FromSeconds(30);
    public const string HorarioOcupado = "Este horário não está mais livre. Escolha outro.";

    private sealed record Contexto(int EmpresaId, string Endereco, PortalEmpresa Portal, TimeZoneInfo Fuso);

    public async Task<PaginaPortalDto> PaginaAsync(string endereco, CancellationToken ct)
    {
        var c = await ContextoAsync(endereco, ct);
        var (profissionais, servicos) = await VisiveisAsync(c, ct);
        var empresa = await empresas.ObterAsync(c.EmpresaId, ct);
        var termos = (await configuracao.ObterAsync(c.EmpresaId, ct)).Terminologia;
        return new PaginaPortalDto(
            c.Endereco,
            new EmpresaPortalDto(empresa?.Nome ?? "Agendamento online", empresa?.LogoUrl, empresa?.Endereco, empresa?.Telefone, empresa?.Email),
            c.Portal.Apresentacao,
            termos,
            c.Fuso.Id,
            c.Portal.AntecedenciaMinutos,
            c.Portal.JanelaDias,
            servicos,
            profissionais.Select(p => new ProfissionalPortalDto(p.Id, p.Nome, p.Cor)).ToList(),
            c.Portal.PrazoAlteracaoHoras);
    }

    public async Task<HorariosLivresResponse> HorariosAsync(
        string endereco, DateOnly data, IReadOnlyList<int> servicoIds, int? profissionalId, CancellationToken ct)
    {
        var c = await ContextoAsync(endereco, ct);
        var (profissionais, servicos) = await VisiveisAsync(c, ct);
        ExigirServicos(servicoIds, servicos);
        return await GradeAsync(c, profissionais, servicos, data, servicoIds, profissionalId, ignorarId: null, ct);
    }

    /// <summary>Horários livres como a página mostra: só profissionais visíveis, dentro da janela e com a antecedência.</summary>
    private async Task<HorariosLivresResponse> GradeAsync(
        Contexto c, List<Profissional> profissionais, List<ServicoPortalDto> servicos, DateOnly data,
        IReadOnlyList<int> servicoIds, int? profissionalId, int? ignorarId, CancellationToken ct)
    {
        if (profissionalId is int pid && profissionais.All(p => p.Id != pid))
        {
            throw new ArgumentException("Este profissional não atende pelo agendamento online.");
        }

        var duracao = servicos.Where(s => servicoIds.Contains(s.Id)).Sum(s => s.DuracaoMinutos);
        var hoje = Hoje(c);
        if (data < hoje || data > hoje.AddDays(c.Portal.JanelaDias))
        {
            return new HorariosLivresResponse(data, duracao, c.Fuso.Id, []);
        }

        var livres = await agendamentos.HorariosLivresAsync(c.EmpresaId,
            new HorariosLivresRequest(data, servicoIds, profissionalId, ignorarId, c.Portal.PassoMinutos), ct);
        var minimo = DateTime.UtcNow.AddMinutes(c.Portal.AntecedenciaMinutos);
        var visiveis = profissionais.Select(p => p.Id).ToHashSet();
        return livres with
        {
            Profissionais = livres.Profissionais
                .Where(p => visiveis.Contains(p.ProfissionalId))
                .Select(p => p with { Horarios = p.Horarios.Where(h => h.Inicio >= minimo).ToList() })
                .ToList(),
        };
    }

    public async Task PedirCodigoAsync(string endereco, string? email, string? ip, CancellationToken ct)
    {
        var c = await ContextoAsync(endereco, ct);
        var destino = NormalizarEmail(email);
        var agora = DateTime.UtcNow;
        var umaHora = agora.AddHours(-1);

        var recentes = await db.CodigosAcesso.AsNoTracking()
            .Where(x => x.Email == destino && x.DataCriacao > umaHora)
            .Select(x => x.DataCriacao)
            .ToListAsync(ct);
        if (recentes.Count >= MaxCodigosPorEmailHora)
        {
            throw new LimiteExcedidoException("Muitos códigos pedidos para este e-mail. Tente de novo daqui a pouco.");
        }
        if (recentes.Count > 0 && recentes.Max() > agora - IntervaloMinimo)
        {
            throw new LimiteExcedidoException("Aguarde alguns segundos antes de pedir outro código.");
        }
        if (ip is not null
            && await db.CodigosAcesso.CountAsync(x => x.Ip == ip && x.DataCriacao > umaHora, ct) >= MaxCodigosPorIpHora)
        {
            throw new LimiteExcedidoException("Muitos códigos pedidos. Tente de novo daqui a pouco.");
        }

        var codigo = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var registro = new CodigoAcesso
        {
            Email = destino,
            EmpresaId = c.EmpresaId,
            CodigoHash = chave.Hash(destino, codigo),
            Expira = agora.AddMinutes(ValidadeCodigoMinutos),
            Ip = ip,
            DataCriacao = agora,
        };
        db.CodigosAcesso.Add(registro);
        await db.SaveChangesAsync(ct);

        await emailCodigo.EnviarAsync(registro.Id, destino, codigo, c.EmpresaId, ValidadeCodigoMinutos, ct);
    }

    public async Task<SessaoClienteDto> EntrarAsync(string endereco, string? email, string? codigo, CancellationToken ct)
    {
        var c = await ContextoAsync(endereco, ct);
        var destino = NormalizarEmail(email);
        var digitos = new string((codigo ?? "").Where(char.IsAsciiDigit).ToArray());
        if (digitos.Length != 6) throw new ArgumentException("O código tem 6 números.");

        var agora = DateTime.UtcNow;
        var registro = await db.CodigosAcesso
            .Where(x => x.Email == destino && x.UsadoEm == null && x.Expira > agora)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
        if (registro is null || registro.Tentativas >= MaxTentativas)
        {
            throw new ArgumentException("Código vencido ou já usado. Peça um novo.");
        }

        var esperado = Convert.FromBase64String(registro.CodigoHash);
        var recebido = Convert.FromBase64String(chave.Hash(destino, digitos));
        if (!CryptographicOperations.FixedTimeEquals(esperado, recebido))
        {
            registro.Tentativas++;
            await db.SaveChangesAsync(ct);
            throw new ArgumentException(registro.Tentativas >= MaxTentativas
                ? "Código incorreto. As tentativas acabaram: peça um novo."
                : "Código incorreto. Confira o e-mail e tente de novo.");
        }

        registro.UsadoEm = agora;
        var conta = await db.ContasCliente.FirstOrDefaultAsync(x => x.Email == destino, ct);
        if (conta is null)
        {
            conta = new ContaCliente { Email = destino, DataCriacao = agora };
            db.ContasCliente.Add(conta);
        }
        conta.UltimoAcesso = agora;
        if (conta.Nome is null || conta.Telefone is null)
        {
            var ficha = await db.Clientes.AsNoTracking()
                .Where(x => x.EmpresaId == c.EmpresaId && x.Email == destino && x.Ativo)
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync(ct);
            if (ficha is not null)
            {
                conta.Nome ??= ficha.Nome;
                conta.Telefone ??= ficha.Telefone;
            }
        }
        await db.SaveChangesAsync(ct);

        var (token, expira) = tokens.Emitir(conta.Id, conta.Email);
        return new SessaoClienteDto(token, expira, Dto(conta));
    }

    public async Task<ContaClienteDto> ContaAsync(int contaId, string endereco, CancellationToken ct)
    {
        await ContextoAsync(endereco, ct);
        return Dto(await ContaAsync(contaId, ct));
    }

    public async Task<ContaClienteDto> SalvarContaAsync(int contaId, string endereco, SalvarContaRequest request, CancellationToken ct)
    {
        await ContextoAsync(endereco, ct);
        var conta = await ContaAsync(contaId, ct);
        var nome = (request.Nome ?? "").Trim();
        if (nome.Length is < 2 or > 120) throw new ArgumentException("Informe seu nome (de 2 a 120 letras).");
        var telefone = new string((request.Telefone ?? "").Where(char.IsAsciiDigit).ToArray());
        if (telefone.Length is < 10 or > 15) throw new ArgumentException("Informe o telefone com DDD.");
        conta.Nome = nome;
        conta.Telefone = telefone;
        await db.SaveChangesAsync(ct);
        return Dto(conta);
    }

    public async Task<AgendamentoPortalDto> AgendarAsync(int contaId, string endereco, AgendarPortalRequest request, CancellationToken ct)
    {
        var c = await ContextoAsync(endereco, ct);
        var conta = await ContaAsync(contaId, ct);
        if (string.IsNullOrWhiteSpace(conta.Nome) || string.IsNullOrWhiteSpace(conta.Telefone))
        {
            throw new ArgumentException("Informe seu nome e telefone antes de agendar.");
        }

        var (profissionais, servicos) = await VisiveisAsync(c, ct);
        var servicoIds = request.ServicoIds ?? [];
        ExigirServicos(servicoIds, servicos);

        var inicio = DatasUtc.Normalizar(request.Inicio);
        var agora = DateTime.UtcNow;
        var dia = DiaPermitido(c, inicio);

        var observacoes = string.IsNullOrWhiteSpace(request.Observacoes) ? null : request.Observacoes.Trim();
        if (observacoes is { Length: > 500 }) throw new ArgumentException("Observações: até 500 caracteres.");

        if (request.ProfissionalId is int escolhido && profissionais.All(p => p.Id != escolhido))
        {
            throw new ArgumentException("Este profissional não atende pelo agendamento online.");
        }
        var profissionalId = await LivreNoHorarioAsync(
            c, profissionais, servicoIds, request.ProfissionalId, dia, inicio, ignorarId: null, ct);

        var ficha = await FichaAsync(c, conta, ct);
        var abertos = await db.Agendamentos.CountAsync(a =>
            a.EmpresaId == c.EmpresaId && a.ClienteId == ficha.Id && a.Inicio > agora
            && (a.Status == StatusAgendamento.Agendado || a.Status == StatusAgendamento.Confirmado), ct);
        if (abertos >= c.Portal.LimiteAbertos)
        {
            throw new InvalidOperationException(c.Portal.LimiteAbertos == 1
                ? "Você já tem um horário marcado. Para marcar outro, fale com a empresa."
                : $"Você já tem {abertos} horários marcados, o máximo pelo agendamento online. Para marcar outro, fale com a empresa.");
        }

        AgendamentoDto criado;
        try
        {
            criado = await agendamentos.CriarAsync(c.EmpresaId,
                new SalvarAgendamentoRequest(ficha.Id, servicoIds, profissionalId, inicio, observacoes),
                ct, FilaNotificacoes.OrigemCliente, conta.Id, c.Portal.ConfirmarAutomatico);
        }
        catch (InvalidOperationException)
        {
            // As mensagens da agenda são para a equipe (podem trazer o motivo de um bloqueio).
            throw new InvalidOperationException(HorarioOcupado);
        }
        return new AgendamentoPortalDto(criado.Id, criado.ProfissionalNome, criado.Servicos.Select(s => s.Nome).ToList(),
            criado.Inicio, criado.Fim, criado.Preco, criado.Status);
    }

    public async Task<MeusAgendamentosDto> MeusAgendamentosAsync(int contaId, string endereco, CancellationToken ct)
    {
        var c = await ContextoAsync(endereco, ct);
        var conta = await ContaAsync(contaId, ct);
        var agora = DateTime.UtcNow;
        var meus = Meus(c, conta);

        var proximos = await Completos(meus)
            .Where(a => a.Fim > agora && (a.Status == StatusAgendamento.Agendado
                || a.Status == StatusAgendamento.Confirmado || a.Status == StatusAgendamento.EmAtendimento))
            .OrderBy(a => a.Inicio)
            .ToListAsync(ct);
        // O cancelado por remarcação não entra: o novo horário já conta a história.
        var historico = await Completos(meus)
            .Where(a => !(a.Fim > agora && (a.Status == StatusAgendamento.Agendado
                || a.Status == StatusAgendamento.Confirmado || a.Status == StatusAgendamento.EmAtendimento)))
            .Where(a => !(a.Status == StatusAgendamento.Cancelado && a.MotivoCancelamento == "Remarcado"
                && db.Agendamentos.Any(n => n.RemarcadoDeId == a.Id)))
            .OrderByDescending(a => a.Inicio)
            .Take(20)
            .ToListAsync(ct);

        var cadeias = await CadeiasAsync(meus, ct);
        return new MeusAgendamentosDto(
            proximos.Select(a => Dto(c, a, cadeias, agora)).ToList(),
            historico.Select(a => Dto(c, a, cadeias, agora)).ToList());
    }

    public async Task<HorariosLivresResponse> HorariosRemarcarAsync(
        int contaId, string endereco, int agendamentoId, DateOnly data, int? profissionalId, CancellationToken ct)
    {
        var c = await ContextoAsync(endereco, ct);
        var conta = await ContaAsync(contaId, ct);
        var atual = await MeuAsync(c, conta, agendamentoId, ct);
        await ExigirPrazoAsync(c, atual, ct);
        var (profissionais, servicos) = await VisiveisAsync(c, ct);
        var servicoIds = ServicosDe(atual);
        ExigirServicosParaRemarcar(servicoIds, servicos);
        return await GradeAsync(c, profissionais, servicos, data, servicoIds, profissionalId, atual.Id, ct);
    }

    public async Task<AgendamentoClienteDto> CancelarAsync(
        int contaId, string endereco, int agendamentoId, CancelarPortalRequest request, CancellationToken ct)
    {
        var c = await ContextoAsync(endereco, ct);
        var conta = await ContaAsync(contaId, ct);
        var atual = await MeuAsync(c, conta, agendamentoId, ct);
        await ExigirPrazoAsync(c, atual, ct);
        var motivo = string.IsNullOrWhiteSpace(request.Motivo) ? null : request.Motivo.Trim();
        if (motivo is { Length: > 200 }) throw new ArgumentException("Motivo: até 200 caracteres.");

        await agendamentos.AlterarStatusAsync(c.EmpresaId, atual.Id, StatusAgendamento.Cancelado, motivo, ct,
            FilaNotificacoes.OrigemCliente);
        return await MeuDtoAsync(c, conta, atual.Id, ct);
    }

    public async Task<AgendamentoClienteDto> RemarcarAsync(
        int contaId, string endereco, int agendamentoId, RemarcarPortalRequest request, CancellationToken ct)
    {
        var c = await ContextoAsync(endereco, ct);
        var conta = await ContaAsync(contaId, ct);
        var atual = await MeuAsync(c, conta, agendamentoId, ct);
        await ExigirPrazoAsync(c, atual, ct);
        var (profissionais, servicos) = await VisiveisAsync(c, ct);
        var servicoIds = ServicosDe(atual);
        ExigirServicosParaRemarcar(servicoIds, servicos);

        var inicio = DatasUtc.Normalizar(request.Inicio);
        if (inicio == atual.Inicio && (request.ProfissionalId is null || request.ProfissionalId == atual.ProfissionalId))
        {
            throw new ArgumentException("Escolha um horário diferente do atual.");
        }
        var dia = DiaPermitido(c, inicio);
        if (request.ProfissionalId is int escolhido && profissionais.All(p => p.Id != escolhido))
        {
            throw new ArgumentException("Este profissional não atende pelo agendamento online.");
        }
        var profissionalId = await LivreNoHorarioAsync(
            c, profissionais, servicoIds, request.ProfissionalId, dia, inicio, atual.Id, ct);

        AgendamentoDto novo;
        try
        {
            novo = await agendamentos.RemarcarAsync(c.EmpresaId, atual.Id,
                new SalvarAgendamentoRequest(atual.ClienteId, servicoIds, profissionalId, inicio, atual.Observacoes),
                ct, FilaNotificacoes.OrigemCliente, c.Portal.ConfirmarAutomatico);
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException(HorarioOcupado);
        }
        return await MeuDtoAsync(c, conta, novo.Id, ct);
    }

    /// <summary>
    /// Os agendamentos que o cliente vê: os que a conta marcou e os das fichas com o e-mail dela (verificado no login).
    /// A ficha achada só pelo telefone não entra, porque o telefone não é verificado.
    /// </summary>
    private IQueryable<Agendamento> Meus(Contexto c, ContaCliente conta) =>
        db.Agendamentos.AsNoTracking().Where(a => a.EmpresaId == c.EmpresaId
            && (a.ContaClienteId == conta.Id || a.Cliente.Email == conta.Email));

    private static IQueryable<Agendamento> Completos(IQueryable<Agendamento> q) =>
        q.Include(a => a.Profissional).Include(a => a.Itens).ThenInclude(i => i.Servico);

    private async Task<Agendamento> MeuAsync(Contexto c, ContaCliente conta, int id, CancellationToken ct) =>
        await Completos(Meus(c, conta)).FirstOrDefaultAsync(a => a.Id == id, ct)
        ?? throw new KeyNotFoundException("Horário não encontrado.");

    private async Task<AgendamentoClienteDto> MeuDtoAsync(Contexto c, ContaCliente conta, int id, CancellationToken ct)
    {
        var a = await MeuAsync(c, conta, id, ct);
        return Dto(c, a, await CadeiasAsync(Meus(c, conta), ct), DateTime.UtcNow);
    }

    /// <summary>Id → primeiro agendamento da cadeia de remarcações (o UID do evento no calendário).</summary>
    private static async Task<Dictionary<int, int>> CadeiasAsync(IQueryable<Agendamento> meus, CancellationToken ct)
    {
        var anteriores = await meus.Select(a => new { a.Id, a.RemarcadoDeId }).ToDictionaryAsync(a => a.Id, a => a.RemarcadoDeId, ct);
        var raizes = new Dictionary<int, int>();
        foreach (var id in anteriores.Keys)
        {
            var raiz = id;
            for (var passos = 0; passos < 50 && anteriores.GetValueOrDefault(raiz) is int anterior; passos++)
            {
                raiz = anterior;
            }
            raizes[id] = raiz;
        }
        return raizes;
    }

    private static List<int> ServicosDe(Agendamento a) => a.Itens.OrderBy(i => i.Ordem).Select(i => i.ServicoId).ToList();

    private static void ExigirServicosParaRemarcar(IReadOnlyList<int> servicoIds, List<ServicoPortalDto> servicos)
    {
        if (servicoIds.Count == 0 || servicoIds.Any(id => servicos.All(s => s.Id != id)))
        {
            throw new InvalidOperationException(
                "Algum serviço deste horário não está mais disponível online. Para remarcar, fale com a empresa.");
        }
    }

    /// <summary>Até quando o cliente cancela ou remarca sozinho; null quando a empresa não deixa ou o horário já começou.</summary>
    private static DateTime? AlterarAte(Contexto c, Agendamento a) =>
        c.Portal.PrazoAlteracaoHoras is int horas
        && a.Status is StatusAgendamento.Agendado or StatusAgendamento.Confirmado
            ? a.Inicio.AddHours(-horas)
            : null;

    private async Task ExigirPrazoAsync(Contexto c, Agendamento a, CancellationToken ct)
    {
        if (a.Status is not (StatusAgendamento.Agendado or StatusAgendamento.Confirmado) || a.Inicio <= DateTime.UtcNow)
        {
            throw new InvalidOperationException("Este horário não pode mais ser alterado.");
        }
        if (AlterarAte(c, a) is DateTime ate && DateTime.UtcNow <= ate) return;

        var telefone = (await empresas.ObterAsync(c.EmpresaId, ct))?.Telefone;
        var fale = telefone is null ? "fale com a empresa." : $"fale com a empresa pelo telefone {telefone}.";
        throw new InvalidOperationException(c.Portal.PrazoAlteracaoHoras is int horas
            ? $"Pelo site, só dá para cancelar ou remarcar até {(horas == 1 ? "1 hora" : $"{horas} horas")} antes. Agora, {fale}"
            : $"Para cancelar ou remarcar, {fale}");
    }

    private static AgendamentoClienteDto Dto(Contexto c, Agendamento a, Dictionary<int, int> cadeias, DateTime agora)
    {
        var ate = AlterarAte(c, a);
        var itens = a.Itens.OrderBy(i => i.Ordem).ToList();
        return new AgendamentoClienteDto(
            a.Id,
            cadeias.GetValueOrDefault(a.Id, a.Id),
            a.Inicio,
            a.Fim,
            a.Status,
            a.ProfissionalId,
            a.Profissional.Nome,
            itens.Select(i => new ItemPortalDto(i.ServicoId, i.Servico.Nome)).ToList(),
            itens.Sum(i => i.Preco),
            a.MotivoCancelamento,
            ate is DateTime limite && agora <= limite && a.Inicio > agora,
            ate);
    }

    /// <summary>O dia do horário, se ele respeita a antecedência e a janela da página.</summary>
    private static DateOnly DiaPermitido(Contexto c, DateTime inicio)
    {
        if (inicio < DateTime.UtcNow.AddMinutes(c.Portal.AntecedenciaMinutos))
        {
            throw new InvalidOperationException(c.Portal.AntecedenciaMinutos > 0
                ? $"Pelo agendamento online, só dá para marcar com {Antecedencia(c.Portal.AntecedenciaMinutos)} de antecedência."
                : "Este horário já passou. Escolha outro.");
        }
        var dia = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(inicio, c.Fuso));
        if (dia > Hoje(c).AddDays(c.Portal.JanelaDias))
        {
            throw new ArgumentException($"Escolha um dia dentro dos próximos {c.Portal.JanelaDias} dias.");
        }
        return dia;
    }

    private async Task<Contexto> ContextoAsync(string endereco, CancellationToken ct)
    {
        var e = (endereco ?? "").Trim().ToLowerInvariant();
        var config = e.Length == 0
            ? null
            : await db.EmpresaConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.PortalEndereco == e, ct);
        var portal = PortalEmpresa.Ler(config?.Portal);
        if (config is null || !portal.Ativo) throw new KeyNotFoundException("Página não encontrada.");
        return new Contexto(config.EmpresaId, e, portal, FusoHorarioEmpresa.Resolver(config.FusoHorario));
    }

    /// <summary>
    /// Profissionais ativos, com expediente e não ocultos; serviços ativos, não ocultos, que algum deles atende.
    /// </summary>
    private async Task<(List<Profissional> Profissionais, List<ServicoPortalDto> Servicos)> VisiveisAsync(Contexto c, CancellationToken ct)
    {
        var profissionais = (await db.Profissionais.AsNoTracking()
                .Where(p => p.EmpresaId == c.EmpresaId && p.Ativo && p.Disponibilidades.Any())
                .OrderBy(p => p.Nome)
                .ToListAsync(ct))
            .Where(p => !c.Portal.ProfissionaisOcultos.Contains(p.Id))
            .ToList();
        var ids = profissionais.Select(p => p.Id).ToList();

        var servicos = (await db.Servicos.AsNoTracking()
                .Include(s => s.Profissionais)
                .Where(s => s.EmpresaId == c.EmpresaId && s.Ativo)
                .OrderBy(s => s.Nome)
                .ToListAsync(ct))
            .Where(s => !c.Portal.ServicosOcultos.Contains(s.Id))
            .Select(s => new ServicoPortalDto(s.Id, s.Nome, s.DuracaoMinutos, s.Preco,
                s.Profissionais.Count == 0
                    ? ids
                    : s.Profissionais.Select(v => v.ProfissionalId).Where(ids.Contains).ToList()))
            .Where(s => s.ProfissionalIds.Count > 0)
            .ToList();
        return (profissionais, servicos);
    }

    private static void ExigirServicos(IReadOnlyList<int> servicoIds, List<ServicoPortalDto> servicos)
    {
        if (servicoIds.Count == 0) throw new ArgumentException("Escolha ao menos um serviço.");
        if (servicoIds.Any(id => servicos.All(s => s.Id != id)))
        {
            throw new ArgumentException("Algum dos serviços escolhidos não está disponível online.");
        }
    }

    /// <summary>
    /// O horário tem de estar na mesma grade que a página mostra. Sem preferência, fica com quem tem
    /// menos atendimentos no dia entre os livres.
    /// </summary>
    private async Task<int> LivreNoHorarioAsync(
        Contexto c, List<Profissional> profissionais, IReadOnlyList<int> servicoIds, int? profissionalId,
        DateOnly dia, DateTime inicio, int? ignorarId, CancellationToken ct)
    {
        var livres = await agendamentos.HorariosLivresAsync(c.EmpresaId,
            new HorariosLivresRequest(dia, servicoIds, profissionalId, ignorarId, c.Portal.PassoMinutos), ct);
        var visiveis = profissionais.Select(p => p.Id).ToHashSet();
        var candidatos = livres.Profissionais
            .Where(p => visiveis.Contains(p.ProfissionalId) && p.Horarios.Any(h => h.Inicio == inicio))
            .Select(p => p.ProfissionalId)
            .ToList();
        if (candidatos.Count == 0) throw new InvalidOperationException(HorarioOcupado);
        if (candidatos.Count == 1) return candidatos[0];

        var inicioDia = TimeZoneInfo.ConvertTimeToUtc(dia.ToDateTime(TimeOnly.MinValue), c.Fuso);
        var fimDia = TimeZoneInfo.ConvertTimeToUtc(dia.AddDays(1).ToDateTime(TimeOnly.MinValue), c.Fuso);
        var ocupacao = await db.Agendamentos.AsNoTracking()
            .Where(a => a.EmpresaId == c.EmpresaId && candidatos.Contains(a.ProfissionalId)
                && a.Status != StatusAgendamento.Cancelado && a.Inicio >= inicioDia && a.Inicio < fimDia)
            .GroupBy(a => a.ProfissionalId)
            .Select(g => new { g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        return candidatos.OrderBy(id => ocupacao.GetValueOrDefault(id)).First();
    }

    /// <summary>
    /// A ficha do cliente na empresa: pelo e-mail, senão pelo telefone; sem nenhuma, cria.
    /// Ficha desativada pela empresa não agenda online. A achada pelo telefone não ganha o e-mail da conta:
    /// o telefone não é verificado, e o e-mail daria acesso aos horários da ficha na área do cliente.
    /// </summary>
    private async Task<Cliente> FichaAsync(Contexto c, ContaCliente conta, CancellationToken ct)
    {
        var porEmail = await db.Clientes
            .Where(x => x.EmpresaId == c.EmpresaId && x.Email == conta.Email)
            .OrderByDescending(x => x.Ativo).ThenBy(x => x.Id)
            .FirstOrDefaultAsync(ct);
        var ficha = porEmail ?? await db.Clientes
            .Where(x => x.EmpresaId == c.EmpresaId && x.Telefone == conta.Telefone)
            .OrderByDescending(x => x.Ativo).ThenBy(x => x.Email == null ? 0 : 1).ThenBy(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (ficha is null)
        {
            ficha = new Cliente
            {
                EmpresaId = c.EmpresaId,
                Nome = conta.Nome!,
                Telefone = conta.Telefone!,
                Email = conta.Email,
                Observacoes = "Cadastro feito pelo agendamento online.",
            };
            db.Clientes.Add(ficha);
            await db.SaveChangesAsync(ct);
            return ficha;
        }

        if (!ficha.Ativo)
        {
            var empresa = await empresas.ObterAsync(c.EmpresaId, ct);
            throw new InvalidOperationException(empresa?.Telefone is { } tel
                ? $"Não foi possível agendar online. Fale com a empresa pelo telefone {tel}."
                : "Não foi possível agendar online. Fale com a empresa.");
        }
        return ficha;
    }

    private async Task<ContaCliente> ContaAsync(int contaId, CancellationToken ct) =>
        await db.ContasCliente.FirstOrDefaultAsync(x => x.Id == contaId, ct)
        ?? throw new KeyNotFoundException("Conta não encontrada. Entre de novo.");

    private static DateOnly Hoje(Contexto c) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, c.Fuso));

    private static string NormalizarEmail(string? email)
    {
        var e = (email ?? "").Trim().ToLowerInvariant();
        if (e.Length is 0 or > 160 || e.Contains(' ')
            || !MailAddress.TryCreate(e, out var endereco) || endereco.Address != e || !endereco.Host.Contains('.'))
        {
            throw new ArgumentException("Informe um e-mail válido.");
        }
        return e;
    }

    private static string Antecedencia(int minutos) => minutos switch
    {
        < 60 => $"{minutos} minutos",
        60 => "1 hora",
        < 1440 when minutos % 60 == 0 => $"{minutos / 60} horas",
        1440 => "1 dia",
        _ when minutos % 1440 == 0 => $"{minutos / 1440} dias",
        _ => $"{minutos / 60}h{minutos % 60:D2}",
    };

    private static ContaClienteDto Dto(ContaCliente conta) => new(conta.Email, conta.Nome, conta.Telefone);
}
