using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Lyra.Tests.Infra;
using static Lyra.Tests.Infra.Cenario;

namespace Lyra.Tests;

[Collection(ColecaoApi.Nome)]
public sealed class PortalTests(ApiFixture api)
{
    private sealed record Portal(Cenario C, string Endereco, Api Anonimo)
    {
        public string Rota(string resto = "") => $"/api/salao/publico/portal/{Endereco}{resto}";
    }

    private async Task<Portal> Ligar(
        Cenario? cenario = null, int antecedencia = 120, int janela = 60, int limite = 2,
        int[]? servicosOcultos = null, int[]? profissionaisOcultos = null, int? prazo = 24, bool confirmar = false)
    {
        var c = cenario ?? await Cenario.Criar(api);
        var endereco = $"teste-{c.EmpresaId}";
        Exigir(await c.Admin.Put("/api/salao/configuracao/portal", new
        {
            ativo = true,
            endereco,
            apresentacao = "Bem-vindo!",
            antecedenciaMinutos = antecedencia,
            janelaDias = janela,
            limiteAbertos = limite,
            passoMinutos = 30,
            servicosOcultos = servicosOcultos ?? [],
            profissionaisOcultos = profissionaisOcultos ?? [],
            prazoAlteracaoHoras = prazo,
            confirmarAutomatico = confirmar,
        }), 200);
        return new Portal(c, endereco, new Api(c.Http, ""));
    }

    private static string NovoEmail() => $"cliente-{Guid.NewGuid():N}"[..24] + "@teste.com.br";

    private string UltimoCodigo(string email)
    {
        var pedidos = api.Mensageria.Para(email);
        Assert.NotEmpty(pedidos);
        return pedidos[^1].Campo("assunto")[..6];
    }

