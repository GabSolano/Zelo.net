# Zelo API (.NET 8) — `zelo-api-dotnet` (`zelo-cuidado`)

> **Global Solution 2026/2 · FIAP — Tema: CitizenTech**  
> **Disciplina:** Advanced Business Development with .NET  
> **Lema do Zelo:** *Cuidado que não vigia.*

---

## 1. Sobre o Projeto e a Solução CitizenTech

O **Zelo** é uma plataforma de monitoramento preventivo e respeitoso para idosos que moram sozinhos em áreas urbanas, utilizando **apenas sensores binários de presença e abertura de porta** (sem câmeras e sem microfones de ambiente, preservando a privacidade e a LGPD). Quando a rotina foge do esperado, o sistema realiza primeiro um *check-in* com o idoso e, caso não haja resposta ou ele peça ajuda, escala em cadeia para o familiar responsável e para o vizinho guardião até que alguém confirme *"Vou verificar"*.

A **`zelo-api-dotnet` (`zelo-cuidado`)** é a **API principal em .NET 8 (Minimal APIs)** do ecossistema:
- Centraliza a **autenticação e emissão de tokens JWT (HS256)** compartilhados com o serviço Java (`zelo-monitor`) e o App Mobile (`zelo-app`);
- Gerencia o cadastro de **idosos, consentimento LGPD, casas, sensores, rede de apoio escalonada e dispositivos push**;
- Fornece o **CRUD completo de lembretes de remédio** (texto livre + confirmação diária *"Tomei"*) consumido pelo App Mobile e o serviço em segundo plano que marca lembretes não confirmados após 60 minutos (`NAO_CONFIRMADO`), acionando a regra `R5` no serviço Java.

---

## 2. Integrantes do Grupo

| Nome | RM | Responsabilidades no Zelo |
| :--- | :---: | :--- |
| **Gabriel Costa Solano** | **562325** | Advanced Business Development with .NET, Banco de Dados, Compliance/QA |
| **Leandro Guarido de Oliveira** | **561760** | Java Advanced (`zelo-monitor`), Mobile Application Development (`zelo-app`) |
| **Kaiky Pereira Rodrigues Da Silva** | **564578** | DevOps Tools & Cloud Computing, Disruptive Architectures (IoT, IoB & IA) |

---

## 3. Atendimento aos 6 Requisitos Oficiais do PDF (100 pts)

| # | Requisito Oficial (FIAP) | Pts | Implementação no Projeto |
| :-: | :--- | :-: | :--- |
| **01** | **Minimal API RESTful funcional**, com validações, códigos HTTP e documentação OpenAPI/Swagger | **20** | Endpoints agrupados por recurso (`MapGroup("/api/v1/...")`) em `src/Zelo.Api/Endpoints/`, validações de domínio com respostas padronizadas `{"codigo":"...","mensagem":"..."}` e status HTTP semânticos (`200`, `201`, `204`, `400`, `401`, `403`, `404`, `409`, `429`). Documentação interativa Swagger UI em `/swagger`. |
| **02** | **Autenticação e autorização com JWT**, protegendo endpoints e validando tokens | **20** | `JwtTokenService` emitindo JWT **HS256** com as claims `sub` (`usuarioId`) e `papel` (`IDOSO`, `FAMILIAR`, `GUARDIAO`, `ADMIN`, `SERVICO`), assinado com `JWT_SECRET` (via variável de ambiente). |
| **03** | **Implementação de idempotência** para evitar operações duplicadas em requisições repetidas | **15** | `IdempotencyMiddleware` + tabela `IDEMPOTENCY_RECORD` (retenção de 24h). Requisições `POST` com header `Idempotency-Key` e mesmo hash SHA-256 do corpo devolvem a resposta original salva (`X-Idempotent-Replay: true`) sem duplicar registros; mesma chave com corpo diferente devolve `409 Conflict`. |
| **04** | **Rate Limiting funcional**, com limites de requisições e tratamento HTTP 429 | **15** | `Microsoft.AspNetCore.RateLimiting` (`AddRateLimiter` / `UseRateLimiter`) com política global e política restrita `"auth"` para `/api/v1/auth`. Quando excedido, retorna `429 Too Many Requests` + header `Retry-After` + JSON de erro. |
| **05** | **Testes unitários e de integração com xUnit.net**, validando regras de negócio e endpoints | **20** | Projeto `tests/Zelo.Tests`: **15 testes automatizados** (9 unitários das regras de negócio em `ZeloBusinessRulesTests` + 6 testes de integração ponta a ponta com `WebApplicationFactory<Program>` em `ZeloApiIntegrationTests`). |
| **06** | **Health Checks** para monitoramento da API, banco de dados e serviços externos utilizados | **10** | Endpoints `GET /health` e `GET /api/v1/health` verificando: `api` (self), `database` (`ZeloDbContext` EF Core) e `zelo-monitor` (`MonitorExternalHealthCheck` consultando `/actuator/health` da API Java). |

