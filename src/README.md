# Código-Fonte (`src/`)

Esta pasta contém o projeto principal da API RESTful do Zelo em .NET 8:

- **`Zelo.Api/`**: Minimal APIs agrupadas por recurso (`/api/v1`), Entity Framework Core (`ZeloDbContext` e `DbSeeder`), Autenticação JWT HS256, Middleware de Idempotência (`Idempotency-Key`), Rate Limiting (`HTTP 429`), Health Checks (`/health`) e documentação OpenAPI/Swagger (`/swagger`).
