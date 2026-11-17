# ==============================================================================
# Estágio 1: Build (.NET 8 SDK)
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia arquivos de projetos para restaurar dependências em cache
COPY ["src/Zelo.Api/Zelo.Api.csproj", "src/Zelo.Api/"]
COPY ["tests/Zelo.Tests/Zelo.Tests.csproj", "tests/Zelo.Tests/"]
RUN dotnet restore "src/Zelo.Api/Zelo.Api.csproj"

# Copia o restante do código-fonte e gera o publish em modo Release
COPY . .
WORKDIR "/src/src/Zelo.Api"
RUN dotnet publish "Zelo.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ==============================================================================
# Estágio 2: Runtime enxuto (.NET 8 ASP.NET Runtime) - Sem segredos na imagem
# ==============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Porta padrão do serviço zelo-cuidado conforme contrato compartilhado
ENV ASPNETCORE_URLS=http://+:5080
EXPOSE 5080

COPY --from=build /app/publish .

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:5080/health || exit 1

ENTRYPOINT ["dotnet", "Zelo.Api.dll"]