---

## 4. Estrutura do Repositório

```text
zelo-api-dotnet/
├── dockerfiles/
│   └── Dockerfile                  # Multi-stage build (.NET 8 SDK -> ASP.NET Runtime na porta 5080)
├── scripts/
│   ├── script-bd.sql               # DDL + Seed para Azure SQL / SQL Server (schema CUIDADO)
│   └── azure-infra.sh              # Script Azure CLI para provisionamento de nuvem (DevOps)
├── src/
│   └── Zelo.Api/
│       ├── Contracts/              # DTOs de entrada e saída (camelCase)
│       ├── Domain/
│       │   ├── Entities/           # Entidades EF Core (Usuario, Idoso, Casa, Sensor, Lembrete, etc.)
│       │   ├── Enums/              # Enums do contrato (IDOSO, FAMILIAR, GUARDIAO, QUARTO, etc.)
│       │   └── Services/           # Regras de negócio puras (ZeloBusinessRules), Relógio e Hash
│       ├── Endpoints/              # Minimal APIs (Auth, Idosos, Casas, RedeApoio, Lembretes)
│       ├── Infrastructure/
│       │   ├── Background/         # Worker de expiração de lembretes (60 min -> NAO_CONFIRMADO / R5)
│       │   ├── Data/               # ZeloDbContext e DbSeeder (dados fixos da Dona Maria)
│       │   ├── Health/             # Health Check do serviço externo zelo-monitor
│       │   ├── Idempotency/        # Middleware de Idempotência (Idempotency-Key)
│       │   └── Security/           # Emissão e leitura de claims JWT HS256
│       └── Program.cs              # Configuração do pipeline ASP.NET Core 8
├── tests/
│   └── Zelo.Tests/
│       ├── Unit/                   # Testes unitários das regras de negócio (xUnit)
│       └── Integration/            # Testes de integração dos endpoints (WebApplicationFactory)
├── .env.example                    # Exemplo de variáveis de ambiente (sem segredos no código)
├── azure-pipeline.yml              # Pipeline de CI para o Azure DevOps
├── identificacao-grupo.txt         # Identificação oficial do grupo para entrega FIAP
└── Zelo.sln
```

---

## 5. Como Executar Localmente

### Pré-requisitos
- .NET 8 SDK (ou superior)

### 1. Configurar variáveis de ambiente (opcional em desenvolvimento)
Por padrão, em modo `Development`, a API utiliza um banco SQLite local (`zelo_dev.db`) criado e populado automaticamente no boot com os dados fixos da **Dona Maria, Ana e Seu José**. Para produção/Azure SQL ou Docker, copie o `.env.example`:

```bash
cp .env.example .env
```

Variáveis suportadas:
- `DB_PROVIDER`: `Sqlite` (padrão local), `SqlServer` (Azure SQL / container) ou `InMemory`
- `ConnectionStrings__Default`: string de conexão do banco
- `JWT_SECRET`: segredo compartilhado com o Java (mínimo 32 caracteres)
- `MONITOR_URL`: URL base do `zelo-monitor` (padrão: `http://localhost:8080`)
- `MODO_SIMULACAO`: `true` ou `false`

### 2. Subir a API (Porta `5080`)
```bash
dotnet run --project src/Zelo.Api/Zelo.Api.csproj --urls "http://localhost:5080"
```
- **Swagger UI:** `http://localhost:5080/swagger`
- **Health Check:** `http://localhost:5080/api/v1/health` (ou `http://localhost:5080/health`)

### 3. Executar os Testes Automatizados (xUnit)
```bash
dotnet test
```

---

## 6. Dados Fixos de Teste (Seed Automático)