    /// <summary>Pede o código, lê no e-mail que a Mensageria recebeu e entra. Devolve a API com o token do cliente.</summary>
    private async Task<Api> Entrar(Portal p, string email, string? nome = "Cliente Online", string? telefone = null)
    {
        Exigir(await p.Anonimo.Post(p.Rota("/codigo"), new { email }), 204);
        var sessao = Exigir(await p.Anonimo.Post(p.Rota("/entrar"), new { email, codigo = UltimoCodigo(email) }), 200);
        var cliente = new Api(p.C.Http, sessao.Json.Str("token"));
        if (nome is not null)
        {
            telefone ??= "119" + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);
            Exigir(await cliente.Put(p.Rota("/conta"), new { nome, telefone }), 200);
        }
        return cliente;
    }

    private static object Pedido(int[] servicos, int? profissional, DateOnly dia, string hora) =>
        new { servicoIds = servicos, profissionalId = profissional, inicio = Utc(dia, hora) };

    [Fato]
    public async Task Pagina_so_existe_ligada_e_mostra_so_o_que_esta_visivel()
    {
        var c = await Cenario.Criar(api);
        var anonimo = new Api(c.Http, "");
        Assert.Equal(404, (await anonimo.Get($"/api/salao/publico/portal/teste-{c.EmpresaId}")).Codigo);

        var p = await Ligar(c, profissionaisOcultos: [c.Bruno]);
        var pagina = Exigir(await p.Anonimo.Get(p.Rota()), 200).Json;
        Assert.Equal("Bem-vindo!", pagina.Str("apresentacao"));
        Assert.Equal([c.Ana], pagina.GetProperty("profissionais").EnumerateArray().Select(x => x.Int("id")));
        var servicos = pagina.GetProperty("servicos").EnumerateArray().ToDictionary(x => x.Int("id"));
        // Avaliação só o Bruno faz, e ele está oculto.
        Assert.Equal([c.Consulta, c.Retorno], servicos.Keys.Order());
        Assert.Equal([c.Ana], servicos[c.Retorno].GetProperty("profissionalIds").EnumerateArray().Select(x => x.GetInt32()));

        var horarios = Exigir(await p.Anonimo.Get(p.Rota($"/horarios?data={Dia(c.Segunda)}&servicoIds={c.Retorno}")), 200).Json;
        Assert.Equal([c.Ana], horarios.GetProperty("profissionais").EnumerateArray().Select(x => x.Int("profissionalId")));
        Assert.Equal(400, (await p.Anonimo.Get(p.Rota($"/horarios?data={Dia(c.Segunda)}&servicoIds={c.Avaliacao}"))).Codigo);

        // Desligado, some de novo.
        var config = Exigir(await c.Admin.Get("/api/salao/configuracao/portal"), 200).Json.GetProperty("portal");
        var desligado = JsonSerializer.Deserialize<Dictionary<string, object>>(config.GetRawText())!;
        desligado["ativo"] = false;
        Exigir(await c.Admin.Put("/api/salao/configuracao/portal", desligado), 200);
        Assert.Equal(404, (await p.Anonimo.Get(p.Rota())).Codigo);
    }

    [Fato]
    public async Task Endereco_reservado_invalido_ou_de_outra_empresa_e_recusado()
    {
        var p = await Ligar();
        var outra = await Cenario.Criar(api);
        object Config(string endereco) => new
        {
            ativo = true, endereco, antecedenciaMinutos = 0, janelaDias = 30, limiteAbertos = 1, passoMinutos = 15,
            servicosOcultos = Array.Empty<int>(), profissionaisOcultos = Array.Empty<int>(),
        };

        Assert.Equal(400, (await outra.Admin.Put("/api/salao/configuracao/portal", Config("agenda"))).Codigo);
        Assert.Equal(400, (await outra.Admin.Put("/api/salao/configuracao/portal", Config("-ruim"))).Codigo);
        Assert.Equal(409, (await outra.Admin.Put("/api/salao/configuracao/portal", Config(p.Endereco.ToUpperInvariant()))).Codigo);

        var disp = Exigir(await outra.Admin.Get($"/api/salao/configuracao/portal/disponivel?endereco={p.Endereco}"), 200).Json;
        Assert.False(disp.GetProperty("disponivel").GetBoolean());
        disp = Exigir(await p.C.Admin.Get($"/api/salao/configuracao/portal/disponivel?endereco={p.Endereco}"), 200).Json;
        Assert.True(disp.GetProperty("disponivel").GetBoolean());

        // Recepção não mexe na configuração.
        Assert.Equal(403, (await p.C.Recepcao.Get("/api/salao/configuracao/portal")).Codigo);
    }

    [Fato]
    public async Task Sugere_o_endereco_pelo_nome_da_empresa()
    {
        var c = await Cenario.Criar(api);
        await api.ExecutarNoBanco("INSERT INTO core.empresas (id, razao_social, nome_fantasia) VALUES ($1, 'Razão', $2)",
            c.EmpresaId, $"Salão Ébano & Cia {c.EmpresaId}");
        var r = Exigir(await c.Admin.Get("/api/salao/configuracao/portal"), 200).Json;
        Assert.Equal($"salao-ebano-cia-{c.EmpresaId}", r.Str("sugestao"));
        Assert.False(r.GetProperty("portal").GetProperty("ativo").GetBoolean());
        Assert.Equal(["Avaliação", "Consulta", "Retorno"], r.GetProperty("servicos").EnumerateArray().Select(x => x.Str("nome")));
        Assert.Equal(["Ana", "Bruno"], r.GetProperty("profissionais").EnumerateArray().Select(x => x.Str("nome")));
    }

    [Fato]
    public async Task Codigo_por_email_entra_e_guarda_o_cadastro()
    {
        var p = await Ligar();
        var email = NovoEmail();
        Exigir(await p.Anonimo.Post(p.Rota("/codigo"), new { email = email.ToUpperInvariant() }), 204);

        var pedido = Assert.Single(api.Mensageria.Para(email));
        Assert.True(pedido.Corpo.GetProperty("transacional").GetBoolean());
        Assert.StartsWith("lyra-acesso-", pedido.Chave);
        Assert.Equal("LYR", pedido.Sistema);
        Assert.Equal(p.C.EmpresaId.ToString(), pedido.Empresa);
        var codigo = UltimoCodigo(email);
        Assert.Contains(codigo, pedido.Campo("texto"));

        var errado = codigo == "000000" ? "111111" : "000000";
        Assert.Equal(400, (await p.Anonimo.Post(p.Rota("/entrar"), new { email, codigo = errado })).Codigo);
        var sessao = Exigir(await p.Anonimo.Post(p.Rota("/entrar"), new { email, codigo }), 200).Json;
        Assert.Equal(email, sessao.GetProperty("conta").Str("email"));
        Assert.True(sessao.GetProperty("conta").Nulo("nome"));
        // Código já usado não serve de novo.
        Assert.Equal(400, (await p.Anonimo.Post(p.Rota("/entrar"), new { email, codigo })).Codigo);

        var cliente = new Api(p.C.Http, sessao.Str("token"));
        Assert.Equal(400, (await cliente.Put(p.Rota("/conta"), new { nome = "Bia", telefone = "123" })).Codigo);
        Exigir(await cliente.Put(p.Rota("/conta"), new { nome = " Bia Souza ", telefone = "(11) 98888-7777" }), 200);
        var conta = Exigir(await cliente.Get(p.Rota("/conta")), 200).Json;
        Assert.Equal("Bia Souza", conta.Str("nome"));
        Assert.Equal("11988887777", conta.Str("telefone"));

        Assert.Equal(401, (await p.Anonimo.Get(p.Rota("/conta"))).Codigo);
    }

    [Fato]
    public async Task Tentativas_erradas_queimam_o_codigo()
    {
        var p = await Ligar();
        var email = NovoEmail();
        Exigir(await p.Anonimo.Post(p.Rota("/codigo"), new { email }), 204);
        var codigo = UltimoCodigo(email);
        var errado = codigo == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(400, (await p.Anonimo.Post(p.Rota("/entrar"), new { email, codigo = errado })).Codigo);
        }
        Assert.Equal(400, (await p.Anonimo.Post(p.Rota("/entrar"), new { email, codigo })).Codigo);
    }

    [Fato]
    public async Task Pedir_codigo_demais_da_429()
    {
        var p = await Ligar();
        var email = NovoEmail();
        Exigir(await p.Anonimo.Post(p.Rota("/codigo"), new { email }), 204);
        var logo = await p.Anonimo.Post(p.Rota("/codigo"), new { email });
        Assert.Equal(429, logo.Codigo);

        var outro = NovoEmail();
        for (var i = 0; i < 5; i++)
        {
            await api.ExecutarNoBanco("""
                INSERT INTO lyra.codigos_acesso (email, empresa_id, codigo_hash, expira, tentativas, data_criacao)
                VALUES ($1, $2, 'x', now(), 0, now() - interval '10 minutes')
                """, outro, p.C.EmpresaId);
        }
        var r = await p.Anonimo.Post(p.Rota("/codigo"), new { email = outro });
        Assert.Equal(429, r.Codigo);
        Assert.Contains("Muitos", r.Mensagem);

        // Muitos pedidos do mesmo IP, com e-mails diferentes.
        var ip = $"10.9.{Random.Shared.Next(0, 255)}.{Random.Shared.Next(1, 255)}";
        var codigos = 0;
        for (var i = 0; i < 31; i++)
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Post, p.Rota("/codigo"))
            {
                Content = JsonContent.Create(new { email = NovoEmail() }),
            };
            pedido.Headers.Add("X-Forwarded-For", $"{ip}, 172.18.0.1");
            using var resposta = await p.C.Http.SendAsync(pedido);
            if ((int)resposta.StatusCode == 204) codigos++;
            else Assert.Equal(429, (int)resposta.StatusCode);
        }
        Assert.Equal(30, codigos);

        Assert.Equal(400, (await p.Anonimo.Post(p.Rota("/codigo"), new { email = "nao-e-email" })).Codigo);
        Assert.Equal(404, (await p.Anonimo.Post("/api/salao/publico/portal/nao-existe-mesmo/codigo", new { email })).Codigo);
    }

    [Fato]
    public async Task Email_devolvido_avisa_para_usar_outro()
    {
        var p = await Ligar();
        var email = NovoEmail();
        api.Mensageria.Descadastrados[email] = 1;
        var r = await p.Anonimo.Post(p.Rota("/codigo"), new { email });
        Assert.Equal(409, r.Codigo);
        Assert.Contains("outro e-mail", r.Mensagem);
    }

    [Fato]
    public async Task Agendar_cria_a_ficha_e_avisa_a_equipe()
    {
        var p = await Ligar();
        var email = NovoEmail();
        var cliente = await Entrar(p, email, "Carla Online", "11977776666");

        var r = Exigir(await cliente.Post(p.Rota("/agendamentos"), Pedido([p.C.Consulta], p.C.Ana, p.C.Segunda, "10:00")), 201).Json;
        Assert.Equal("Ana", r.Str("profissional"));
        Assert.Equal("agendado", r.Str("status"));

        var ag = Exigir(await p.C.Admin.Get($"/api/salao/agendamentos/{r.Int("id")}"), 200).Json;
        Assert.Equal("Carla Online", ag.Str("clienteNome"));
        var ficha = Exigir(await p.C.Admin.Get($"/api/salao/clientes/{ag.Int("clienteId")}"), 200).Json;
        Assert.Equal(email, ficha.Str("email"));
        Assert.Equal("11977776666", ficha.Str("telefone"));

        // O sino mostra a todos da equipe, com a origem para o "online".
        await api.ProcessarFila();
        var sino = Exigir(await p.C.Admin.Get("/api/salao/painel/notificacoes"), 200).Json;
        var aviso = Assert.Single(sino.GetProperty("itens").EnumerateArray());
        Assert.Equal("cliente", aviso.Str("origem"));
        Assert.Equal("Carla Online", aviso.Str("cliente"));

        // O segundo agendamento usa a mesma ficha.
        var r2 = Exigir(await cliente.Post(p.Rota("/agendamentos"), Pedido([p.C.Retorno], p.C.Ana, p.C.Terca, "10:00")), 201).Json;
        var ag2 = Exigir(await p.C.Admin.Get($"/api/salao/agendamentos/{r2.Int("id")}"), 200).Json;
        Assert.Equal(ag.Int("clienteId"), ag2.Int("clienteId"));

        // Horário que já não está livre.
        var outro = await Entrar(p, NovoEmail());
        Assert.Equal(409, (await outro.Post(p.Rota("/agendamentos"), Pedido([p.C.Consulta], p.C.Ana, p.C.Segunda, "10:00"))).Codigo);
    }

    [Fato]
    public async Task Agendar_liga_a_ficha_que_ja_existe_pelo_telefone_ou_email()
    {
        var p = await Ligar();
        await LigarEmails(p);
        var maria = Exigir(await p.C.Admin.Get($"/api/salao/clientes/{p.C.Maria}"), 200).Json;
        var email = NovoEmail();
        var cliente = await Entrar(p, email, "Maria pelo site", maria.Str("telefone"));
        var r = Exigir(await cliente.Post(p.Rota("/agendamentos"), Pedido([p.C.Retorno], null, p.C.Segunda, "09:00")), 201).Json;
        var ag = Exigir(await p.C.Admin.Get($"/api/salao/agendamentos/{r.Int("id")}"), 200).Json;
        Assert.Equal(p.C.Maria, ag.Int("clienteId"));
        // O telefone não é verificado: a ficha não ganha o e-mail da conta, mas o aviso vai para ele.
        maria = Exigir(await p.C.Admin.Get($"/api/salao/clientes/{p.C.Maria}"), 200).Json;
        Assert.True(maria.Nulo("email"));
        Assert.Equal("Maria", maria.Str("nome"));
        await api.ProcessarFila();
        Assert.Contains(api.Mensageria.Para(email), m => m.Campo("assunto").StartsWith("Horário marcado"));

        // Pelo e-mail vale mesmo com outro telefone na conta.
        var emailPaula = NovoEmail();
        var paula = Exigir(await p.C.Admin.Post("/api/salao/clientes",
            new { nome = "Paula", telefone = "11944443333", email = emailPaula, ativo = true }), 201).Id;
        var outra = await Entrar(p, emailPaula, "Paula Nova", "11922221111");
        r = Exigir(await outra.Post(p.Rota("/agendamentos"), Pedido([p.C.Retorno], null, p.C.Segunda, "11:00")), 201).Json;
        ag = Exigir(await p.C.Admin.Get($"/api/salao/agendamentos/{r.Int("id")}"), 200).Json;
        Assert.Equal(paula, ag.Int("clienteId"));
    }

    [Fato]
    public async Task Conta_nova_vem_com_nome_e_telefone_da_ficha_pelo_email()
    {
        var p = await Ligar();
        var email = NovoEmail();
        Exigir(await p.C.Admin.Post("/api/salao/clientes", new { nome = "Paula Ficha", telefone = "11955554444", email, ativo = true }), 201);
        var cliente = await Entrar(p, email, nome: null);
        var conta = Exigir(await cliente.Get(p.Rota("/conta")), 200).Json;
        Assert.Equal("Paula Ficha", conta.Str("nome"));
        Assert.Equal("11955554444", conta.Str("telefone"));
    }

    [Fato]
    public async Task Ficha_desativada_nao_agenda_online()
    {
        var p = await Ligar();
        var maria = Exigir(await p.C.Admin.Get($"/api/salao/clientes/{p.C.Maria}"), 200).Json;
        Exigir(await p.C.Admin.Patch($"/api/salao/clientes/{p.C.Maria}/ativo", new { ativo = false }), 200);
        var cliente = await Entrar(p, NovoEmail(), "Maria", maria.Str("telefone"));
        var r = await cliente.Post(p.Rota("/agendamentos"), Pedido([p.C.Retorno], p.C.Ana, p.C.Segunda, "09:00"));
        Assert.Equal(409, r.Codigo);
        Assert.Contains("Fale com a empresa", r.Mensagem);
    }

    [Fato]
    public async Task Sem_preferencia_fica_com_quem_esta_livre_e_menos_ocupado()
    {
        var p = await Ligar();
        await p.C.AgendarOk(p.C.Maria, p.C.Ana, [p.C.Retorno], p.C.Segunda, "10:00");
        var cliente = await Entrar(p, NovoEmail());
        var r = Exigir(await cliente.Post(p.Rota("/agendamentos"), Pedido([p.C.Retorno], null, p.C.Segunda, "10:00")), 201).Json;
        Assert.Equal("Bruno", r.Str("profissional"));

        // Os dois livres às 14h: vai para quem tem menos no dia (Ana com 1, Bruno com 2).
        await p.C.AgendarOk(p.C.Joao, p.C.Bruno, [p.C.Retorno], p.C.Segunda, "15:00");
        var outro = await Entrar(p, NovoEmail());
        r = Exigir(await outro.Post(p.Rota("/agendamentos"), Pedido([p.C.Retorno], null, p.C.Segunda, "14:00")), 201).Json;
        Assert.Equal("Ana", r.Str("profissional"));
    }

    [Fato]
    public async Task Regras_de_antecedencia_janela_limite_e_ocultos()
    {
        var c = await Cenario.Criar(api);
        var p = await Ligar(c, limite: 1, servicosOcultos: [c.Consulta]);
        var cliente = await Entrar(p, NovoEmail());

        Assert.Equal(400, (await cliente.Post(p.Rota("/agendamentos"), Pedido([c.Consulta], c.Ana, c.Segunda, "10:00"))).Codigo);
        Exigir(await cliente.Post(p.Rota("/agendamentos"), Pedido([c.Retorno], c.Ana, c.Segunda, "10:00")), 201);
        var limite = await cliente.Post(p.Rota("/agendamentos"), Pedido([c.Retorno], c.Ana, c.Terca, "10:00"));
        Assert.Equal(409, limite.Codigo);
        Assert.Contains("já tem um horário", limite.Mensagem);

        // Janela de 3 dias: a segunda que vem (7+ dias) fica de fora.
        p = await Ligar(c, janela: 3);
        var horarios = Exigir(await p.Anonimo.Get(p.Rota($"/horarios?data={Dia(c.Terca)}&servicoIds={c.Retorno}")), 200).Json;
        Assert.Equal(0, horarios.GetProperty("profissionais").GetArrayLength());
        var outro = await Entrar(p, NovoEmail());
        Assert.Equal(400, (await outro.Post(p.Rota("/agendamentos"), Pedido([c.Retorno], c.Ana, c.Terca, "11:00"))).Codigo);

        // Antecedência de 7 dias: amanhã não dá.
        p = await Ligar(c, antecedencia: 10080);
        var amanha = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var cedo = await outro.Post(p.Rota("/agendamentos"), Pedido([c.Retorno], c.Bruno, amanha, "12:00"));
        Assert.Equal(409, cedo.Codigo);
        Assert.Contains("7 dias", cedo.Mensagem);
    }

    [Fato]
    public async Task Horario_fora_da_grade_nao_conta_o_motivo_ao_cliente()
    {
        var p = await Ligar();
        Exigir(await p.C.Admin.Post("/api/salao/bloqueios", new
        {
            profissionalId = p.C.Ana, inicio = Utc(p.C.Segunda, "14:00"), fim = Utc(p.C.Segunda, "16:00"), motivo = "Médico da Ana",
        }), 201);
        var cliente = await Entrar(p, NovoEmail());

        var bloqueado = await cliente.Post(p.Rota("/agendamentos"), Pedido([p.C.Retorno], p.C.Ana, p.C.Segunda, "14:30"));
        Assert.Equal(409, bloqueado.Codigo);
        Assert.DoesNotContain("Médico", bloqueado.Mensagem);
        // Fora do passo de 30 minutos da página.
        Assert.Equal(409, (await cliente.Post(p.Rota("/agendamentos"), Pedido([p.C.Retorno], p.C.Ana, p.C.Segunda, "10:10"))).Codigo);
        // Fora do expediente.
        Assert.Equal(409, (await cliente.Post(p.Rota("/agendamentos"), Pedido([p.C.Retorno], p.C.Ana, p.C.Segunda, "20:00"))).Codigo);
    }

    private static async Task LigarEmails(Portal p) => Exigir(await p.C.Admin.Put("/api/salao/configuracao/avisos",
        new { enviarEmails = true, lembretesHoras = new[] { 24 }, resumoDiario = false, eventos = Array.Empty<object>() }), 200);

    private static string Meus(Portal p, string resto = "") => p.Rota("/meus-agendamentos" + resto);

    private static int[] Ids(JsonElement lista, string nome) =>
        lista.GetProperty(nome).EnumerateArray().Select(x => x.Int("id")).ToArray();

    private static DateTime Instante(string utc) =>
        DateTime.Parse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    private Task MoverParaDaquiA(int agendamento, string intervalo) => api.ExecutarNoBanco(
        $"UPDATE lyra.agendamentos SET inicio = now() + interval '{intervalo}', fim = now() + interval '{intervalo}' + (fim - inicio) WHERE id = $1",
        agendamento);

    [Fato]
    public async Task Area_lista_proximos_e_historico_e_cancela_com_motivo()
    {
        var p = await Ligar();
        await LigarEmails(p);
        var email = NovoEmail();
        var cliente = await Entrar(p, email, "Clara");
        var a1 = Exigir(await cliente.Post(p.Rota("/agendamentos"), Pedido([p.C.Retorno], p.C.Ana, p.C.Segunda, "10:00")), 201).Json.Int("id");
        var a2 = Exigir(await cliente.Post(p.Rota("/agendamentos"), Pedido([p.C.Consulta], p.C.Ana, p.C.Terca, "10:00")), 201).Json.Int("id");
        Assert.Equal(401, (await p.Anonimo.Get(Meus(p))).Codigo);

        var lista = Exigir(await cliente.Get(Meus(p)), 200).Json;
        Assert.Equal([a1, a2], Ids(lista, "proximos"));
        Assert.Empty(Ids(lista, "historico"));
        var primeiro = lista.GetProperty("proximos")[0];
        Assert.True(primeiro.GetProperty("podeAlterar").GetBoolean());
        Assert.Equal(a1, primeiro.Int("cadeia"));
        Assert.Equal("Ana", primeiro.Str("profissional"));
        Assert.Equal(["Retorno"], primeiro.GetProperty("servicos").EnumerateArray().Select(x => x.Str("nome")));
        Assert.Equal(150m, lista.GetProperty("proximos")[1].GetProperty("preco").GetDecimal());

        // Já atendido vai para o histórico e não se altera mais.
        await api.ExecutarNoBanco(
            "UPDATE lyra.agendamentos SET inicio = inicio - interval '30 days', fim = fim - interval '30 days', status = 'concluido' WHERE id = $1", a2);
        Assert.Equal(409, (await cliente.Post(Meus(p, $"/{a2}/cancelar"), new { })).Codigo);

        var cancelado = Exigir(await cliente.Post(Meus(p, $"/{a1}/cancelar"), new { motivo = " Imprevisto " }), 200).Json;
        Assert.Equal("cancelado", cancelado.Str("status"));
        Assert.Equal("Imprevisto", cancelado.Str("motivoCancelamento"));
        Assert.False(cancelado.GetProperty("podeAlterar").GetBoolean());
        Assert.Equal(409, (await cliente.Post(Meus(p, $"/{a1}/cancelar"), new { })).Codigo);

        lista = Exigir(await cliente.Get(Meus(p)), 200).Json;
        Assert.Empty(Ids(lista, "proximos"));
        Assert.Equal([a1, a2], Ids(lista, "historico"));

        // A equipe vê que foi o cliente; o cliente recebe o cancelamento por e-mail.
        await api.ProcessarFila();
        var sino = Exigir(await p.C.Admin.Get("/api/salao/painel/notificacoes"), 200).Json;
        var aviso = sino.GetProperty("itens").EnumerateArray().Single(x => x.Str("evento") == "agendamento_cancelado");
        Assert.Equal("cliente", aviso.Str("origem"));
        Assert.Equal("Clara", aviso.Str("cliente"));
        var email1 = api.Mensageria.Para(email).Single(m => m.Campo("assunto").StartsWith("Horário cancelado"));
        Assert.Contains("Motivo: Imprevisto", email1.Campo("texto"));
    }

    [Fato]
    public async Task Prazo_para_alterar_vale_e_a_empresa_pode_nao_deixar()
    {
        var c = await Cenario.Criar(api);
        var p = await Ligar(c);
        var cliente = await Entrar(p, NovoEmail());
        var id = Exigir(await cliente.Post(p.Rota("/agendamentos"), Pedido([c.Retorno], c.Ana, c.Segunda, "10:00")), 201).Json.Int("id");
        Assert.Equal(24, Exigir(await p.Anonimo.Get(p.Rota()), 200).Json.Int("prazoAlteracaoHoras"));

        // Faltando 3 horas, com prazo de 24: só falando com a empresa.
        await MoverParaDaquiA(id, "3 hours");
        var item = Exigir(await cliente.Get(Meus(p)), 200).Json.GetProperty("proximos")[0];
        Assert.False(item.GetProperty("podeAlterar").GetBoolean());
        Assert.False(item.Nulo("alterarAte"));
        var r = await cliente.Post(Meus(p, $"/{id}/cancelar"), new { });
        Assert.Equal(409, r.Codigo);
        Assert.Contains("24 horas", r.Mensagem);
        Assert.Equal(409, (await cliente.Post(Meus(p, $"/{id}/remarcar"),
            new { profissionalId = c.Ana, inicio = Utc(c.Terca, "10:00") })).Codigo);

        // Prazo 0: até a hora marcada.
        p = await Ligar(c, prazo: 0);
        Assert.True(Exigir(await cliente.Get(Meus(p)), 200).Json.GetProperty("proximos")[0].GetProperty("podeAlterar").GetBoolean());

        // Sem prazo: a empresa não deixa alterar pelo site.
        p = await Ligar(c, prazo: null);
        Assert.True(Exigir(await p.Anonimo.Get(p.Rota()), 200).Json.Nulo("prazoAlteracaoHoras"));
        item = Exigir(await cliente.Get(Meus(p)), 200).Json.GetProperty("proximos")[0];
        Assert.False(item.GetProperty("podeAlterar").GetBoolean());
        Assert.True(item.Nulo("alterarAte"));
        r = await cliente.Post(Meus(p, $"/{id}/cancelar"), new { });
        Assert.Equal(409, r.Codigo);
        Assert.Contains("fale com a empresa", r.Mensagem);

        Assert.Equal(400, (await c.Admin.Put("/api/salao/configuracao/portal", new
        {
            ativo = true, endereco = p.Endereco, antecedenciaMinutos = 0, janelaDias = 30, limiteAbertos = 1, passoMinutos = 15,
            servicosOcultos = Array.Empty<int>(), profissionaisOcultos = Array.Empty<int>(), prazoAlteracaoHoras = 169,
        })).Codigo);
    }

    [Fato]
    public async Task Remarcar_pelo_site_usa_os_mesmos_servicos_e_pode_encostar_no_proprio_horario()
    {
        var p = await Ligar();
        await LigarEmails(p);
        var email = NovoEmail();
        var cliente = await Entrar(p, email, "Rita");
        // Consulta: 30 min + 10 de preparo, então 10:30 cruza com o próprio horário das 10:00.
        var id = Exigir(await cliente.Post(p.Rota("/agendamentos"), Pedido([p.C.Consulta], p.C.Ana, p.C.Segunda, "10:00")), 201).Json.Int("id");

        var grade = Exigir(await cliente.Get(Meus(p, $"/{id}/horarios?data={Dia(p.C.Segunda)}")), 200).Json;
        var horas = grade.GetProperty("profissionais").EnumerateArray().Single(x => x.Int("profissionalId") == p.C.Ana)
            .GetProperty("horarios").EnumerateArray().Select(h => h.GetProperty("inicio").GetDateTime().ToUniversalTime()).ToList();
        Assert.Contains(Instante(Utc(p.C.Segunda, "10:00")), horas);
        Assert.Contains(Instante(Utc(p.C.Segunda, "10:30")), horas);

        Assert.Equal(400, (await cliente.Post(Meus(p, $"/{id}/remarcar"), new { inicio = Utc(p.C.Segunda, "10:00") })).Codigo);
        var novo = Exigir(await cliente.Post(Meus(p, $"/{id}/remarcar"), new { inicio = Utc(p.C.Segunda, "10:30") }), 200).Json;
        Assert.NotEqual(id, novo.Int("id"));
        Assert.Equal(id, novo.Int("cadeia"));
        Assert.Equal(Instante(Utc(p.C.Segunda, "10:30")), novo.GetProperty("inicio").GetDateTime().ToUniversalTime());
        Assert.Equal(["Consulta"], novo.GetProperty("servicos").EnumerateArray().Select(x => x.Str("nome")));

        // O anterior não aparece mais (nem no histórico) e não se remarca de novo.
        var lista = Exigir(await cliente.Get(Meus(p)), 200).Json;
        Assert.Equal([novo.Int("id")], Ids(lista, "proximos"));
        Assert.Empty(Ids(lista, "historico"));
        Assert.Equal(409, (await cliente.Post(Meus(p, $"/{id}/remarcar"), new { inicio = Utc(p.C.Terca, "10:00") })).Codigo);

        // A remarcação herda a conta: o novo também se altera pelo site, na mesma cadeia.
        var terceiro = Exigir(await cliente.Post(Meus(p, $"/{novo.Int("id")}/remarcar"), new { inicio = Utc(p.C.Terca, "11:00") }), 200).Json;
        Assert.Equal(id, terceiro.Int("cadeia"));

        // Horário ocupado por outra pessoa.
        await p.C.AgendarOk(p.C.Joao, p.C.Ana, [p.C.Retorno], p.C.Quarta, "09:00");
        var ocupado = await cliente.Post(Meus(p, $"/{terceiro.Int("id")}/remarcar"), new { profissionalId = p.C.Ana, inicio = Utc(p.C.Quarta, "09:00") });
        Assert.Equal(409, ocupado.Codigo);
        Assert.Equal("Este horário não está mais livre. Escolha outro.", ocupado.Mensagem);

        await api.ProcessarFila();
        var sino = Exigir(await p.C.Admin.Get("/api/salao/painel/notificacoes"), 200).Json;
        Assert.Contains(sino.GetProperty("itens").EnumerateArray(),
            x => x.Str("evento") == "agendamento_remarcado" && x.Str("origem") == "cliente");
        Assert.Contains(api.Mensageria.Para(email), m => m.Campo("assunto").StartsWith("Horário alterado"));
    }

    [Fato]
    public async Task Empresa_pode_fazer_o_online_entrar_confirmado()
    {
        var c = await Cenario.Criar(api);
        var p = await Ligar(c);
        await LigarEmails(p);
        var email = NovoEmail();
        var cliente = await Entrar(p, email, "Lia");
        var aConfirmar = Exigir(await cliente.Post(p.Rota("/agendamentos"), Pedido([c.Retorno], c.Ana, c.Segunda, "09:00")), 201).Json;
        Assert.Equal("agendado", aConfirmar.Str("status"));

        p = await Ligar(c, confirmar: true);
        var portal = Exigir(await c.Admin.Get("/api/salao/configuracao/portal"), 200).Json.GetProperty("portal");
        Assert.True(portal.GetProperty("confirmarAutomatico").GetBoolean());
        var confirmado = Exigir(await cliente.Post(p.Rota("/agendamentos"), Pedido([c.Retorno], c.Ana, c.Terca, "09:00")), 201).Json;
        Assert.Equal("confirmado", confirmado.Str("status"));

        // Remarcar pelo site também entra confirmado; o da equipe continua a confirmar.
        var remarcado = Exigir(await cliente.Post(Meus(p, $"/{aConfirmar.Int("id")}/remarcar"),
            new { inicio = Utc(c.Quarta, "09:00") }), 200).Json;
        Assert.Equal("confirmado", remarcado.Str("status"));
        var daEquipe = await c.AgendarOk(c.Joao, c.Ana, [c.Retorno], c.Quinta, "09:00");
        Assert.Equal("agendado", Exigir(await c.Admin.Get($"/api/salao/agendamentos/{daEquipe}"), 200).Json.Str("status"));

        // O e-mail não pede para confirmar o que já está confirmado.
        await api.ProcessarFila();
        var emails = api.Mensageria.Para(email);
        var marcados = emails.Where(m => m.Campo("assunto").StartsWith("Horário marcado")).ToList();
        Assert.Equal(2, marcados.Count);
        Assert.Contains("/c/", marcados[0].Campo("texto"));
        Assert.DoesNotContain("/c/", marcados[1].Campo("texto"));
        Assert.Contains("marcado e confirmado", marcados[1].Campo("texto"));
        Assert.DoesNotContain("/c/", emails.Single(m => m.Campo("assunto").StartsWith("Horário alterado")).Campo("texto"));
    }

    [Fato]
    public async Task Cliente_liga_e_desliga_os_avisos_por_email_da_empresa()
    {
        var p = await Ligar();
        var email = NovoEmail();
        var cliente = await Entrar(p, email);
        var rota = p.Rota("/conta/preferencias");
        Assert.Equal(401, (await p.Anonimo.Get(rota)).Codigo);

        var pref = Exigir(await cliente.Get(rota), 200).Json;
        Assert.True(pref.GetProperty("disponivel").GetBoolean());
        Assert.False(pref.GetProperty("empresaEnvia").GetBoolean());
        Assert.True(pref.GetProperty("receberEmails").GetBoolean());

        await LigarEmails(p);
        pref = Exigir(await cliente.Put(rota, new { receberEmails = false }), 200).Json;
        Assert.True(pref.GetProperty("empresaEnvia").GetBoolean());
        Assert.False(pref.GetProperty("receberEmails").GetBoolean());
        Assert.True(pref.Nulo("bloqueio"));
        Assert.Contains($"{p.C.EmpresaId}|{email}", api.Mensageria.Saidas.Keys);

        pref = Exigir(await cliente.Put(rota, new { receberEmails = true }), 200).Json;
        Assert.True(pref.GetProperty("receberEmails").GetBoolean());

        // E-mail que voltou: o site mostra o motivo e não desfaz.
        api.Mensageria.Descadastrados.TryAdd(email, 0);
        try
        {
            pref = Exigir(await cliente.Put(rota, new { receberEmails = true }), 200).Json;
            Assert.False(pref.GetProperty("receberEmails").GetBoolean());
            Assert.Equal("devolvido", pref.Str("bloqueio"));
        }
        finally
        {
            api.Mensageria.Descadastrados.TryRemove(email, out _);
        }

        api.Mensageria.MensageriaFora = true;
        try
        {
            Assert.Equal(409, (await cliente.Get(rota)).Codigo);
        }
        finally
        {
            api.Mensageria.MensageriaFora = false;
        }
    }

    [Fato]
    public async Task Area_mostra_so_o_que_e_da_conta_ou_do_email_dela()
    {
        var p = await Ligar();
        var maria = Exigir(await p.C.Admin.Get($"/api/salao/clientes/{p.C.Maria}"), 200).Json;
        var daMaria = await p.C.AgendarOk(p.C.Maria, p.C.Ana, [p.C.Retorno], p.C.Terca, "10:00");

        // Quem entra com o telefone da Maria marca na ficha dela, mas só vê o que marcou.
        var intrusa = await Entrar(p, NovoEmail(), "Outra", maria.Str("telefone"));
        var meu = Exigir(await intrusa.Post(p.Rota("/agendamentos"), Pedido([p.C.Retorno], p.C.Ana, p.C.Segunda, "09:00")), 201).Json.Int("id");
        var lista = Exigir(await intrusa.Get(Meus(p)), 200).Json;
        Assert.Equal([meu], Ids(lista, "proximos"));
        Assert.Equal(404, (await intrusa.Post(Meus(p, $"/{daMaria}/cancelar"), new { })).Codigo);
        Assert.Equal(404, (await intrusa.Get(Meus(p, $"/{daMaria}/horarios?data={Dia(p.C.Segunda)}"))).Codigo);

        // Ficha com o e-mail da conta: o que a equipe marcou também aparece.
        var email = NovoEmail();
        var paula = Exigir(await p.C.Admin.Post("/api/salao/clientes",
            new { nome = "Paula", telefone = "11944443333", email, ativo = true }), 201).Id;
        var daPaula = await p.C.AgendarOk(paula, p.C.Bruno, [p.C.Retorno], p.C.Terca, "11:00");
        var conta = await Entrar(p, email, nome: null);
        Assert.Equal([daPaula], Ids(Exigir(await conta.Get(Meus(p)), 200).Json, "proximos"));

        // Outra conta não alcança.
        var estranha = await Entrar(p, NovoEmail());
        Assert.Empty(Ids(Exigir(await estranha.Get(Meus(p)), 200).Json, "proximos"));
        Assert.Equal(404, (await estranha.Post(Meus(p, $"/{meu}/cancelar"), new { })).Codigo);
        Assert.Equal(401, (await p.C.Admin.Get(Meus(p))).Codigo);
    }

    [Fato]
    public async Task Quem_tem_a_ficha_com_o_email_agenda_para_a_familia_montada_pela_empresa()
    {
        var p = await Ligar(limite: 1);
        await LigarEmails(p);
        var email = NovoEmail();
        var telefone = "119" + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);
        var carla = Exigir(await p.C.Admin.Post("/api/salao/clientes",
            new { nome = "Carla Souza", telefone, email, ativo = true }), 201).Id;
        var pedro = Exigir(await p.C.Admin.Post("/api/salao/clientes",
            new { nome = "Pedro Souza", telefone, parentesco = "filho", familiaDeClienteId = carla, ativo = true }), 201).Id;
        var tia = Exigir(await p.C.Admin.Post("/api/salao/clientes",
            new { nome = "Tia Souza", telefone, familiaDeClienteId = carla, ativo = false }), 201).Id;

        var conta = await Entrar(p, email, nome: null);
        var familia = Exigir(await conta.Get(p.Rota("/conta/familia")), 200).Json;
        Assert.Equal(1, familia.GetArrayLength());
        Assert.Equal(pedro, familia[0].Int("id"));
        Assert.Equal("Pedro Souza", familia[0].Str("nome"));
        Assert.Equal("filho", familia[0].Str("parentesco"));

        var paraPedro = Exigir(await conta.Post(p.Rota("/agendamentos"), new
        {
            servicoIds = new[] { p.C.Retorno }, profissionalId = p.C.Ana, inicio = Utc(p.C.Segunda, "09:00"), clienteId = pedro,
        }), 201).Json;
        Assert.Equal("Pedro Souza", paraPedro.Str("para"));
        Assert.Equal(pedro, Exigir(await p.C.Admin.Get($"/api/salao/agendamentos/{paraPedro.Int("id")}"), 200).Json.Int("clienteId"));

        // O limite de horários abertos vale por pessoa.
        var meu = Exigir(await conta.Post(p.Rota("/agendamentos"), Pedido([p.C.Retorno], p.C.Ana, p.C.Terca, "09:00")), 201).Json;
        Assert.True(meu.Nulo("para"));
        var limite = await conta.Post(p.Rota("/agendamentos"), new
        {
            servicoIds = new[] { p.C.Retorno }, profissionalId = p.C.Ana, inicio = Utc(p.C.Quarta, "09:00"), clienteId = pedro,
        });
        Assert.Equal(409, limite.Codigo);
        Assert.Contains("Pedro Souza já tem", limite.Json.Str("message"));

        // Desativado na família, ou de outra família, não entra.
        foreach (var outro in new[] { tia, p.C.Maria })
        {
            Assert.Equal(404, (await conta.Post(p.Rota("/agendamentos"), new
            {
                servicoIds = new[] { p.C.Retorno }, profissionalId = p.C.Ana, inicio = Utc(p.C.Quinta, "09:00"), clienteId = outro,
            })).Codigo);
        }

        var proximos = Exigir(await conta.Get(Meus(p)), 200).Json.GetProperty("proximos");
        Assert.Equal(2, proximos.GetArrayLength());
        Assert.Equal("Pedro Souza", proximos.EnumerateArray().Single(a => a.Int("id") == paraPedro.Int("id")).Str("para"));
        Assert.True(proximos.EnumerateArray().Single(a => a.Int("id") == meu.Int("id")).Nulo("para"));

        // O aviso do horário do Pedro chega no e-mail da conta que marcou.
        await api.ProcessarFila();
        Assert.Contains(api.Mensageria.Para(email), m => m.Campo("texto").Contains("Olá, Pedro!"));

        // Entrar com o telefone da família, sem o e-mail da ficha, não dá acesso à família.
        var intrusa = await Entrar(p, NovoEmail(), "Outra", telefone);
        Assert.Equal(0, Exigir(await intrusa.Get(p.Rota("/conta/familia")), 200).Json.GetArrayLength());
        Assert.Equal(404, (await intrusa.Post(p.Rota("/agendamentos"), new
        {
            servicoIds = new[] { p.C.Retorno }, profissionalId = p.C.Ana, inicio = Utc(p.C.Quinta, "10:00"), clienteId = pedro,
        })).Codigo);
        Assert.Equal(401, (await p.Anonimo.Get(p.Rota("/conta/familia"))).Codigo);
    }

    [Fato]
    public async Task Token_do_cliente_e_token_da_equipe_nao_se_misturam()
    {
        var p = await Ligar();
        var cliente = await Entrar(p, NovoEmail());

        Assert.Equal(401, (await p.C.Admin.Get(p.Rota("/conta"))).Codigo);
        Assert.Equal(401, (await p.C.Admin.Post(p.Rota("/agendamentos"), Pedido([p.C.Retorno], p.C.Ana, p.C.Segunda, "09:00"))).Codigo);
        Assert.Equal(401, (await cliente.Get("/api/salao/agendamentos/horarios-livres?data=" + Dia(p.C.Segunda))).Codigo);
        Assert.Equal(401, (await cliente.Get("/api/salao/clientes")).Codigo);
    }
}
