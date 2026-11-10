<#
.SYNOPSIS
    Teste de ponta a ponta da agenda do Lyra pela API (Windows PowerShell 5.1+).

.DESCRIPTION
    Cria dados numa empresa fictícia e confere as regras: expediente com pausa, preparo entre
    atendimentos, quem atende cada serviço, bloqueios, horários livres, status, configuração e
    permissões. Só funciona contra uma API em Development (token sem assinatura).

    Os dados criados ficam na empresa fictícia; os nomes levam um sufixo único por execução.

.EXAMPLE
    .\tests\e2e-agenda.ps1 -Api http://localhost:8092 -EmpresaId 999999
#>
param(
    [string]$Api = 'http://localhost:8092',
    [int]$EmpresaId = 999999
)

$ErrorActionPreference = 'Stop'
$script:ok = 0
$script:falhas = New-Object System.Collections.Generic.List[string]
$sufixo = (Get-Date).ToString('HHmmss')
$script:pessoa = 900000000 + [int](Get-Date).ToString('HHmmss') * 10
function NovaPessoa { $script:pessoa++; $script:pessoa }

function B64Url([string]$s) {
    [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($s)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Token([string[]]$modulos, [bool]$comEmpresa = $true, [bool]$comRoles = $true) {
    $exp = [DateTimeOffset]::UtcNow.AddHours(1).ToUnixTimeSeconds()
    $claims = [ordered]@{ userId = '1'; username = 'e2e'; email = 'e2e@teste'; modulos = $modulos; exp = $exp; iss = 'e2e'; aud = 'e2e' }
    if ($comEmpresa) { $claims.empresaId = "$EmpresaId" }
    if ($comRoles) { $claims.roles = @('USER') }
    $h = B64Url '{"alg":"HS256","typ":"JWT"}'
    $p = B64Url ($claims | ConvertTo-Json -Compress)
    "$h.$p.c2ln"
}

$todos = @('LYRA000000', 'LYR0000001', 'LYR0000002', 'LYR0000003', 'LYR0000004', 'LYR0000005', 'LYR0000006')
$tokenAdmin = Token $todos
$tokenRecepcao = Token @('LYRA000000', 'LYR0000001')

function Chamar([string]$metodo, [string]$rota, $corpo = $null, [string]$token = $tokenAdmin, [hashtable]$extra = @{}) {
    $headers = @{ Authorization = "Bearer $token" } + $extra
    $args = @{ Method = $metodo; Uri = "$Api$rota"; Headers = $headers; UseBasicParsing = $true }
    if ($null -ne $corpo) {
        $args.Body = [Text.Encoding]::UTF8.GetBytes(($corpo | ConvertTo-Json -Depth 6 -Compress))
        $args.ContentType = 'application/json; charset=utf-8'
    }
    try {
        $r = Invoke-WebRequest @args
        $texto = [Text.Encoding]::UTF8.GetString($r.RawContentStream.ToArray())
        return [pscustomobject]@{ Status = [int]$r.StatusCode; Json = $(if ($texto) { $texto | ConvertFrom-Json } else { $null }); Texto = $texto }
    } catch {
        $resp = $_.Exception.Response
        if (-not $resp) { throw }
        $leitor = New-Object IO.StreamReader($resp.GetResponseStream(), [Text.Encoding]::UTF8)
        $texto = $leitor.ReadToEnd()
        $json = $null
        try { $json = $texto | ConvertFrom-Json } catch { }
        return [pscustomobject]@{ Status = [int]$resp.StatusCode; Json = $json; Texto = $texto }
    }
}

function Confere([string]$id, [string]$descricao, [bool]$condicao, [string]$detalhe = '') {
    if ($condicao) {
        $script:ok++
        Write-Host ("  [ok]    {0} {1}" -f $id, $descricao) -ForegroundColor Green
    } else {
        $script:falhas.Add("$id $descricao $detalhe")
        Write-Host ("  [FALHA] {0} {1} {2}" -f $id, $descricao, $detalhe) -ForegroundColor Red
    }
}

function Status([string]$id, [string]$descricao, $resp, [int]$esperado, [string]$trecho = '') {
    $msg = if ($resp.Json -and $resp.Json.message) { $resp.Json.message } else { $resp.Texto }
    $passou = $resp.Status -eq $esperado -and (-not $trecho -or ($msg -like "*$trecho*"))
    Confere $id $descricao $passou "(esperado $esperado '$trecho', veio $($resp.Status): $msg)"
}

# Datas: segunda-feira futura, no fuso da empresa (America/Sao_Paulo, UTC-3).
$hoje = [DateTime]::Today
$segunda = $hoje.AddDays(((8 - [int]$hoje.DayOfWeek) % 7) + 7)
$terca = $segunda.AddDays(1)
$quarta = $segunda.AddDays(2)
$sabado = $segunda.AddDays(5)
function Utc([DateTime]$dia, [string]$hhmm, [int]$offsetHoras = -3) {
    $t = [TimeSpan]::Parse($hhmm)
    ($dia.Date + $t).AddHours(-$offsetHoras).ToString("yyyy-MM-ddTHH:mm:ssZ")
}
function Dia([DateTime]$d) { $d.ToString('yyyy-MM-dd') }
function Horas($resp) { if ($resp.Json -and $resp.Json.profissionais) { @($resp.Json.profissionais[0].horarios | ForEach-Object { $_.hora }) } else { @() } }

Write-Host "Lyra e2e — empresa $EmpresaId, segunda de teste $(Dia $segunda), sufixo $sufixo" -ForegroundColor Cyan

# ---------------------------------------------------------------- Configuração
Write-Host "`nConfiguração da empresa"
$r = Chamar GET '/api/salao/configuracao'
Status 'C-01' 'GET configuração' $r 200
$r = Chamar PUT '/api/salao/configuracao' @{ segmento = 'saude'; fusoHorario = 'America/Sao_Paulo'; terminologia = $null }
Status 'C-02' 'Salvar segmento saúde' $r 200
Confere 'C-02b' 'Terminologia do segmento saúde aplicada' ($r.Json.terminologia.cliente.singular -eq 'Paciente') "(veio $($r.Json.terminologia.cliente.singular))"
$termos = @{
    recurso = @{ singular = 'Médica'; plural = 'Médicas'; genero = 'f' }
    servico = @{ singular = 'Procedimento'; plural = 'Procedimentos'; genero = 'm' }
    cliente = @{ singular = 'Paciente'; plural = 'Pacientes'; genero = 'm' }
    agendamento = @{ singular = 'Consulta'; plural = 'Consultas'; genero = 'f' }
}
$r = Chamar PUT '/api/salao/configuracao' @{ segmento = 'saude'; fusoHorario = 'America/Sao_Paulo'; terminologia = $termos }
Status 'C-03' 'Salvar nomes personalizados' $r 200
Confere 'C-03b' 'Marcada como personalizada' ($r.Json.personalizada -eq $true)
$termos.recurso.singular = ''
$r = Chamar PUT '/api/salao/configuracao' @{ segmento = 'saude'; fusoHorario = 'America/Sao_Paulo'; terminologia = $termos }
Status 'C-04' 'Nome vazio é recusado' $r 400
$termos.recurso.singular = 'X' * 41
$r = Chamar PUT '/api/salao/configuracao' @{ segmento = 'saude'; fusoHorario = 'America/Sao_Paulo'; terminologia = $termos }
Status 'C-05' 'Nome com mais de 40 caracteres é recusado' $r 400
$r = Chamar PUT '/api/salao/configuracao' @{ segmento = 'inexistente'; fusoHorario = 'America/Sao_Paulo' }
Status 'C-05b' 'Segmento inválido é recusado' $r 400
$r = Chamar PUT '/api/salao/configuracao' @{ segmento = 'saude'; fusoHorario = 'Marte/Base' }
Status 'C-05c' 'Fuso inválido é recusado' $r 400
$r = Chamar PUT '/api/salao/configuracao' @{ segmento = 'saude'; fusoHorario = 'America/Sao_Paulo'; terminologia = $null }
Status 'C-06' 'Voltar aos nomes do segmento' $r 200
Confere 'C-06b' 'Deixa de ser personalizada' ($r.Json.personalizada -eq $false)
$r = Chamar GET '/api/salao/configuracao/opcoes'
Status 'C-06c' 'Opções de segmento e fuso' $r 200
Confere 'C-06d' '7 segmentos disponíveis' (@($r.Json.segmentos).Count -eq 7) "(veio $(@($r.Json.segmentos).Count))"

# ---------------------------------------------------------------- Profissionais
Write-Host "`nProfissionais e expediente"
$semanaComPausa = foreach ($d in 1..5) { @{ diaSemana = $d; inicio = '09:00'; fim = '12:00' }; @{ diaSemana = $d; inicio = '13:00'; fim = '18:00' } }
$r = Chamar POST '/api/salao/profissionais' @{ nome = "Ana E2E $sufixo"; pessoaId = (NovaPessoa); cor = 'copper'; ativo = $true; disponibilidades = @($semanaComPausa) }
Status 'PR-01' 'Criar Ana com pausa de almoço' $r 201
$ana = $r.Json
Confere 'PR-02' 'Ana com 10 faixas (2 por dia útil)' (@($ana.disponibilidades).Count -eq 10) "(veio $(@($ana.disponibilidades).Count))"
$r = Chamar POST '/api/salao/profissionais' @{ nome = "Bruno E2E $sufixo"; pessoaId = (NovaPessoa); cor = 'ok'; ativo = $true; disponibilidades = @(foreach ($d in 1..6) { @{ diaSemana = $d; inicio = '08:00'; fim = '17:00' } }) }
Status 'PR-01b' 'Criar Bruno seg–sáb 08–17' $r 201
$bruno = $r.Json
$r = Chamar POST '/api/salao/profissionais' @{ nome = "Sobreposto $sufixo"; pessoaId = (NovaPessoa); ativo = $true; disponibilidades = @(@{ diaSemana = 1; inicio = '09:00'; fim = '12:00' }, @{ diaSemana = 1; inicio = '11:00'; fim = '14:00' }) }
Status 'PR-04' 'Faixas sobrepostas são recusadas' $r 400 'sobrepostas'
$r = Chamar POST '/api/salao/profissionais' @{ nome = "Invertido $sufixo"; pessoaId = (NovaPessoa); ativo = $true; disponibilidades = @(@{ diaSemana = 1; inicio = '12:00'; fim = '09:00' }) }
Status 'PR-05' 'Término antes do início é recusado' $r 400
$r = Chamar POST '/api/salao/profissionais' @{ nome = "Sete faixas $sufixo"; pessoaId = (NovaPessoa); ativo = $true; disponibilidades = @(foreach ($h in 0..6) { @{ diaSemana = 1; inicio = ('{0:00}:00' -f ($h * 2 + 6)); fim = ('{0:00}:30' -f ($h * 2 + 6)) } }) }
Status 'PR-05b' 'Mais de 6 faixas no dia é recusado' $r 400 'no máximo'
$r = Chamar POST '/api/salao/profissionais' @{ nome = "Ana E2E $sufixo"; pessoaId = (NovaPessoa); ativo = $true }
Status 'PR-12' 'Nome repetido é recusado' $r 409

# ---------------------------------------------------------------- Serviços
Write-Host "`nServiços"
$r = Chamar POST '/api/salao/servicos' @{ nome = "Consulta E2E $sufixo"; duracaoMinutos = 30; preco = 150; categoria = 'Clínica'; ativo = $true; intervaloAposMinutos = 10; profissionalIds = @($ana.id) }
Status 'S-01' 'Criar Consulta (30 min, preparo 10, só Ana)' $r 201
$consulta = $r.Json
Confere 'S-01b' 'Consulta vinculada a Ana' ((@($consulta.profissionalIds) -join ',') -eq "$($ana.id)") "(veio $(@($consulta.profissionalIds) -join ','))"
$r = Chamar POST '/api/salao/servicos' @{ nome = "Retorno E2E $sufixo"; duracaoMinutos = 15; preco = 0; ativo = $true; profissionalIds = @() }
Status 'S-02' 'Criar Retorno (15 min, todos)' $r 201
$retorno = $r.Json
$r = Chamar POST '/api/salao/servicos' @{ nome = "Avaliação E2E $sufixo"; duracaoMinutos = 60; preco = 200; ativo = $true; profissionalIds = @($bruno.id) }
Status 'S-03' 'Criar Avaliação (60 min, só Bruno)' $r 201
$avaliacao = $r.Json
$r = Chamar POST '/api/salao/servicos' @{ nome = "Consulta E2E $sufixo"; duracaoMinutos = 30; preco = 1; ativo = $true }
Status 'S-04' 'Nome repetido é recusado' $r 409
$r = Chamar POST '/api/salao/servicos' @{ nome = "Zero $sufixo"; duracaoMinutos = 0; preco = 1; ativo = $true }
Status 'S-05' 'Duração 0 é recusada' $r 400
$r = Chamar POST '/api/salao/servicos' @{ nome = "Preparo longo $sufixo"; duracaoMinutos = 30; preco = 1; ativo = $true; intervaloAposMinutos = 241 }
Status 'S-06' 'Preparo acima de 240 é recusado' $r 400
$r = Chamar POST '/api/salao/servicos' @{ nome = "Outro dono $sufixo"; duracaoMinutos = 30; preco = 1; ativo = $true; profissionalIds = @(1) }
Status 'S-06b' 'Profissional de outra empresa é recusado' $r 400
$r = Chamar PUT "/api/salao/servicos/$($consulta.id)" @{ nome = $consulta.nome; duracaoMinutos = 30; preco = 150; ativo = $true; intervaloAposMinutos = 10; profissionalIds = @($ana.id, $bruno.id) }
Status 'S-07' 'Adicionar Bruno à Consulta' $r 200
Confere 'S-07b' 'Consulta com Ana e Bruno' (@($r.Json.profissionalIds).Count -eq 2)
$r = Chamar PUT "/api/salao/servicos/$($consulta.id)" @{ nome = $consulta.nome; duracaoMinutos = 30; preco = 150; ativo = $true; intervaloAposMinutos = 10 }
Confere 'S-07c' 'Salvar sem profissionalIds mantém os vínculos' ($r.Status -eq 200 -and @($r.Json.profissionalIds).Count -eq 2)
$r = Chamar PUT "/api/salao/servicos/$($consulta.id)" @{ nome = $consulta.nome; duracaoMinutos = 30; preco = 150; ativo = $true; intervaloAposMinutos = 10; profissionalIds = @($ana.id) }
Confere 'S-07d' 'Voltar Consulta só para Ana' ($r.Status -eq 200 -and @($r.Json.profissionalIds).Count -eq 1)
$r = Chamar GET "/api/salao/servicos?search=E2E%20$sufixo&page=1&pageSize=10"
Status 'S-09' 'Buscar serviços' $r 200
Confere 'S-09b' 'Busca acha os 3 serviços' ([int]$r.Json.total -eq 3) "(veio $($r.Json.total))"
Confere 'S-09c' 'Lista traz os profissionais para "Quem atende"' (@($r.Json.profissionais | Where-Object { $_.id -eq $ana.id }).Count -eq 1)

# ---------------------------------------------------------------- Clientes
Write-Host "`nClientes"
$tel = { '119' + (Get-Random -Minimum 10000000 -Maximum 99999999) }
$r = Chamar POST '/api/salao/clientes' @{ nome = "Maria E2E $sufixo"; telefone = (& $tel); ativo = $true }
Status 'CL-01' 'Criar Maria' $r 201
$maria = $r.Json
$r = Chamar POST '/api/salao/clientes' @{ nome = "João E2E $sufixo"; telefone = (& $tel); ativo = $true }
Status 'CL-01b' 'Criar João' $r 201
$joao = $r.Json
$r = Chamar POST '/api/salao/clientes' @{ nome = "Telefone ruim $sufixo"; telefone = '123'; ativo = $true }
Status 'CL-02' 'Telefone inválido é recusado' $r 400
$r = Chamar POST '/api/salao/clientes' @{ nome = "Mesmo telefone $sufixo"; telefone = $maria.telefone; ativo = $true }
Confere 'CL-03' 'Telefone repetido é recusado (R-29: decidir se deve aceitar)' ($r.Status -eq 409) "(veio $($r.Status))"
$r = Chamar GET '/api/salao/clientes?ativo=true&page=1&pageSize=200'
Confere 'CL-05' 'Lista de clientes com pageSize 200 respeita o pedido (R-07)' ($r.Status -eq 200 -and [int]$r.Json.pageSize -ge 100) "(pageSize devolvido: $($r.Json.pageSize))"

# ---------------------------------------------------------------- Opções da agenda
Write-Host "`nOpções da agenda"
$r = Chamar GET '/api/salao/agendamentos/opcoes'
Status 'A-00' 'Opções da agenda' $r 200
Confere 'A-00b' 'Fuso da empresa nas opções' ($r.Json.fusoHorario -eq 'America/Sao_Paulo') "(veio $($r.Json.fusoHorario))"
$cons = $r.Json.servicos | Where-Object { $_.id -eq $consulta.id }
Confere 'A-00c' 'Consulta nas opções com preparo e quem atende' ($cons.intervaloAposMinutos -eq 10 -and (@($cons.profissionalIds) -join ',') -eq "$($ana.id)")
$anaOp = $r.Json.profissionais | Where-Object { $_.id -eq $ana.id }
Confere 'A-01' 'Expediente de Ana com pausa nas opções' (@($anaOp.disponibilidades | Where-Object { $_.diaSemana -eq 1 }).Count -eq 2)

# ---------------------------------------------------------------- Agendamentos
Write-Host "`nAgendamentos (segunda $(Dia $segunda))"
function NovoAg($cliente, $prof, $servicos, [DateTime]$dia, [string]$hora, $obs = $null) {
    Chamar POST '/api/salao/agendamentos' @{ clienteId = $cliente.id; profissionalId = $prof.id; servicoIds = @($servicos | ForEach-Object { $_.id }); inicio = (Utc $dia $hora); observacoes = $obs }
}
$r = NovoAg $maria $ana @($consulta) $segunda '10:00'
Status 'AG-08' 'Ana, Consulta 10:00' $r 201
$ag1 = $r.Json
Confere 'AG-08b' 'Fim 10:30 e preparo 10 gravados' ($ag1.intervaloAposMinutos -eq 10 -and ([DateTime]$ag1.fim).ToUniversalTime().ToString('HH:mm') -eq '13:30') "(fim $($ag1.fim), preparo $($ag1.intervaloAposMinutos))"
$r = NovoAg $joao $ana @($consulta) $segunda '10:00'
Status 'AG-09' 'Mesmo horário é recusado' $r 409 'já tem outro agendamento'
$r = NovoAg $joao $ana @($retorno) $segunda '10:30'
Status 'AG-10' 'Colado sem o preparo é recusado' $r 409 'preparo'
$r = NovoAg $joao $ana @($retorno) $segunda '10:40'
Status 'AG-10b' 'Depois do preparo é aceito (10:40)' $r 201
$ag2 = $r.Json
$r = NovoAg $joao $ana @($consulta) $segunda '11:45'
Status 'AG-11' 'Atravessando o almoço é recusado' $r 409 'Fora do expediente'
$r = NovoAg $joao $ana @($consulta) $segunda '12:15'
Status 'AG-11b' 'Dentro da pausa é recusado' $r 409 'Fora do expediente'
$r = NovoAg $joao $ana @($consulta) $segunda '13:00'
Status 'AG-11c' 'Logo depois da pausa é aceito (13:00)' $r 201
$ag3 = $r.Json
$r = NovoAg $joao $ana @($consulta) $segunda '08:30'
Status 'AG-11d' 'Antes do expediente é recusado' $r 409 'Fora do expediente'
$r = NovoAg $joao $ana @($avaliacao) $segunda '14:00'
Status 'AG-04' 'Ana não atende Avaliação' $r 409 'não atende'
$r = NovoAg $joao $ana @($retorno) $sabado '10:00'
Status 'AG-12' 'Ana não atende no sábado' $r 409 'não atende neste dia'
$r = NovoAg $joao $bruno @($avaliacao) $sabado '10:00'
Status 'AG-12b' 'Bruno, Avaliação no sábado 10:00' $r 201
$r = NovoAg $joao $bruno @($retorno, $avaliacao) $segunda '16:00'
Status 'AG-07' 'Dois serviços somados passam do expediente (16:00 + 75 min)' $r 409 'Fora do expediente'
$r = NovoAg $joao $bruno @($retorno, $avaliacao) $segunda '15:30'
Status 'AG-07b' 'Dois serviços somados cabem (15:30–16:45)' $r 201
Confere 'AG-07c' 'Duração e preço somados' ($r.Json.duracaoMinutos -eq 75 -and [decimal]$r.Json.preco -eq 200) "(duração $($r.Json.duracaoMinutos), preço $($r.Json.preco))"
$r = Chamar POST '/api/salao/agendamentos' @{ clienteId = $maria.id; profissionalId = $ana.id; servicoIds = @($consulta.id); inicio = (Utc $hoje.AddDays(-7) '10:00') }
Confere 'AG-14' 'Marcar no passado é recusado (R-20)' ($r.Status -ge 400) "(veio $($r.Status))"

# ---------------------------------------------------------------- Horários livres
Write-Host "`nHorários livres"
$r = Chamar GET "/api/salao/agendamentos/horarios-livres?data=$(Dia $segunda)&servicoIds=$($consulta.id)&profissionalId=$($ana.id)"
Status 'HL-01' 'Horários livres de Ana para Consulta' $r 200
$horas = Horas $r
Confere 'HL-02' '09:00 e 09:15 livres' (($horas -contains '09:00') -and ($horas -contains '09:15')) "($($horas -join ' '))"
Confere 'HL-03' '09:30 ocupado (o preparo encostaria nas 10:00)' (-not ($horas -contains '09:30'))
Confere 'HL-04' '10:00 a 10:45 ocupados' (-not ($horas | Where-Object { $_ -in '10:00', '10:15', '10:30', '10:45' }))
Confere 'HL-05' '11:00 e 11:30 livres; 11:45 não (cruza o almoço)' (($horas -contains '11:00') -and ($horas -contains '11:30') -and -not ($horas -contains '11:45'))
Confere 'HL-06' 'Nada entre 12:00 e 12:45 (pausa)' (-not ($horas | Where-Object { $_ -like '12:*' }))
Confere 'HL-07' '13:00 ocupado e 13:45 livre (13:00 + 30 + 10 de preparo = 13:40)' ((-not ($horas -contains '13:00')) -and ($horas -contains '13:45'))
Confere 'HL-08' 'Último horário 17:30' ($horas.Count -gt 0 -and $horas[-1] -eq '17:30') "(último $(if ($horas.Count) { $horas[-1] }))"
$r = Chamar GET "/api/salao/agendamentos/horarios-livres?data=$(Dia $segunda)&servicoIds=$($avaliacao.id)"
Confere 'HL-09' 'Avaliação: só Bruno aparece' ($r.Status -eq 200 -and @($r.Json.profissionais).Count -eq 1 -and $r.Json.profissionais[0].profissionalId -eq $bruno.id)
$r = Chamar GET "/api/salao/agendamentos/horarios-livres?data=$(Dia $segunda)&servicoIds=$($retorno.id)"
Confere 'HL-10' 'Retorno: Ana e Bruno aparecem' ($r.Status -eq 200 -and @($r.Json.profissionais | Where-Object { $_.profissionalId -in $ana.id, $bruno.id }).Count -eq 2)
$r = Chamar GET "/api/salao/agendamentos/horarios-livres?data=$(Dia $sabado)&servicoIds=$($retorno.id)&profissionalId=$($ana.id)"
Confere 'HL-11' 'Ana no sábado: nenhum horário' ($r.Status -eq 200 -and @($r.Json.profissionais[0].horarios).Count -eq 0)
$r = Chamar GET "/api/salao/agendamentos/horarios-livres?data=$(Dia $segunda)&servicoIds=$($consulta.id)&profissionalId=$($ana.id)&ignorarAgendamentoId=$($ag1.id)"
$horasIgn = Horas $r
Confere 'AG-16' 'Ao remarcar, o próprio horário (10:00) aparece livre' ($horasIgn -contains '10:00')
$r = Chamar GET "/api/salao/agendamentos/horarios-livres?data=$(Dia $hoje.AddDays(-7))&servicoIds=$($retorno.id)&profissionalId=$($bruno.id)"
Confere 'HL-12' 'Dia no passado: nenhum horário' ($r.Status -eq 200 -and @($r.Json.profissionais[0].horarios).Count -eq 0)

# ---------------------------------------------------------------- Edição e status
Write-Host "`nEdição e status"
$r = Chamar PUT "/api/salao/agendamentos/$($ag1.id)" @{ clienteId = $maria.id; profissionalId = $ana.id; servicoIds = @($consulta.id); inicio = $ag1.inicio; observacoes = 'Trazer exames' }
Status 'AG-15' 'Mudar só a observação' $r 200
$r = Chamar PUT "/api/salao/agendamentos/$($ag1.id)" @{ clienteId = $maria.id; profissionalId = $ana.id; servicoIds = @($consulta.id); inicio = (Utc $segunda '09:00') }
Status 'AG-16b' 'Remarcar 10:00 → 09:00' $r 200
$r = Chamar PUT "/api/salao/agendamentos/$($ag1.id)" @{ clienteId = $maria.id; profissionalId = $ana.id; servicoIds = @($avaliacao.id); inicio = (Utc $segunda '09:00') }
Status 'AG-04b' 'Trocar para serviço que Ana não faz é recusado' $r 409 'não atende'
$r = Chamar PATCH "/api/salao/agendamentos/$($ag1.id)/status" @{ status = 'confirmado' }
Status 'ST-01' 'Confirmar' $r 200
$r = Chamar PATCH "/api/salao/agendamentos/$($ag1.id)/status" @{ status = 'concluido' }
Confere 'ST-04' 'Concluir agendamento futuro é recusado (R-13)' ($r.Status -ge 400) "(veio $($r.Status))"
$r = Chamar PATCH "/api/salao/agendamentos/$($ag1.id)/status" @{ status = 'no_show' }
Confere 'ST-04b' 'Falta em agendamento futuro é recusada (R-13)' ($r.Status -ge 400) "(veio $($r.Status))"
$r = Chamar PATCH "/api/salao/agendamentos/$($ag2.id)/status" @{ status = 'cancelado' }
Status 'ST-03' 'Cancelar' $r 200
$r = Chamar PUT "/api/salao/agendamentos/$($ag2.id)" @{ clienteId = $joao.id; profissionalId = $ana.id; servicoIds = @($retorno.id); inicio = $ag2.inicio; observacoes = 'x' }
Status 'ST-03b' 'Agendamento cancelado não pode ser alterado' $r 409
$r = NovoAg $maria $ana @($retorno) $segunda '10:40'
Status 'ST-03c' 'Horário do cancelado fica livre de novo' $r 201
$r = Chamar PATCH "/api/salao/agendamentos/$($ag1.id)/status" @{ status = 'inexistente' }
Status 'ST-06' 'Status inválido é recusado' $r 400

# ---------------------------------------------------------------- Bloqueios
Write-Host "`nBloqueios"
$r = Chamar POST '/api/salao/bloqueios' @{ profissionalId = $ana.id; inicio = (Utc $terca '00:00'); fim = (Utc $quarta '00:00'); motivo = 'Férias' }
Status 'B-02' 'Bloquear Ana na terça (dia todo)' $r 201
$bFerias = $r.Json.bloqueio
$r = NovoAg $maria $ana @($retorno) $terca '10:00'
Status 'B-04' 'Marcar com Ana no dia bloqueado é recusado' $r 409 'Férias'
$r = NovoAg $maria $bruno @($retorno) $terca '10:00'
Status 'B-04b' 'Bloqueio de Ana não afeta Bruno' $r 201
$r = Chamar GET "/api/salao/agendamentos/horarios-livres?data=$(Dia $terca)&servicoIds=$($retorno.id)&profissionalId=$($ana.id)"
Confere 'B-04c' 'Ana sem horários livres na terça' ($r.Status -eq 200 -and @($r.Json.profissionais[0].horarios).Count -eq 0)
$r = Chamar POST '/api/salao/bloqueios' @{ profissionalId = $null; inicio = (Utc $quarta '14:00'); fim = (Utc $quarta '15:00'); motivo = 'Reunião geral' }
Status 'B-06' 'Bloqueio da empresa toda, quarta 14–15' $r 201
$bReuniao = $r.Json.bloqueio
$r = NovoAg $maria $bruno @($retorno) $quarta '14:30'
Status 'B-05' 'Bloqueio da empresa vale para Bruno' $r 409 'Reunião geral'
$r = NovoAg $maria $bruno @($retorno) $quarta '15:00'
Status 'B-05b' 'Logo após o bloqueio é aceito' $r 201
$r = Chamar POST '/api/salao/bloqueios' @{ profissionalId = $null; inicio = (Utc $quarta '15:00'); fim = (Utc $quarta '14:00') }
Status 'B-07' 'Fim antes do início é recusado' $r 400
$r = Chamar POST '/api/salao/bloqueios' @{ profissionalId = $ana.id; inicio = (Utc $segunda '08:00'); fim = (Utc $segunda '12:00'); motivo = 'Congresso' }
Status 'B-08' 'Bloquear período que já tem agendamentos' $r 201
Confere 'B-08b' 'Aviso conta os agendamentos ativos do período' ($r.Json.agendamentosNoPeriodo -ge 2) "(veio $($r.Json.agendamentosNoPeriodo))"
$bCongresso = $r.Json.bloqueio
$r = Chamar GET "/api/salao/bloqueios?de=$(Utc $segunda '00:00')&ate=$(Utc $sabado '00:00')"
Status 'B-10' 'Listar bloqueios da semana' $r 200
Confere 'B-10b' 'Os 3 bloqueios aparecem' (@($r.Json.items | Where-Object { $_.id -in $bFerias.id, $bReuniao.id, $bCongresso.id }).Count -eq 3)
$r = Chamar GET "/api/salao/bloqueios?de=$(Utc $segunda '00:00')&ate=$(Utc $sabado '00:00')&profissionalId=$($bruno.id)"
Confere 'B-10c' 'Filtro por Bruno traz só o da empresa' ((@($r.Json.items | Where-Object { $_.id -in $bFerias.id, $bReuniao.id, $bCongresso.id }) | ForEach-Object { $_.id }) -join ',' -eq "$($bReuniao.id)")
foreach ($b in $bFerias, $bReuniao, $bCongresso) { $null = Chamar DELETE "/api/salao/bloqueios/$($b.id)" }
$r = NovoAg $joao $ana @($retorno) $terca '10:00'
Status 'B-10d' 'Depois de remover as férias, terça volta a aceitar' $r 201
$r = Chamar DELETE "/api/salao/bloqueios/$($bFerias.id)"
Status 'B-10e' 'Remover de novo dá 404' $r 404

# ---------------------------------------------------------------- Fuso
Write-Host "`nFuso horário"
$null = Chamar PUT '/api/salao/configuracao' @{ segmento = 'saude'; fusoHorario = 'America/Manaus'; terminologia = $null }
$r = Chamar GET "/api/salao/agendamentos/horarios-livres?data=$(Dia $quarta)&servicoIds=$($retorno.id)&profissionalId=$($bruno.id)"
$primeiro = if ($r.Json.profissionais) { $r.Json.profissionais[0].horarios[0] } else { $null }
Confere 'F-03' 'Empresa em Manaus: 08:00 local = 12:00 UTC' ($primeiro.hora -eq '08:00' -and ([DateTime]$primeiro.inicio).ToUniversalTime().ToString('HH:mm') -eq '12:00') "(hora $($primeiro.hora), inicio $($primeiro.inicio))"
$r = Chamar POST '/api/salao/agendamentos' @{ clienteId = $maria.id; profissionalId = $bruno.id; servicoIds = @($retorno.id); inicio = (Utc $quarta '07:30' -4) }
Status 'F-03b' 'Manaus: 07:30 local é antes do expediente' $r 409 'Fora do expediente'
$null = Chamar PUT '/api/salao/configuracao' @{ segmento = 'saude'; fusoHorario = 'America/Sao_Paulo'; terminologia = $null }

# ---------------------------------------------------------------- Permissões
Write-Host "`nPermissões"
$r = Chamar GET '/api/salao/agendamentos/opcoes' $null $tokenRecepcao
Status 'PM-02' 'Recepção abre a agenda' $r 200
$r = Chamar GET '/api/salao/servicos' $null $tokenRecepcao
Status 'PM-03' 'Recepção não lista serviços' $r 403
$r = Chamar GET "/api/salao/agendamentos/clientes?busca=Maria%20E2E%20$sufixo" $null $tokenRecepcao
Confere 'PM-04' 'Recepção busca clientes para agendar (R-06)' ($r.Status -eq 200 -and @($r.Json.items).Count -eq 1 -and $r.Json.items[0].id -eq $maria.id) "(veio $($r.Status), $(@($r.Json.items).Count) itens)"
$r = Chamar GET '/api/salao/clientes?ativo=true' $null $tokenRecepcao
Status 'PM-04b' 'Recepção continua sem abrir o cadastro de clientes' $r 403
$r = Chamar GET '/api/salao/agendamentos/clientes?limite=500'
Confere 'PM-04c' 'Busca da agenda devolve no máximo 50' ($r.Status -eq 200 -and [int]$r.Json.pageSize -eq 50) "(pageSize $($r.Json.pageSize))"
$r = Chamar POST '/api/salao/bloqueios' @{ profissionalId = $null; inicio = (Utc $sabado '20:00'); fim = (Utc $sabado '21:00'); motivo = 'teste recepção' } $tokenRecepcao
Status 'PM-04b' 'Recepção cria bloqueio' $r 201
if ($r.Json) { $null = Chamar DELETE "/api/salao/bloqueios/$($r.Json.bloqueio.id)" $null $tokenRecepcao }
$r = Chamar GET '/api/salao/configuracao/opcoes' $null $tokenRecepcao
Status 'PM-05' 'Recepção não abre opções de configuração' $r 403
$semEmpresa = Token $todos $false
$r = Chamar GET '/api/salao/agendamentos/opcoes' $null $semEmpresa @{ 'X-Empresa-Id' = "$EmpresaId" }
Confere 'R-02' 'Token sem empresa NÃO pode escolher a empresa por cabeçalho' ($r.Status -eq 403) "(veio $($r.Status))"
$r = Chamar GET "/api/salao/agendamentos?de=2000-01-01T00:00:00Z&ate=2100-01-01T00:00:00Z"
Confere 'R-18' 'Listagem com período enorme é limitada' ($r.Status -eq 400) "(veio $($r.Status))"

# ---------------------------------------------------------------- Desativação
Write-Host "`nDesativação"
$r = Chamar PATCH "/api/salao/servicos/$($consulta.id)/ativo" @{ ativo = $false }
Status 'S-10' 'Desativar Consulta' $r 200
$r = Chamar GET '/api/salao/agendamentos/opcoes'
Confere 'S-10b' 'Consulta some das opções' (-not ($r.Json.servicos | Where-Object { $_.id -eq $consulta.id }))
$r = Chamar PUT "/api/salao/agendamentos/$($ag3.id)" @{ clienteId = $joao.id; profissionalId = $ana.id; servicoIds = @($consulta.id); inicio = $ag3.inicio; observacoes = 'só a observação' }
Confere 'AG-17' 'Editar observação de agendamento com serviço desativado (R-08)' ($r.Status -eq 200) "(veio $($r.Status): $($r.Json.message))"
Confere 'AG-17b' 'Preço e duração gravados são mantidos (R-09)' ($r.Status -eq 200 -and [decimal]$r.Json.preco -eq [decimal]$ag3.preco -and $r.Json.fim -eq $ag3.fim) "(preço $($r.Json.preco) / $($ag3.preco))"
$r = Chamar PUT "/api/salao/agendamentos/$($ag3.id)" @{ clienteId = $joao.id; profissionalId = $ana.id; servicoIds = @($consulta.id, $retorno.id); inicio = $ag3.inicio }
Confere 'AG-17c' 'Trocar a lista com serviço desativado é recusado' ($r.Status -eq 400 -and $r.Json.message -like '*inativo*') "(veio $($r.Status): $($r.Json.message))"
$null = Chamar PATCH "/api/salao/servicos/$($consulta.id)/ativo" @{ ativo = $true }

# ---------------------------------------------------------------- Resultado
$total = $script:ok + $script:falhas.Count
Write-Host "`nResultado: $($script:ok) de $total passaram." -ForegroundColor Cyan
if ($script:falhas.Count) {
    Write-Host "Falhas:" -ForegroundColor Red
    $script:falhas | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