A API já sobe com os dados fixos do Documento-base do Zelo prontos para uso pelo Mobile e pelo Java:

| Personagem / Entidade | Papel / Tipo | Email de Login | Senha | UUID Fixo |
| :--- | :---: | :--- | :---: | :--- |
| **Usuária Dona Maria** | `IDOSO` | `maria@zelo.teste` | `123456` | `00000000-0000-0000-0000-000000000001` |
| **Usuária Ana (Filha)** | `FAMILIAR` (ordem 1) | `ana@zelo.teste` | `123456` | `00000000-0000-0000-0000-000000000002` |
| **Usuário Seu José (Vizinho)** | `GUARDIAO` (ordem 2) | `jose@zelo.teste` | `123456` | `00000000-0000-0000-0000-000000000003` |
| **Administrador Zelo** | `ADMIN` | `admin@zelo.teste` | `123456` | `00000000-0000-0000-0000-000000000004` |
| **Serviço Interno (Java)** | `SERVICO` | `servico@zelo.teste` | `123456` | `00000000-0000-0000-0000-000000000005` |
| **Idoso Dona Maria** | Entidade `Idoso` | — | — | `00000000-0000-0000-0000-000000000010` |
| **Casa da Dona Maria** | Entidade `Casa` | — | — | `00000000-0000-0000-0000-000000000100` |
| **Sensores (5 cômodos)** | `QUARTO` a `ENTRADA` | — | — | `00000000-0000-0000-0000-000000001001` a `...1005` |
| **Lembrete das 08:00** | `"Remédio da pressão"` | — | — | `00000000-0000-0000-0000-000000002001` |

---

## 7. Endpoints Principais (`/api/v1`)

| Método e Rota | Uso Principal | Consumidor |
| :--- | :--- | :--- |
| `POST /api/v1/auth/signup` | Cadastro de usuário com suporte a `Idempotency-Key` | App Mobile |
| `POST /api/v1/auth/login` | Login com emissão de JWT HS256 (`sub`, `papel`) | App Mobile / Java |
| `POST /api/v1/usuarios/{id}/dispositivos` | Registro de `pushToken` (Expo Push) | App Mobile |
| `GET, POST /api/v1/idosos` | Listagem e cadastro de idosos | App Mobile / Admin |
| `GET, PUT, DELETE /api/v1/idosos/{id}` | Consulta, edição e remoção de idoso | App Mobile / Admin |
| `GET, POST /api/v1/idosos/{id}/consentimentos` | Consulta, aceite e revogação do termo LGPD pelo próprio idoso | App Mobile |
| `GET, POST /api/v1/casas` e `GET, PUT, DELETE /api/v1/casas/{id}` | Gestão da residência monitorada | Admin / Setup |
| `GET, POST /api/v1/casas/{id}/sensores` | Sensores instalados (1 por cômodo por casa) | Admin / Setup |
| `GET /api/v1/casas/{id}/rede-apoio` | Rede de apoio ordenada + `pushTokens` + `consentimentoAtivo` | **Java (`zelo-monitor`)** |
| `GET, POST /api/v1/idosos/{id}/contatos` | Consulta e adição de contatos na rede de apoio (ordem única) | App Mobile |
| `PUT, DELETE /api/v1/contatos/{id}` | Atualização de ordem/papel e remoção de contato | App Mobile |
| `GET, POST /api/v1/idosos/{id}/lembretes` | CRUD de lembretes (suporta `Idempotency-Key` no `POST`) | **App Mobile** |
| `PUT, DELETE /api/v1/lembretes/{id}` | Edição e exclusão de lembrete | **App Mobile** |
| `GET /api/v1/idosos/{id}/lembretes/hoje` | Lembretes do dia atual (`PENDENTE`, `CONFIRMADO`, `NAO_CONFIRMADO`) | **App Mobile (Idoso)** |
| `POST /api/v1/lembretes/registros/{id}/confirmacao` | Idoso confirma `"Tomei"` (suporta `Idempotency-Key`) | **App Mobile (Idoso)** |
| `GET /api/v1/lembretes/nao-confirmados?desde=` | Lista registros que expiraram sem confirmação após 60 min | **Java (`zelo-monitor`)** |
| `GET /api/v1/health` (e `GET /health`) | Health check detalhado da API, Banco e `zelo-monitor` | **DevOps / Azure** |
