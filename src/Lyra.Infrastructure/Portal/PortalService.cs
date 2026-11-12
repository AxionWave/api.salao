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
            profissionais.Select(p => new ProfissionalPortalDto(p.Id, p.Nome, p.Cor)).ToList());
    }

    public async Task<HorariosLivresResponse> HorariosAsync(
        string endereco, DateOnly data, IReadOnlyList<int> servicoIds, int? profissionalId, CancellationToken ct)
    {
        var c = await ContextoAsync(endereco, ct);
        var (profissionais, servicos) = await VisiveisAsync(c, ct);
        ExigirServicos(servicoIds, servicos);
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
            new HorariosLivresRequest(data, servicoIds, profissionalId, null, c.Portal.PassoMinutos), ct);
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
        if (inicio < agora.AddMinutes(c.Portal.AntecedenciaMinutos))
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

        var observacoes = string.IsNullOrWhiteSpace(request.Observacoes) ? null : request.Observacoes.Trim();
        if (observacoes is { Length: > 500 }) throw new ArgumentException("Observações: até 500 caracteres.");

        if (request.ProfissionalId is int escolhido && profissionais.All(p => p.Id != escolhido))
        {
            throw new ArgumentException("Este profissional não atende pelo agendamento online.");
        }
        var profissionalId = await LivreNoHorarioAsync(c, profissionais, servicoIds, request.ProfissionalId, dia, inicio, ct);

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
                ct, FilaNotificacoes.OrigemCliente);
        }
        catch (InvalidOperationException)
        {
            // As mensagens da agenda são para a equipe (podem trazer o motivo de um bloqueio).
            throw new InvalidOperationException(HorarioOcupado);
        }
        return new AgendamentoPortalDto(criado.Id, criado.ProfissionalNome, criado.Servicos.Select(s => s.Nome).ToList(),
            criado.Inicio, criado.Fim, criado.Preco, criado.Status);
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
        DateOnly dia, DateTime inicio, CancellationToken ct)
    {
        var livres = await agendamentos.HorariosLivresAsync(c.EmpresaId,
            new HorariosLivresRequest(dia, servicoIds, profissionalId, null, c.Portal.PassoMinutos), ct);
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
    /// Ficha desativada pela empresa não agenda online.
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
        if (ficha.Email is null)
        {
            ficha.Email = conta.Email;
            ficha.DataAtualizacao = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
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
