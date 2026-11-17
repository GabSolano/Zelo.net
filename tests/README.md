# Testes Automatizados (`tests/`)

Esta pasta contém a suíte de testes automatizados com **xUnit** e **`WebApplicationFactory`**:

- **`Zelo.Tests/Unit/`**: Testes unitários validando as regras de negócio do domínio Zelo (consentimento exclusivo do idoso, ordem única de contatos de apoio, 1 sensor por cômodo por casa, lembrete texto livre e expiração de 60 minutos).
- **`Zelo.Tests/Integration/`**: Testes de integração ponta a ponta validando os endpoints da Minimal API (Login JWT, contrato `/casas/{id}/rede-apoio`, Idempotência `201`/`409`, Rate Limiting `429`, Health Checks e Swagger).
