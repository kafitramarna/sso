FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Sso.sln ./
COPY src/Sso.Domain/Sso.Domain.csproj src/Sso.Domain/
COPY src/Sso.Application/Sso.Application.csproj src/Sso.Application/
COPY src/Sso.Infrastructure/Sso.Infrastructure.csproj src/Sso.Infrastructure/
COPY src/Sso.Api/Sso.Api.csproj src/Sso.Api/
RUN dotnet restore src/Sso.Api/Sso.Api.csproj
COPY src/ src/
RUN dotnet publish src/Sso.Api/Sso.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Sso.Api.dll"]
