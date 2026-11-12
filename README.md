# api.salao — Lyra

API de negócio do **Lyra** (Salão). .NET 8, camadas:

```
src/
  Lyra.API            → HTTP, JWT, controllers
  Lyra.Application    → casos de uso / contratos
  Lyra.Infrastructure → EF Core (schema `lyra`), accessor do usuário
  Lyra.Core           → claims Enterprise, códigos de módulo
```

Não duplica `usuarios`. O JWT é o **mesmo** emitido pelo oAuth (via Gateway).

## Integração Enterprise

| Peça | Valor |
|------|--------|
| Sistema (`core.sistemas.codigo`) | `LYR` |
| Módulo de segurança | `LYRA000000` (legado `LYR0000000`) |
| Gateway | `/api/salao/**` |
| Eureka / service id | `salao` |
| Porta local | `8092` |
| Schema Postgres | `lyra` |

Front: `app.salao` → só chama `http://localhost:8080` (Gateway) + `X-Secret-Token` + Bearer.

## Pré-requisitos

- .NET 8 SDK
- Gateway + oAuth + Core no ar
- Seed: `infra/seed-sistema.sql` (neste repo)
- `JWT_SECRET` **igual** ao oAuth/Gateway

## Rodar local

```bash
# na pasta api.salao
dotnet restore
dotnet run --project src/Lyra.API
# http://localhost:8092/health
# Swagger: http://localhost:8092/swagger
```

Gateway local (sem Eureka) precisa de:

```
GATEWAY_SALAO_URI=http://localhost:8092
```

Smoke (depois do login no front):

```http
GET http://localhost:8080/api/salao/me
Authorization: Bearer <access_token>
X-Secret-Token: <FRONTEND_SECRET_TOKEN>
```

Sem o módulo `LYRA000000` (ou legado `LYR0000000`) no JWT → **403**.

## Portal do cliente

Agendamento online em `/api/salao/publico/portal/{endereco}` (rota `salao-public` do Gateway, sem o filtro de JWT). O cliente não é usuário do Core: a conta fica em `lyra.contas_cliente`, entra com código por e-mail e recebe um token próprio (esquema `Cliente`, audiência `lyra-portal`, chave derivada do `JWT_SECRET`). Esse token só vale nas rotas do portal, e o JWT da equipe não vale nelas. O código sai pela AW.Mensageria (`GATEWAY_INTERNAL_TOKEN`); sem ela, em desenvolvimento, o código aparece no log. Detalhes em `app.salao/docs/lyra-proximos-passos.md`, Etapa 2.

## Testes

`tests/Lyra.Tests` sobe a API em memória (ambiente `Testing`, token assinado de verdade) contra um banco criado só para a execução e apagado no fim. Cada teste usa uma empresa nova. Nunca aponte para o banco de homologação ou produção.

```bash
docker run -d --name lyra-pg-testes -e POSTGRES_PASSWORD=postgres -p 55432:5432 postgres:16-alpine
LYRA_TEST_DB="Host=localhost;Port=55432;Username=postgres;Password=postgres" dotnet test tests/Lyra.Tests
```

Sem `LYRA_TEST_DB` os testes são pulados (no CI, falham). O GitHub Actions roda a suíte em todo push na `develop` e antes de cada deploy em `production`.

## O que NÃO fazer

- Novo login / tabela de usuários
- Chamar Core `:8081` do browser
- Inventar códigos de módulo fora de `LYRA*` / `LYR*`
