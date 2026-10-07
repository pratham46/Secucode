FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY server/Secucode.Contracts/Secucode.Contracts.csproj server/Secucode.Contracts/
COPY server/Secucode.Api/Secucode.Api.csproj server/Secucode.Api/
RUN dotnet restore server/Secucode.Api/Secucode.Api.csproj
COPY server/Secucode.Contracts/ server/Secucode.Contracts/
COPY server/Secucode.Api/ server/Secucode.Api/
RUN dotnet publish server/Secucode.Api/Secucode.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Secucode.Api.dll"]
