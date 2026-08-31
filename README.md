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
| Módulo raiz | `LYR0000000` |
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

Sem o módulo `LYR0000000` no JWT → **403**.

## O que NÃO fazer

- Novo login / tabela de usuários
- Chamar Core `:8081` do browser
- Inventar códigos de módulo fora de `LYR*`
