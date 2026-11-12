using Lyra.Application.Agendamentos;
using Lyra.Application.Configuracao;

namespace Lyra.Application.Portal;

/// <summary>Como a empresa recebe agendamentos online. Editado em Configurações.</summary>
/// <param name="Endereco">O que vem depois de <c>lyra.lumensoft.com.br/</c> (ex.: <c>salao-bela</c>).</param>
/// <param name="Apresentacao">Texto curto no topo da página.</param>
/// <param name="AntecedenciaMinutos">Quanto antes do horário o cliente ainda pode marcar.</param>
/// <param name="JanelaDias">Até quantos dias à frente aparecem horários.</param>
/// <param name="LimiteAbertos">Quantos horários futuros o cliente pode ter marcados ao mesmo tempo.</param>
/// <param name="PassoMinutos">De quanto em quanto tempo os horários aparecem (10, 15, 20, 30 ou 60).</param>
public sealed record PortalConfigDto(
    bool Ativo,
    string? Endereco,
    string? Apresentacao,
    int AntecedenciaMinutos,
    int JanelaDias,
    int LimiteAbertos,
    int PassoMinutos,
    IReadOnlyList<int> ServicosOcultos,
    IReadOnlyList<int> ProfissionaisOcultos);

public sealed record ItemPortalDto(int Id, string Nome);

/// <param name="Sugestao">Endereço sugerido a partir do nome da empresa, quando ainda não há um.</param>
/// <param name="Servicos">Serviços ativos, para escolher os que ficam fora da página.</param>
/// <param name="Profissionais">Profissionais ativos, idem.</param>
public sealed record PortalConfigResponse(
    PortalConfigDto Portal,
    string? Sugestao,
    IReadOnlyList<ItemPortalDto> Servicos,
    IReadOnlyList<ItemPortalDto> Profissionais);

public sealed record EnderecoDisponivelResponse(bool Disponivel, string? Motivo);

public sealed record EmpresaPortalDto(
    string Nome,
    string? LogoUrl,
    string? Endereco,
    string? Telefone,
    string? Email);

/// <param name="ProfissionalIds">Quem atende online. Nunca vazio: serviço sem ninguém visível não aparece.</param>
public sealed record ServicoPortalDto(int Id, string Nome, int DuracaoMinutos, decimal Preco, IReadOnlyList<int> ProfissionalIds);

public sealed record ProfissionalPortalDto(int Id, string Nome, string Cor);

/// <summary>Tudo o que a página pública da empresa mostra, sem login.</summary>
public sealed record PaginaPortalDto(
    string Endereco,
    EmpresaPortalDto Empresa,
    string? Apresentacao,
    TerminologiaDto Termos,
    string FusoHorario,
    int AntecedenciaMinutos,
    int JanelaDias,
    IReadOnlyList<ServicoPortalDto> Servicos,
    IReadOnlyList<ProfissionalPortalDto> Profissionais);

public sealed record PedirCodigoRequest(string Email);

public sealed record EntrarRequest(string Email, string Codigo);

/// <param name="Nome">Nulo enquanto o cliente não completou o cadastro.</param>
public sealed record ContaClienteDto(string Email, string? Nome, string? Telefone);

public sealed record SessaoClienteDto(string Token, DateTime Expira, ContaClienteDto Conta);

public sealed record SalvarContaRequest(string Nome, string Telefone);

/// <param name="ProfissionalId">Nulo = qualquer profissional livre no horário.</param>
public sealed record AgendarPortalRequest(
    IReadOnlyList<int> ServicoIds,
    int? ProfissionalId,
    DateTime Inicio,
    string? Observacoes);

public sealed record AgendamentoPortalDto(
    int Id,
    string Profissional,
    IReadOnlyList<string> Servicos,
    DateTime Inicio,
    DateTime Fim,
    decimal Preco,
    string Status);

/// <summary>Configuração do portal pela equipe.</summary>
public interface IPortalConfigService
{
    Task<PortalConfigResponse> ObterAsync(int empresaId, CancellationToken ct);
    Task<PortalConfigResponse> SalvarAsync(int empresaId, PortalConfigDto request, CancellationToken ct);
    Task<EnderecoDisponivelResponse> DisponivelAsync(int empresaId, string? endereco, CancellationToken ct);
}

/// <summary>O que o cliente faz na página da empresa: ver, entrar com código e agendar.</summary>
public interface IPortalService
{
    Task<PaginaPortalDto> PaginaAsync(string endereco, CancellationToken ct);
    Task<HorariosLivresResponse> HorariosAsync(string endereco, DateOnly data, IReadOnlyList<int> servicoIds, int? profissionalId, CancellationToken ct);
    Task PedirCodigoAsync(string endereco, string? email, string? ip, CancellationToken ct);
    Task<SessaoClienteDto> EntrarAsync(string endereco, string? email, string? codigo, CancellationToken ct);
    Task<ContaClienteDto> ContaAsync(int contaId, string endereco, CancellationToken ct);
    Task<ContaClienteDto> SalvarContaAsync(int contaId, string endereco, SalvarContaRequest request, CancellationToken ct);
    Task<AgendamentoPortalDto> AgendarAsync(int contaId, string endereco, AgendarPortalRequest request, CancellationToken ct);
}

/// <summary>Token do cliente no portal: chave e audiência próprias, não serve nas rotas da equipe.</summary>
public interface ITokensCliente
{
    (string Token, DateTime Expira) Emitir(int contaId, string email);
}

/// <summary>Pedidos demais em pouco tempo (código de acesso): vira 429.</summary>
public sealed class LimiteExcedidoException(string message) : Exception(message);
